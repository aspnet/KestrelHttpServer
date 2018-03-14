// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class SocketReceiver
    {
        private readonly Socket _socket;
        private readonly SocketAsyncEventArgs _eventArgs = new SocketAsyncEventArgs();
        private readonly SocketAwaitable _awaitable;
        private Memory<byte> _buffer;

        public SocketReceiver(Socket socket, PipeScheduler scheduler)
        {
            _socket = socket;
            _awaitable = new SocketAwaitable(scheduler);
            _eventArgs.SetBuffer(Array.Empty<byte>(), 0, 0);
            _eventArgs.UserToken = this;
            _eventArgs.Completed += (_, e) => ((SocketReceiver)e.UserToken).OnReadable();
        }

        public SocketAwaitable ReceiveAsync(Memory<byte> buffer)
        {
            _buffer = buffer;
            if (!_socket.ReceiveAsync(_eventArgs))
            {
                OnReadable();
            }
            return _awaitable;
        }

        private void OnReadable()
        {
            SocketError errorCode = _eventArgs.SocketError;
            int bytesTransferred = 0;
            if (errorCode == SocketError.Success)
            {
                try
                {
#if NETCOREAPP2_1
                    bytesTransferred = _socket.Receive(_buffer.Span, SocketFlags.None, out errorCode);
#else
                    var segment = _buffer.GetArray();
                    bytesTransferred = _socket.Receive(segment.Array, segment.Offset, segment.Count, SocketFlags.None, out errorCode);
#endif
                }
                catch (ObjectDisposedException)
                {
                    errorCode = SocketError.ConnectionAborted;
                }
            }
            _buffer = null;
            _awaitable.Complete(bytesTransferred, errorCode);
        }
    }
}
