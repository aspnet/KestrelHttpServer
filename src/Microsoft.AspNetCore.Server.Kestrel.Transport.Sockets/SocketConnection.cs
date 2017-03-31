// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using System.IO.Pipelines;
using System.Net;
using System.Buffers;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets
{
    public sealed class SocketConnection : IConnectionInformation, ITimeoutControl
    {
        Socket _socket;
        SocketTransport _transport;
        IPipeWriter _input;
        IPipeReader _output;

        private const int MinAllocBufferSize = 2048;        // from libuv transport

        public SocketConnection(Socket socket, SocketTransport transport)
        {
            _socket = socket;
            _transport = transport;
        }

        public async void Start(IConnectionHandler connectionHandler)
        {
            _socket.NoDelay = true;

            IConnectionContext context = connectionHandler.OnConnection(this);

            _input = context.Input;
            _output = context.Output;

            // Spawn send and receive logic
            Task receiveTask = DoReceive();
            Task sendTask = DoSend();

            // Wait for them to complete (note they won't throw exceptions)
            await receiveTask;
            await sendTask;

            _socket.Dispose();
        }
        
        private async Task DoReceive()
        {
            try
            {
                while (true)
                {
                    // Ensure we have some reasonable amount of buffer space
                    WritableBuffer buffer = _input.Alloc(MinAllocBufferSize);

                    int bytesReceived = await _socket.ReceiveAsync(GetArraySegment(buffer.Buffer), SocketFlags.None);

                    if (bytesReceived == 0)
                    {
                        // EOF
                        buffer.Commit();
                        _input.Complete();
                        break;
                    }

                    // record what data we filled into the buffer and push to pipe
                    buffer.Advance(bytesReceived);
                    var result = await buffer.FlushAsync();

                    if (result.IsCompleted)
                    {
                        // Pipe consumer is shut down
                        _socket.Shutdown(SocketShutdown.Receive);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _input.Complete(ex);
            }
        }

        private async Task DoSend()
        {
            try
            {
                while (true)
                {
                    // Wait for data to write from the pipe producer
                    ReadResult result = await _output.ReadAsync();
                    if (result.IsCompleted)
                    {
                        // Pipe producer is shut down
                        Debug.Assert(result.Buffer.IsEmpty);
                        _socket.Shutdown(SocketShutdown.Send);
                        break;
                    }

                    // Write received data to socket
                    // TODO: Do multi-buffer write here
                    ReadableBuffer buffer = result.Buffer;
                    foreach (Buffer<byte> b in buffer)
                    {
                        await _socket.SendAsync(GetArraySegment(b), SocketFlags.None);
                    }

                    _output.Advance(buffer.End);
                }
            }
            catch (Exception ex)
            {
                _output.Complete(ex);
            }
        }

        private static ArraySegment<byte> GetArraySegment(Buffer<byte> buffer)
        {
            ArraySegment<byte> segment;
            if (!buffer.TryGetArray(out segment))
            {
                throw new InvalidOperationException("Memory is not backed by an array; oops!");
            }

            return segment;
        }

        public void SetTimeout(long milliseconds, TimeoutAction timeoutAction)
        {
            // TODO
        }

        public void ResetTimeout(long milliseconds, TimeoutAction timeoutAction)
        {
            // TODO
        }

        public void CancelTimeout()
        {
            // TODO
        }

        public ListenOptions ListenOptions => _transport.ListenOptions;

        public IPEndPoint RemoteEndPoint => (IPEndPoint)_socket.RemoteEndPoint;

        public IPEndPoint LocalEndPoint => (IPEndPoint)_socket.LocalEndPoint;

        public PipeFactory PipeFactory => _transport.PipeFactory;

        public IScheduler InputWriterScheduler => InlineScheduler.Default;

        public IScheduler OutputWriterScheduler => InlineScheduler.Default;

        public ITimeoutControl TimeoutControl => this;
    }
}
