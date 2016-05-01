// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Networking.Uv.Interop;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Server.Networking.Uv
{
    public class UvEngine : ServiceContext, IServerEngine
    {
        private readonly UvOptions _options;
        private ServiceContext _context;
        
        public UvEngine(IOptions<UvOptions> options)
            : this(new Libuv(), options)
        { }

        // For testing
        internal UvEngine(Libuv uv, IOptions<UvOptions> options)
        {
            Libuv = uv;
            _options = options.Value;
            Threads = new List<UvThread>();
        }

        public Libuv Libuv { get; private set; }
        public List<UvThread> Threads { get; private set; }

        public void Start(ServiceContext context)
        {
            HttpComponentFactory = context.HttpComponentFactory;
            AppLifetime = context.AppLifetime;
            DateHeaderValueManager = context.DateHeaderValueManager;
            FrameFactory = context.FrameFactory;
            Log = context.Log;
            ThreadPool = context.ThreadPool;
            ServerOptions = context.ServerOptions;

            for (var index = 0; index < _options.ThreadCount; index++)
            {
                Threads.Add(new UvThread(this));
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
            var listeners = new List<IAsyncDisposable>();

            var usingPipes = address.IsUnixPipe;

            try
            {
                var pipeName = (Libuv.IsWindows ? @"\\.\pipe\kestrel_" : "/tmp/kestrel_") + Guid.NewGuid().ToString("n");

                var single = Threads.Count == 1;
                var first = true;

                foreach (var thread in Threads)
                {
                    if (single)
                    {
                        var listener = usingPipes ?
                            (UvListener) new UvPipeListener(this) :
                            new UvTcpListener(this);
                        listeners.Add(listener);
                        listener.StartAsync(address, thread).Wait();
                    }
                    else if (first)
                    {
                        var listener = usingPipes
                            ? (UvListenerPrimary) new UvPipeListenerPrimary(this)
                            : new UvTcpListenerPrimary(this);

                        listeners.Add(listener);
                        listener.StartAsync(pipeName, address, thread).Wait();
                    }
                    else
                    {
                        var listener = usingPipes
                            ? (UvListenerSecondary) new UvPipeListenerSecondary(this)
                            : new UvTcpListenerSecondary(this);
                        listeners.Add(listener);
                        listener.StartAsync(pipeName, address, thread).Wait();
                    }

                    first = false;
                }

                return new Disposable(() =>
                {
                    DisposeListeners(listeners);
                });
            }
            catch
            {
                DisposeListeners(listeners);

                throw;
            }
        }

        private void DisposeListeners(List<IAsyncDisposable> listeners)
        {
            var disposeTasks = new List<Task>();

            foreach (var listener in listeners)
            {
                 disposeTasks.Add(listener.DisposeAsync());
            }

            if (!Task.WhenAll(disposeTasks).Wait(ServerOptions.ShutdownTimeout))
            {
                Log.NotAllConnectionsClosedGracefully();
            }
        }
    }
}
