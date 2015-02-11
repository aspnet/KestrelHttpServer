// Copyright (c) .NET Foundation. All rights reserved.
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
        private readonly IDisposable nativeBinder;

        public KestrelEngine(ILibraryManager libraryManager)
        {
            Threads = new List<KestrelThread>();
            Listeners = new List<Listener>();
            Memory = new MemoryPool();

            var library = libraryManager.GetLibraryInformation("Microsoft.AspNet.Server.Kestrel");
            var libraryPath = library.Path;
            if (library.Type == "Project")
            {
                libraryPath = Path.GetDirectoryName(libraryPath);
            }

            if (Libuv.IsWindows)
            {
                var architectureLibraryPath = Path.Combine(
                    libraryPath,
                    "native",
                    "windows",
#if DNXCORE50
                    // TODO: This is only temporary. Remove when CoreCLR has a release with the Is64BitProcess member
                    IntPtr.Size == 8 ? "amd64" : "x86",
#else
                    Environment.Is64BitProcess ? "amd64" : "x86",
#endif
                    "libuv.dll");

                nativeBinder = new WindowsNativeBinder(
                    architectureLibraryPath,
                    typeof(UnsafeNativeMethods));
            }
            else if (Libuv.IsDarwin)
            {
                var architectureLibraryPath = Path.Combine(
                    libraryPath,
                    "native",
                    "darwin",
                    "universal",
                    "libuv.dylib"
                );
                nativeBinder = new UnixNativeBinder(
                    architectureLibraryPath,
                    typeof(UnsafeNativeMethods));
            }
            else
            {
                nativeBinder = new UnixNativeBinder(
                    "libuv.so.1",
                    typeof(UnsafeNativeMethods));
            }
        }

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

            nativeBinder.Dispose();
        }

        public IDisposable CreateServer(string scheme, string host, int port, Func<Frame, Task> application)
        {
            var listeners = new List<Listener>();

            try
            {
                foreach (var thread in Threads)
                {
                    var listener = new Listener(Memory);

                    listeners.Add(listener);
                    listener.StartAsync(scheme, host, port, thread, application).Wait();
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
