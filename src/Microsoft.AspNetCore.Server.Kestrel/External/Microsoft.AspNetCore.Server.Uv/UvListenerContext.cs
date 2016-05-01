// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Abstractions;
using Microsoft.AspNetCore.Server.Infrastructure;
using Microsoft.AspNetCore.Server.Networking.Uv.Interop;

namespace Microsoft.AspNetCore.Server.Networking.Uv
{
    public class UvListenerContext : ServiceContext
    {
        public UvListenerContext()
        {
        }

        public UvListenerContext(ServiceContext serviceContext)
            : base(serviceContext)
        {
            Memory = new MemoryPool();
            WriteReqPool = new Queue<UvWriteReq>(UvSocketOutput.MaxPooledWriteReqs);
        }

        public UvListenerContext(UvListenerContext listenerContext)
            : base(listenerContext)
        {
            ServerAddress = listenerContext.ServerAddress;
            Thread = listenerContext.Thread;
            Memory = listenerContext.Memory;
            ConnectionManager = listenerContext.ConnectionManager;
            WriteReqPool = listenerContext.WriteReqPool;
            Log = listenerContext.Log;
        }

        public ServerAddress ServerAddress { get; set; }

        public UvThread Thread { get; set; }

        public MemoryPool Memory { get; set; }

        public UvConnectionManager ConnectionManager { get; set; }

        public Queue<UvWriteReq> WriteReqPool { get; set; }
    }
}
