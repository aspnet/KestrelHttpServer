// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class ListenerContext : ServiceContext
    {
        public ListenerContext()
        {
            InputMemory = new MemoryPool2();
            OutputMemory = new MemoryPool2();
        }

        public ListenerContext(ServiceContext serviceContext) 
            : base(serviceContext)
        {
            InputMemory = new MemoryPool2();
            OutputMemory = new MemoryPool2();
        }

        public ListenerContext(ListenerContext listenerContext)
            : base(listenerContext)
        {
            ServerAddress = listenerContext.ServerAddress;
            Thread = listenerContext.Thread;
            Application = listenerContext.Application;
            InputMemory = listenerContext.InputMemory;
            OutputMemory = listenerContext.OutputMemory;
            Log = listenerContext.Log;
        }

        public ServerAddress ServerAddress { get; set; }

        public KestrelThread Thread { get; set; }

        public Func<Frame, Task> Application { get; set; }

        public MemoryPool2 InputMemory { get; set; }

        public MemoryPool2 OutputMemory { get; set; }
    }
}
