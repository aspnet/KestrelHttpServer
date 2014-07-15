// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public static class PlatformApis
    {
        public enum Platform
        {
            Unknown,
            Linux,
            Mac,
            Windows,
        }

        public static Platform CurrentPlatform { get; private set; }

        public static string Architecture { get; private set; }

        static PlatformApis()
        {
            CurrentPlatform = Platform.Unknown;
            Architecture = IntPtr.Size == 4
                ? "x86"
                : "amd64";
#if K10
            CurrentPlatform = Platform.Windows;
#else
            var p = (int)Environment.OSVersion.Platform;
            if ((p != 4) && (p != 6) && (p != 128))
            {
                CurrentPlatform = Platform.Windows;
            }
            else
            {
                string uname = Uname("");
                if ("Darwin".Equals(uname))
                {
                    CurrentPlatform = Platform.Mac;
                }
                else if ("Linux".Equals(uname))
                {
                    CurrentPlatform = Platform.Linux;
                }
                Architecture = Uname("-m");
            }
#endif
        }

        private static string Uname(string args)
        {
            Process p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = "uname";
            p.StartInfo.Arguments = args;
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (output ?? "").Trim();
        }

        public static void Apply(Libuv libuv)
        {
            if (libuv.IsWindows)
            {
                WindowsApis.Apply(libuv);
            }
            else
            {
                LinuxApis.Apply(libuv);
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

            public static void Apply(Libuv libuv)
            {
                libuv.LoadLibrary = LoadLibrary;
                libuv.FreeLibrary = FreeLibrary;
                libuv.GetProcAddress = GetProcAddress;
            }
        }
    }
}
