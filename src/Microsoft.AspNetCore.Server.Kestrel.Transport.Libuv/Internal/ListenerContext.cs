// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal
{
    public class ListenerContext
    {
        public ListenerContext(LibuvTransportContext transportContext)
        {
            TransportContext = transportContext;
        }

        public LibuvTransportContext TransportContext { get; set; }

        public IEndPointInformation EndPointInformation { get; set; }

        public LibuvThread Thread { get; set; }

        /// <summary>
        /// Creates a socket which can be used to accept an incoming connection.
        /// </summary>
        protected UvStreamHandle CreateAcceptSocket()
        {
            switch (EndPointInformation.Type)
            {
                case ListenType.IPEndPoint:
                    return AcceptTcp();
                case ListenType.SocketPath:
                    return AcceptPipe();
                case ListenType.FileHandle:
                    return AcceptHandle();
                default:
                    throw new InvalidOperationException();
            }
        }

        private UvTcpHandle AcceptTcp()
        {
            var socket = new UvTcpHandle(TransportContext.Log);

            try
            {
                socket.Init(Thread.Loop, Thread.QueueCloseHandle);
                socket.NoDelay(EndPointInformation.NoDelay);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            return socket;
        }

        private UvPipeHandle AcceptPipe()
        {
            var pipe = new UvPipeHandle(TransportContext.Log);

            try
            {
                pipe.Init(Thread.Loop, Thread.QueueCloseHandle);
            }
            catch
            {
                pipe.Dispose();
                throw;
            }

            return pipe;
        }

        private UvStreamHandle AcceptHandle()
        {
            switch (EndPointInformation.HandleType)
            {
                case FileHandleType.Auto:
                    //  Handle type detection must be done by listener, refreshing endpoint information
                    throw new InvalidOperationException();
                case FileHandleType.Tcp:
                    return AcceptTcp();
                case FileHandleType.Pipe:
                    return AcceptPipe();
                default:
                    throw new NotSupportedException();
            }
        }
    }
}
