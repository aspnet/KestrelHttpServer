﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public sealed class SocketReceiver : SocketSenderReceiverBase
    {
        public SocketReceiver(Socket socket, PipeScheduler scheduler) : base(socket, scheduler)
        {
        }

        public SocketAwaitableEventArgs WaitForDataAsync()
        {
            _awaitableEventArgs.SetBuffer(Array.Empty<byte>(), 0, 0);

            using (SuppressExecutionContext())
            {
                if (!_socket.ReceiveAsync(_awaitableEventArgs))
                {
                    _awaitableEventArgs.Complete();
                }
            }

            return _awaitableEventArgs;
        }

        public SocketAwaitableEventArgs ReceiveAsync(Memory<byte> buffer)
        {
#if NETCOREAPP2_1
            _awaitableEventArgs.SetBuffer(buffer);
#elif NETSTANDARD2_0
            var segment = buffer.GetArray();

            _awaitableEventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);
#else
#error TFMs need to be updated
#endif
            using (SuppressExecutionContext())
            {
                if (!_socket.ReceiveAsync(_awaitableEventArgs))
                {
                    _awaitableEventArgs.Complete();
                }
            }

            return _awaitableEventArgs;
        }
    }
}
