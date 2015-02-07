// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNet.Server.Kestrel.Networking;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Http;
using Microsoft.Framework.Runtime;
using System.IO;

namespace Microsoft.AspNet.Server.Kestrel
{
    public class KestrelEngine : IDisposable
    {

        public KestrelEngine(ILibraryManager libraryManager)
        {
            Threads = new List<KestrelThread>();
            Listeners = new List<Listener>();
            Memory = new MemoryPool();
            Libuv = new Libuv();

            if (Libuv.IsWindows)
            {
                var library = libraryManager.GetLibraryInformation("Microsoft.AspNet.Server.Kestrel");
                var libraryPath = library.Path;
                if (library.Type == "Project")
                    libraryPath = Path.GetDirectoryName(libraryPath);

                var architectureLibraryPath = Path.Combine(
                    libraryPath,
                    "native",
                    "windows",
                    IntPtr.Size == 4 ? "x86" : "amd64");
                UnsafeNativeMethods.SetDllDirectory(architectureLibraryPath);
            }
        }

        public Libuv Libuv { get; private set; }
        public IMemoryPool Memory { get; set; }
        public List<KestrelThread> Threads { get; private set; }
        public List<Listener> Listeners { get; private set; }

        public void Start(int count)
        {
            for (var index = 0; index != count; ++index)
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

        public IDisposable CreateServer(string scheme, string host, int port, Func<Frame, Task> application)
        {
            var listeners = new List<Listener>();
            foreach (var thread in Threads)
            {
                var listener = new Listener(Memory);
                listener.StartAsync(scheme, host, port, thread, application).Wait();
                listeners.Add(listener);
            }
            return new Disposable(() =>
            {
                foreach (var listener in listeners)
                {
                    listener.Dispose();
                }
            });
        }
    }
}
