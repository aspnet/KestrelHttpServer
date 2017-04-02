﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal
{
    public class KestrelEngine : ITransport
    {
        private readonly IListenOptions _listenOptions;

        private readonly List<IAsyncDisposable> _listeners = new List<IAsyncDisposable>();

        public KestrelEngine(LibuvTransportContext context, IListenOptions listenOptions)
            : this(new LibuvFunctions(), context, listenOptions)
        { }

        // For testing
        public KestrelEngine(LibuvFunctions uv, LibuvTransportContext context, IListenOptions listenOptions)
        {
            Libuv = uv;
            TransportContext = context;

            _listenOptions = listenOptions;
        }

        public LibuvFunctions Libuv { get; }
        public LibuvTransportContext TransportContext { get; }
        public List<KestrelThread> Threads { get; } = new List<KestrelThread>();

        public IApplicationLifetime AppLifetime => TransportContext.AppLifetime;
        public IKestrelTrace Log => TransportContext.Log;
        public LibuvTransportOptions TransportOptions => TransportContext.Options;

        public async Task StopAsync()
        {
            try
            {
                await Task.WhenAll(Threads.Select(thread => thread.StopAsync(TimeSpan.FromSeconds(2.5))).ToArray())
                    .ConfigureAwait(false);
            }
            catch (AggregateException aggEx)
            {
                // An uncaught exception was likely thrown from the libuv event loop.
                // The original error that crashed one loop may have caused secondary errors in others.
                // Make sure that the stack trace of the original error is logged.
                foreach (var ex in aggEx.InnerExceptions)
                {
                    Log.LogCritical("Failed to gracefully close Kestrel.", ex);
                }

                throw;
            }

            Threads.Clear();
#if DEBUG
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
#endif
        }

        public async Task BindAsync()
        {
            // TODO: Move thread management to LibuvTransportFactory
            // TODO: Split endpoint management from thread management
            for (var index = 0; index < TransportOptions.ThreadCount; index++)
            {
                Threads.Add(new KestrelThread(this));
            }

            foreach (var thread in Threads)
            {
                await thread.StartAsync().ConfigureAwait(false);
            }

            try
            {
                if (TransportOptions.ThreadCount == 1)
                {
                    var listener = new Listener(TransportContext);
                    _listeners.Add(listener);
                    await listener.StartAsync(_listenOptions, Threads[0]).ConfigureAwait(false);
                }
                else
                {
                    var pipeName = (Libuv.IsWindows ? @"\\.\pipe\kestrel_" : "/tmp/kestrel_") + Guid.NewGuid().ToString("n");
                    var pipeMessage = Guid.NewGuid().ToByteArray();

                    var listenerPrimary = new ListenerPrimary(TransportContext);
                    _listeners.Add(listenerPrimary);
                    await listenerPrimary.StartAsync(pipeName, pipeMessage, _listenOptions, Threads[0]).ConfigureAwait(false);

                    foreach (var thread in Threads.Skip(1))
                    {
                        var listenerSecondary = new ListenerSecondary(TransportContext);
                        _listeners.Add(listenerSecondary);
                        await listenerSecondary.StartAsync(pipeName, pipeMessage, _listenOptions, thread).ConfigureAwait(false);
                    }
                }
            }
            catch (UvException ex) when (ex.StatusCode == Constants.EADDRINUSE)
            {
                await UnbindAsync().ConfigureAwait(false);
                throw new AddressInUseException(ex.Message, ex);
            }
            catch
            {
                await UnbindAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task UnbindAsync()
        {
            var disposeTasks = _listeners.Select(listener => listener.DisposeAsync()).ToArray();

            if (!await WaitAsync(Task.WhenAll(disposeTasks), TimeSpan.FromSeconds(2.5)).ConfigureAwait(false))
            {
                Log.LogError(0, null, "Disposing listeners failed");
            }

            _listeners.Clear();
        }

        private static async Task<bool> WaitAsync(Task task, TimeSpan timeout)
        {
            return await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) == task;
        }
    }
}
