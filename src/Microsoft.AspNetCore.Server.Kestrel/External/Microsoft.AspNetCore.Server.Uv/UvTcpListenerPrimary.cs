// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Networking.Uv.Interop;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Networking.Uv
{
    /// <summary>
    /// An implementation of <see cref="UvListenerPrimary"/> using TCP sockets.
    /// </summary>
    public class UvTcpListenerPrimary : UvListenerPrimary
    {
        public UvTcpListenerPrimary(ServiceContext serviceContext) : base(serviceContext)
        {
        }

        /// <summary>
        /// Creates the socket used to listen for incoming connections
        /// </summary>
        protected override UvStreamHandle CreateListenSocket()
        {
            var socket = new UvTcpHandle(Log);
            socket.Init(Thread.Loop, Thread.QueueCloseHandle);
            socket.NoDelay(ServerOptions.NoDelay);
            socket.Bind(ServerAddress);
            socket.Listen(Constants.ListenBacklog, (stream, status, error, state) => ConnectionCallback(stream, status, error, state), this);
            return socket;
        }

        /// <summary>
        /// Handles an incoming connection
        /// </summary>
        /// <param name="listenSocket">Socket being used to listen on</param>
        /// <param name="status">Connection status</param>
        protected override void OnConnection(UvStreamHandle listenSocket, int status)
        {
            var acceptSocket = new UvTcpHandle(Log);

            try
            {
                acceptSocket.Init(Thread.Loop, Thread.QueueCloseHandle);
                acceptSocket.NoDelay(ServerOptions.NoDelay);
                listenSocket.Accept(acceptSocket);
                DispatchConnection(acceptSocket);

            }
            catch (UvException ex)
            {
                Log.LogError(0, ex, "TcpListenerPrimary.OnConnection");
                acceptSocket.Dispose();
                return;
            }
        }
    }
}
