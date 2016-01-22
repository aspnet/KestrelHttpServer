// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Server.Kestrel.Http;
using Microsoft.AspNetCore.Server.Kestrel.Networking;

namespace Microsoft.AspNetCore.Server.Kestrel
{
    public class KestrelEngine : ServiceContext, IDisposable
    {
        public KestrelEngine(ServiceContext context)
            : this(new Libuv(), context)
        { }

        // For testing
        internal KestrelEngine(Libuv uv, ServiceContext context)
           : base(context)
        {
            Libuv = uv;
            Threads = new List<KestrelThread>();
        }

        public Libuv Libuv { get; private set; }
        public List<KestrelThread> Threads { get; private set; }

        public void Start(int count)
        {
            if (count > 1 && PlatformApis.IsWindows)
            {
                // Increase thread count by one for multithreaded Windows servers
                // since the primary thread does not actually accept connections.
                count++;
            }

            for (var index = 0; index < count; index++)
            {
                Threads.Add(new KestrelThread(this));
            }

            foreach (var thread in Threads)
            {
                thread.StartAsync().Wait();
            }
        }

        public void Dispose()
        {
            foreach (var thread in Threads)
            {
                thread.Stop(TimeSpan.FromSeconds(2.5));
            }
            Threads.Clear();
        }

        public IDisposable CreateServer(ServerAddress address)
        {
            var listeners = new List<IDisposable>();

            var usingPipes = address.IsUnixPipe;

            try
            {
                var pipeName = (Libuv.IsWindows ? @"\\.\pipe\kestrel_" : "/tmp/kestrel_") + Guid.NewGuid().ToString("n");

                var single = Threads.Count == 1;

                if (single)
                {
                    var listener = usingPipes ?
                        (Listener)new PipeListener(this, address, Threads[0]) :
                        new TcpListener(this, address, Threads[0]);
                    listeners.Add(listener);
                    listener.StartAsync().Wait();
                }
                else if (PlatformApis.IsWindows)
                {
                    // libuv on unix does not allow sockets from a listener on one loop to be
                    // accepted on another loop.
                    var listener = new TcpListenerMultithreaded(this, address, Threads);
                    listeners.Add(listener);
                    listener.StartAsync().Wait();
                }
                else
                {
                    var first = true;

                    foreach (var thread in Threads)
                    {
                        if (first)
                        {
                            var listener = usingPipes
                                ? (ListenerPrimary)new PipeListenerPrimary(this, address, thread, pipeName)
                                : new TcpListenerPrimary(this, address, thread, pipeName);
                            listeners.Add(listener);
                            listener.StartPrimaryAsync().Wait();
                        }
                        else
                        {
                            var listener = usingPipes
                                ? (ListenerSecondary)new PipeListenerSecondary(this, address, thread, pipeName)
                                : new TcpListenerSecondary(this, address, thread, pipeName);
                            listeners.Add(listener);
                            listener.StartSecondaryAsync().Wait();
                        }

                        first = false;
                    }
                }

                return new Disposable(() =>
                {
                    foreach (var listener in listeners)
                    {
                        listener.Dispose();
                    }
                });
            }
            catch
            {
                foreach (var listener in listeners)
                {
                    listener.Dispose();
                }

                throw;
            }
        }
    }
}
