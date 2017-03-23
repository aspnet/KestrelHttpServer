// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;
using Microsoft.AspNetCore.Server.Kestrel.Libuv.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class ListenerContext
    {
        public ListenerContext(LibuvTransportContext transportContext)
        {
            TransportContext = transportContext;
        }

        public LibuvTransportContext TransportContext { get; set; }

        public ListenOptions ListenOptions { get; set; }

        public KestrelThread Thread { get; set; }

        /// <summary>
        /// Creates a socket which can be used to accept an incoming connection.
        /// </summary>
        protected UvStreamHandle CreateAcceptSocket()
        {
            switch (ListenOptions.Type)
            {
                case ListenType.IPEndPoint:
                case ListenType.FileHandle:
                    var tcpHandle = new UvTcpHandle(TransportContext.Log);
                    tcpHandle.Init(Thread.Loop, Thread.QueueCloseHandle);
                    tcpHandle.NoDelay(ListenOptions.NoDelay);
                    return tcpHandle;
                case ListenType.SocketPath:
                    var pipeHandle = new UvPipeHandle(TransportContext.Log);
                    pipeHandle.Init(Thread.Loop, Thread.QueueCloseHandle);
                    return pipeHandle;
                default:
                    throw new InvalidOperationException();
            }
        }

        public PipeOptions LibuvInputPipeOptions => new PipeOptions
        {
            ReaderScheduler = TransportContext.ThreadPool,
            WriterScheduler = Thread,
            MaximumSizeHigh = TransportContext.Options.Limits.MaxRequestBufferSize ?? 0,
            MaximumSizeLow = TransportContext.Options.Limits.MaxRequestBufferSize ?? 0
        };

        public PipeOptions LibuvOutputPipeOptions => new PipeOptions
        {
            ReaderScheduler = Thread,
            WriterScheduler = TransportContext.ThreadPool,
            MaximumSizeHigh = GetOutputResponseBufferSize(),
            MaximumSizeLow = GetOutputResponseBufferSize()
        };

        public PipeOptions AdaptedPipeOptions => new PipeOptions
        {
            ReaderScheduler = InlineScheduler.Default,
            WriterScheduler = InlineScheduler.Default,
            MaximumSizeHigh = TransportContext.Options.Limits.MaxRequestBufferSize ?? 0,
            MaximumSizeLow = TransportContext.Options.Limits.MaxRequestBufferSize ?? 0
        };

        private long GetOutputResponseBufferSize()
        {
            var bufferSize = TransportContext.Options.Limits.MaxResponseBufferSize;
            if (bufferSize == 0)
            {
                // 0 = no buffering so we need to configure the pipe so the the writer waits on the reader directly
                return 1;
            }

            // null means that we have no back pressure
            return bufferSize ?? 0;
        }
    }
}
