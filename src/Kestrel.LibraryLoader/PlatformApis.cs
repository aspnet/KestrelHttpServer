// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Kestrel.LibraryLoader
{
    public static class PlatformApis
    {
        public static bool IsWindows()
        {
#if DNXCORE50
            return true;
#else
            var p = (int)Environment.OSVersion.Platform;
            return (p != 4) && (p != 6) && (p != 128);
#endif
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

        public static bool IsDarwin()
        {
            return string.Equals(GetUname(), "Darwin", StringComparison.Ordinal);
        }

        public static void Apply(Library library)
        {
            if (library.IsWindows)
            {
                WindowsApis.Apply(library);
            }
            else
            {
                LinuxApis.Apply(library);
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

            public static void Apply(Library library)
            {
                library.LoadLibrary = LoadLibrary;
                library.FreeLibrary = FreeLibrary;
                library.GetProcAddress = GetProcAddress;
            }
        }

        public static class LinuxApis
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

            public static void Apply(Library library)
            {
                library.LoadLibrary = LoadLibrary;
                library.FreeLibrary = FreeLibrary;
                library.GetProcAddress = GetProcAddress;
            }
        }
    }
}
