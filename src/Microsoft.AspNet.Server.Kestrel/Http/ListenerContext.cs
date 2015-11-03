// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNet.Http;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class ListenerContext : ServiceContext
    {
        public ListenerContext()
        {
            Memory = new MemoryPool();
        }

        public ListenerContext(ServiceContext serviceContext) 
            : base(serviceContext)
        {
            Memory = new MemoryPool();
        }

        public ListenerContext(ListenerContext listenerContext)
            : base(listenerContext)
        {
            ServerAddress = listenerContext.ServerAddress;
            Thread = listenerContext.Thread;
            Application = listenerContext.Application;
            Memory = listenerContext.Memory;
            Log = listenerContext.Log;
        }

        public ServerAddress ServerAddress { get; set; }

        public KestrelThread Thread { get; set; }

        public RequestDelegate Application { get; set; }

        public MemoryPool Memory { get; set; }
    }
}
