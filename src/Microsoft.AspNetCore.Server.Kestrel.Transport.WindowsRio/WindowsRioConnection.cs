// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio
{
    internal sealed class WindowsRioConnection : IConnectionInformation
    {
        private readonly RioSocket _socket;
        private readonly WindowsRioTransport _transport;
        private IConnectionContext _connectionContext;
        private IPipeWriter _input;
        private IPipeReader _output;

        private const int MinAllocBufferSize = 2048;        // from libuv transport

        public IPEndPoint RemoteEndPoint { get; }

        public IPEndPoint LocalEndPoint { get; }

        public PipeFactory PipeFactory => _transport.TransportFactory.PipeFactory;

        public bool RequiresDispatch => _transport.TransportFactory.ForceDispatch;

        public IScheduler InputWriterScheduler => InlineScheduler.Default;

        public IScheduler OutputReaderScheduler => InlineScheduler.Default;

        internal WindowsRioConnection(RioSocket socket, WindowsRioTransport transport)
        {
            Debug.Assert(socket != null);
            Debug.Assert(transport != null);

            _socket = socket;
            _transport = transport;

            LocalEndPoint = _socket.LocalEndPoint;
            RemoteEndPoint = _socket.RemoteEndPoint;
        }

        public async void Start(IConnectionHandler connectionHandler)
        {
            try
            {
                _connectionContext = connectionHandler.OnConnection(this);

                _input = _connectionContext.Input;
                _output = _connectionContext.Output;

                // Spawn send and receive logic
                Task receiveTask = DoReceive();
                Task sendTask = DoSend();

                // Wait for eiher of them to complete (note they won't throw exceptions)
                await Task.WhenAny(receiveTask, sendTask);

                // Shut the socket down and wait for both sides to end
                _socket.Shutdown(SocketShutdown.Both);

                // Now wait for both to complete
                await receiveTask;
                await sendTask;

                // Dispose the socket
                _socket.Dispose();
            }
            catch (Exception)
            {
                // TODO: Log
            }
            finally
            {
                // Mark the connection as closed after disposal
                _connectionContext.OnConnectionClosed();
            }
        }

        private async Task DoReceive()
        {
            try
            {
                while (true)
                {
                    // Ensure we have some reasonable amount of buffer space
                    var buffer = _input.Alloc(MinAllocBufferSize);

                    try
                    {
                        var receiveResult = await _socket.ReceiveAsync(buffer.Buffer);
                        // Record what data we filled into the buffer and push to pipe
                        buffer.Advance(receiveResult.BytesReceived);

                        if (receiveResult.IsEnd)
                        {
                            // We receive a FIN so throw an exception so that we cancel the input
                            // with an error
                            throw new TaskCanceledException("The request was aborted");
                        }
                    }
                    finally
                    {
                        buffer.Commit();
                    }

                    var result = await buffer.FlushAsync();
                    if (result.IsCompleted)
                    {
                        // Pipe consumer is shut down, do we stop writing
                        _socket.Shutdown(SocketShutdown.Receive);
                        break;
                    }
                }

                _input.Complete();
            }
            catch (Exception ex)
            {
                _connectionContext.Abort(ex);
                _input.Complete(ex);
            }
        }

        private async Task DoSend()
        {
            try
            {
                while (true)
                {
                    var sendResult = await _socket.ReadyToSend();

                    if (_output.TryRead(out var readResult))
                    {
                        var buffer = readResult.Buffer;
                        var consumed = buffer.End;
                        try
                        {
                            if (sendResult.InputConsumed || readResult.IsCancelled || readResult.IsCompleted)
                            {
                                if (!buffer.IsEmpty)
                                {
                                    _socket.SendComplete(buffer);
                                }

                                if (readResult.IsCancelled)
                                {
                                    // Send a FIN
                                    _socket.Shutdown(SocketShutdown.Send);
                                    break;
                                }

                                if (buffer.IsEmpty && readResult.IsCompleted)
                                {
                                    break;
                                }
                            }
                            else if (!buffer.IsEmpty && !buffer.IsSingleSpan)
                            {
                                consumed = _socket.SendPartial(buffer);
                            }
                            else
                            {
                                consumed = buffer.Start;
                            }
                        }
                        finally
                        {
                            _output.Advance(consumed);
                        }
                    }
                }

                // We're done reading
                _output.Complete();
            }
            catch (Exception ex)
            {
                _output.Complete(ex);
            }
        }
    }
}
