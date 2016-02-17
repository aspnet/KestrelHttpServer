// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Networking;

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    public class ListenerContext : ServiceContext
    {
        public ListenerContext()
        {
            Memory2 = new MemoryPool2();
        }

        public ListenerContext(ServiceContext serviceContext) 
            : base(serviceContext)
        {
            Memory2 = new MemoryPool2();
            WriteReqPool = new Queue<NonCovariant<UvWriteReq>>(SocketOutput.MaxPooledWriteReqs);
        }

        public ListenerContext(ListenerContext listenerContext)
            : base(listenerContext)
        {
            ServerAddress = listenerContext.ServerAddress;
            Thread = listenerContext.Thread;
            Memory2 = listenerContext.Memory2;
            WriteReqPool = listenerContext.WriteReqPool;
            Log = listenerContext.Log;
        }

        public ServerAddress ServerAddress { get; set; }

        public KestrelThread Thread { get; set; }

        public MemoryPool2 Memory2 { get; set; }

        public Queue<NonCovariant<UvWriteReq>> WriteReqPool { get; set; }
    }
}
