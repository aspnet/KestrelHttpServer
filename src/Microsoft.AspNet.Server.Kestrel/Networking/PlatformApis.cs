// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public static class PlatformApis
    {
        public enum Platform
        {
            None,
            Windows,
            MacOSX,
            OtherUnixSystems
        };

        static Platform currentPlatform = Platform.None;

        public static Platform GetPlatform()
        {
            if (currentPlatform == Platform.None)
            {
                // check platform
#if ASPNETCORE50
                currentPlatform = Platform.Windows;
#else
                var p = (int)Environment.OSVersion.Platform;
                if ((p != 4) && (p != 6) && (p != 128))
                {
                    currentPlatform = Platform.Windows;
                }
                else if (string.Equals(GetUname(), "Darwin", StringComparison.Ordinal))
                {
                    currentPlatform = Platform.MacOSX;
                }
                else
                {
                    currentPlatform = Platform.OtherUnixSystems;
                }
#endif
            }
            return currentPlatform;
        }

        [DllImport("libc")]
        static extern int uname(IntPtr buf);

        static unsafe string GetUname()
        {
            var buffer = new byte[8192];
            try
            {
                fixed (byte* buf = buffer)
                {
                    if (uname((IntPtr)buf) == 0)
                    {
                        return Marshal.PtrToStringAnsi((IntPtr)buf);
                    }
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void Apply(Libuv libuv)
        {
            if (GetPlatform() == Platform.Windows)
            {
                WindowsApis.Apply(libuv);
            }
            else
            {
                UnixApis.Apply(libuv);
            }
        }

        public static class WindowsApis
        {
            [DllImport("kernel32")]
            public static extern IntPtr LoadLibrary(string dllToLoad);

            [DllImport("kernel32")]
            public static extern bool FreeLibrary(IntPtr hModule);

            [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

            public static void Apply(Libuv libuv)
            {
                libuv.LoadLibrary = LoadLibrary;
                libuv.FreeLibrary = FreeLibrary;
                libuv.GetProcAddress = GetProcAddress;
            }
        }

        public static class UnixApis
        {
            [DllImport("libdl")]
            public static extern IntPtr dlopen(String fileName, int flags);

            [DllImport("libdl")]
            public static extern IntPtr dlsym(IntPtr handle, String symbol);

            [DllImport("libdl")]
            public static extern int dlclose(IntPtr handle);

            [DllImport("libdl")]
            public static extern IntPtr dlerror();

            public static IntPtr LoadLibrary(string dllToLoad)
            {
                return dlopen(dllToLoad, 2);
            }

            public static bool FreeLibrary(IntPtr hModule)
            {
                return dlclose(hModule) == 0;
            }

            public static IntPtr GetProcAddress(IntPtr hModule, string procedureName)
            {
                dlerror();
                var res = dlsym(hModule, procedureName);
                var errPtr = dlerror();
                return errPtr == IntPtr.Zero ? res : IntPtr.Zero;
            }

            public static void Apply(Libuv libuv)
            {
                libuv.LoadLibrary = LoadLibrary;
                libuv.FreeLibrary = FreeLibrary;
                libuv.GetProcAddress = GetProcAddress;
            }
        }
    }
}
