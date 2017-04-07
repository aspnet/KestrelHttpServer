// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    public struct RioListenSocket : IDisposable
    {
        private IntPtr _handle;

        public static RioListenSocket Create()
        {
            return RioFunctions.CreateListenSocket();
        }

        public bool IsNull => _handle == IntPtr.Zero;

        public void Bind(IPEndPoint endPoint)
        {
            RioFunctions.BindSocket(this, endPoint);
        }

        public void Listen(int listenBacklog)
        {
            RioFunctions.Listen(this, listenBacklog);
        }

        public RioConnectedSocket AcceptSocket()
        {
            return RioFunctions.AcceptSocket(this);
        }

        public IPEndPoint LocalEndPoint
        {
            get => RioFunctions.GetSockIPEndPoint(this);
        }

        public void Dispose()
        {
            RioFunctions.CloseSocket(this);
            _handle = IntPtr.Zero;
        }
    }
}
