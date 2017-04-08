﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets
{
    internal sealed class SocketConnection : IConnectionInformation, ITimeoutControl
    {
        private readonly Socket _socket;
        private readonly SocketTransport _transport;
        private readonly IPEndPoint _localEndPoint;
        private readonly IPEndPoint _remoteEndPoint;
        private IPipeWriter _input;
        private IPipeReader _output;

        private const int MinAllocBufferSize = 2048;        // from libuv transport

        internal SocketConnection(Socket socket, SocketTransport transport)
        {
            Debug.Assert(socket != null);
            Debug.Assert(transport != null);

            _socket = socket;
            _transport = transport;

            _localEndPoint = (IPEndPoint)_socket.LocalEndPoint;
            _remoteEndPoint = (IPEndPoint)_socket.RemoteEndPoint;
        }

        public async void Start(IConnectionHandler connectionHandler)
        {
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
                bool done = false;
                while (!done)
                {
                    // Ensure we have some reasonable amount of buffer space
                    WritableBuffer buffer = _input.Alloc(MinAllocBufferSize);

                    try
                    {
                        int bytesReceived = await _socket.ReceiveAsync(GetArraySegment(buffer.Buffer), SocketFlags.None);

                        if (bytesReceived == 0)
                        {
                            // EOF
                            _input.Complete();
                            done = true;
                            break;
                        }

                        // record what data we filled into the buffer and push to pipe
                        buffer.Advance(bytesReceived);
                    }
                    finally
                    {
                        var result = await buffer.FlushAsync();
                        if (result.IsCompleted)
                        {
                            // Pipe consumer is shut down
                            _socket.Shutdown(SocketShutdown.Receive);
                            done = true;
                        }
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
                bool done = false;
                while (!done)
                {
                    // Wait for data to write from the pipe producer
                    ReadResult result = await _output.ReadAsync();
                    ReadableBuffer buffer = result.Buffer;

                    try
                    {
                        // Write received data to socket
                        // TODO: Do multi-buffer write here
                        foreach (Buffer<byte> b in buffer)
                        {
                            await _socket.SendAsync(GetArraySegment(b), SocketFlags.None);
                        }
                    }
                    finally
                    {
                        _output.Advance(buffer.End);
                        if (result.IsCompleted)
                        {
                            // Pipe producer is shut down
                            _socket.Shutdown(SocketShutdown.Send);
                            done = true;
                        }
                    }
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
                throw new InvalidOperationException();
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

        public IPEndPoint RemoteEndPoint => _remoteEndPoint;

        public IPEndPoint LocalEndPoint => _localEndPoint;

        public PipeFactory PipeFactory => _transport.PipeFactory;

        public IScheduler InputReaderScheduler => InlineScheduler.Default
            ;
        public IScheduler InputWriterScheduler => InlineScheduler.Default;

        public IScheduler OutputReaderScheduler => InlineScheduler.Default;

        public IScheduler OutputWriterScheduler => InlineScheduler.Default;

        public ITimeoutControl TimeoutControl => this;
    }
}
