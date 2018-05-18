// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public sealed class SocketReceiver : SocketSenderReceiverBase
    {
        public SocketReceiver(Socket socket, PipeScheduler scheduler) : base(socket, scheduler)
        {
        }

        public SocketAwaitable ReceiveAsync(Memory<byte> buffer)
        {
#if NETCOREAPP2_1
            _eventArgs.SetBuffer(buffer);
#elif NETSTANDARD2_0
            var segment = buffer.GetArray();

            _eventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);
#else
#error TFMs need to be updated
#endif
            if (!_socket.ReceiveAsync(_eventArgs))
            {
                _awaitable.Complete(_eventArgs.BytesTransferred, _eventArgs.SocketError);
            }

            return _awaitable;
        }
    }
}
