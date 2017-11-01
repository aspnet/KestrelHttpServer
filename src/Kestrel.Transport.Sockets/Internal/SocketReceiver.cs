// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Sockets;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class SocketReceiver
    {
        private readonly Socket _socket;
        private readonly SocketAsyncEventArgs _eventArgs = new SocketAsyncEventArgs();
        private readonly SocketAwaitable _awaitable = new SocketAwaitable();

        public SocketReceiver(Socket socket)
        {
            _socket = socket;
            _eventArgs.UserToken = this;
            _eventArgs.Completed += (_, e) => ((SocketReceiver)e.UserToken).ReceiveCompleted(e);
        }

        public SocketAwaitable ReceiveAsync(Buffer<byte> buffer)
        {
            if (!buffer.TryGetArray(out var segment))
            {
                throw new InvalidOperationException();
            }

            _eventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

            if (!_socket.ReceiveAsync(_eventArgs))
            {
                ReceiveCompleted(_eventArgs);
            }

            return _awaitable;
        }

        private void ReceiveCompleted(SocketAsyncEventArgs e)
        {
            _awaitable.Complete(e.BytesTransferred, e.SocketError);
        }
    }
}