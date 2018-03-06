// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Protocols;
using System.Threading;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    internal sealed class SocketConnection : TransportConnection
    {
        private const int MinAllocBufferSize = 2048;

        private readonly Socket _socket;
        private readonly ISocketsTrace _trace;
        private readonly SocketReceiver _receiver;
        private readonly SocketSender _sender;

        private volatile bool _aborted;

        internal SocketConnection(Socket socket, MemoryPool<byte> memoryPool, ISocketsTrace trace)
        {
            Debug.Assert(socket != null);
            Debug.Assert(memoryPool != null);
            Debug.Assert(trace != null);

            _socket = socket;
            MemoryPool = memoryPool;
            _trace = trace;

            var localEndPoint = (IPEndPoint)_socket.LocalEndPoint;
            var remoteEndPoint = (IPEndPoint)_socket.RemoteEndPoint;

            LocalAddress = localEndPoint.Address;
            LocalPort = localEndPoint.Port;

            RemoteAddress = remoteEndPoint.Address;
            RemotePort = remoteEndPoint.Port;

            _receiver = new SocketReceiver(_socket);
            _sender = new SocketSender(_socket);
        }

        public override MemoryPool<byte> MemoryPool { get; }
        public override PipeScheduler InputWriterScheduler => PipeScheduler.Inline;
        public override PipeScheduler OutputReaderScheduler => GlobalThreadPoolScheduler.Default;

        public async Task StartAsync(IConnectionHandler connectionHandler)
        {
            Exception sendError = null;
            try
            {
                connectionHandler.OnConnection(this);

                // Spawn send and receive logic
                Task receiveTask = DoReceive();
                Task<Exception> sendTask = DoSend();

                // If the sending task completes then close the receive
                // We don't need to do this in the other direction because the kestrel
                // will trigger the output closing once the input is complete.
                if (await Task.WhenAny(receiveTask, sendTask) == sendTask)
                {
                    // Tell the reader it's being aborted
                    _socket.Dispose();
                }

                // Now wait for both to complete
                await receiveTask;
                sendError = await sendTask;

                // Dispose the socket(should noop if already called)
                _socket.Dispose();
            }
            catch (Exception ex)
            {
                _trace.LogError(0, ex, $"Unexpected exception in {nameof(SocketConnection)}.{nameof(StartAsync)}.");
            }
            finally
            {
                // Complete the output after disposing the socket
                Output.Complete(sendError);
            }
        }

        private async Task DoReceive()
        {
            Exception error = null;

            try
            {
                while (true)
                {
                    // Ensure we have some reasonable amount of buffer space
                    var buffer = Input.GetMemory(MinAllocBufferSize);

                    var bytesReceived = await _receiver.ReceiveAsync(buffer);

                    if (bytesReceived == 0)
                    {
                        // FIN
                        _trace.ConnectionReadFin(ConnectionId);
                        break;
                    }

                    Input.Advance(bytesReceived);

                    var flushTask = Input.FlushAsync();

                    if (!flushTask.IsCompleted)
                    {
                        _trace.ConnectionPause(ConnectionId);

                        await flushTask;

                        _trace.ConnectionResume(ConnectionId);
                    }

                    var result = flushTask.GetAwaiter().GetResult();
                    if (result.IsCompleted)
                    {
                        // Pipe consumer is shut down, do we stop writing
                        break;
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                error = new ConnectionResetException(ex.Message, ex);
                _trace.ConnectionReset(ConnectionId);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted ||
                                             ex.SocketErrorCode == SocketError.ConnectionAborted ||
                                             ex.SocketErrorCode == SocketError.Interrupted ||
                                             ex.SocketErrorCode == SocketError.InvalidArgument)
            {
                if (!_aborted)
                {
                    // Calling Dispose after ReceiveAsync can cause an "InvalidArgument" error on *nix.
                    error = new ConnectionAbortedException();
                    _trace.ConnectionError(ConnectionId, error);
                }
            }
            catch (ObjectDisposedException)
            {
                if (!_aborted)
                {
                    error = new ConnectionAbortedException();
                    _trace.ConnectionError(ConnectionId, error);
                }
            }
            catch (IOException ex)
            {
                error = ex;
                _trace.ConnectionError(ConnectionId, error);
            }
            catch (Exception ex)
            {
                error = new IOException(ex.Message, ex);
                _trace.ConnectionError(ConnectionId, error);
            }
            finally
            {
                if (_aborted)
                {
                    error = error ?? new ConnectionAbortedException();
                }

                Input.Complete(error);
            }
        }

        private async Task<Exception> DoSend()
        {
            Exception error = null;

            try
            {
                while (true)
                {
                    // Wait for data to write from the pipe producer
                    var result = await Output.ReadAsync();
                    var buffer = result.Buffer;

                    if (result.IsCanceled)
                    {
                        break;
                    }

                    try
                    {
                        if (!buffer.IsEmpty)
                        {
                            await _sender.SendAsync(buffer);
                        }
                        else if (result.IsCompleted)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        Output.AdvanceTo(buffer.End);
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                error = null;
            }
            catch (ObjectDisposedException)
            {
                error = null;
            }
            catch (IOException ex)
            {
                error = ex;
            }
            catch (Exception ex)
            {
                error = new IOException(ex.Message, ex);
            }
            finally
            {
                // Make sure to close the connection only after the _aborted flag is set.
                // Without this, the RequestsCanBeAbortedMidRead test will sometimes fail when
                // a BadHttpRequestException is thrown instead of a TaskCanceledException.
                _aborted = true;
                _trace.ConnectionWriteFin(ConnectionId);
                _socket.Shutdown(SocketShutdown.Both);
            }

            return error;
        }

        // This scheduler runs on the global thread pool queue instead of the local queue
        // We can delete this when we get a new pipelines build since that's the new default.
        // In the current version that Kestrel uses, we're using local queues instead of the global one.
        private class GlobalThreadPoolScheduler : PipeScheduler
        {
            public static GlobalThreadPoolScheduler Default = new GlobalThreadPoolScheduler();

            public override void Schedule(Action action)
            {
                System.Threading.ThreadPool.QueueUserWorkItem(s =>
                {
                    ((Action)s)();
                },
                action);
            }

            public override void Schedule(Action<object> action, object state)
            {
                System.Threading.ThreadPool.QueueUserWorkItem(s =>
                {
                    var t = (Tuple<Action<object>, object>)s;
                    t.Item1(t.Item2);
                },
                Tuple.Create(action, state));
            }
        }
    }
}
