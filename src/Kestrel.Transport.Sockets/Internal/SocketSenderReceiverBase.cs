// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public abstract class SocketSenderReceiverBase : IDisposable
    {
        protected readonly Socket _socket;
        protected readonly SocketAsyncEventArgs _eventArgs = new SocketAsyncEventArgs();
        protected readonly SocketAwaitable _awaitable;

        protected SocketSenderReceiverBase(Socket socket, PipeScheduler scheduler)
        {
            _socket = socket;
            _awaitable = new SocketAwaitable(scheduler);
            _eventArgs.UserToken = _awaitable;
            _eventArgs.Completed += (_, e) => ((SocketAwaitable)e.UserToken).Complete(e.BytesTransferred, e.SocketError);
        }

        public void Dispose() => _eventArgs.Dispose();
    }
}
