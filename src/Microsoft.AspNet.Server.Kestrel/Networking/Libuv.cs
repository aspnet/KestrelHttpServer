// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
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
                return true;
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
                // According to the documentation,
                //  there might be 9, 33, 65, or 257 bytes
                var buffer = new StringBuilder(8192);
                try
                {
                    if (uname(buffer) == 0)
                    {
                        return string.Equals(
                            buffer.ToString(),
                            "Darwin",
                            StringComparison.Ordinal);
                    }
                }
                catch (Exception)
                {
                }

                return false;
            }
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
