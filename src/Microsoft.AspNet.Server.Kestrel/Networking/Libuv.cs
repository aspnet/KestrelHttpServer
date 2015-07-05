// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public static class Libuv
    {
        public static bool IsWindows
        {
            get
            {
#if DNXCORE50
                // Until Environment.OSVersion.Platform is exposed on .NET Core, we
                // try to call uname and if that fails we assume we are on Windows.
                return GetUname() == string.Empty;
#else
                var p = (int)Environment.OSVersion.Platform;
                return (p != 4) && (p != 6) && (p != 128);
#endif
            }
        }

        [DllImport("libc", CharSet = CharSet.Ansi)]
        static extern int uname([Out] StringBuilder buf);

        public static bool IsDarwin
        {
            get
            {
                var uname = GetUname();
                return string.Equals(
                    uname,
                    "Darwin",
                    StringComparison.Ordinal);
            }
        }

        public static string GetUname()
        {
            // According to the documentation,
            //  there might be 9, 33, 65, or 257 bytes
            var buffer = new StringBuilder(8192);
            try
            {
                if (uname(buffer) == 0)
                {
                    return buffer.ToString();
                }
            }
            catch (Exception)
            {
            }

            return string.Empty;
        }

        public static void ThrowOnError(int statusCode)
        {
            var error = ExceptionForError(statusCode);
            if (error != null)
            {
                throw error;
            }
        }

        public static Exception ExceptionForError(int statusCode)
        {
            if (statusCode < 0)
            {
                var errorName = Marshal.PtrToStringAnsi(UnsafeNativeMethods.uv_err_name(statusCode));
                var errorDescription = Marshal.PtrToStringAnsi(UnsafeNativeMethods.uv_strerror(statusCode));
                return new Exception("Error " + statusCode + " " + errorName + " " + errorDescription);
            }
            else
                return null;
        }
    }
}
