// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Networking;

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    /// <summary>
    /// An implementation of <see cref="UvListenerSecondary"/> using UNIX sockets.
    /// </summary>
    public class UvPipeListenerSecondary : UvListenerSecondary
    {
        public UvPipeListenerSecondary(ServiceContext serviceContext) : base(serviceContext)
        {
        }

        /// <summary>
        /// Creates a socket which can be used to accept an incoming connection
        /// </summary>
        protected override UvStreamHandle CreateAcceptSocket()
        {
            var acceptSocket = new UvPipeHandle(Log);
            acceptSocket.Init(Thread.Loop, Thread.QueueCloseHandle, false);
            return acceptSocket;
        }
    }
}
