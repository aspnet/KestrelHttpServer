// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class Libuv
    {
        public bool IsWindows
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

        public static void Check(int statusCode)
        {
            Exception error;
            Check(statusCode, out error);
            if (error != null)
            {
                throw error;
            }
        }

        public static void Check(int statusCode, out Exception error)
        {
            error = null;

            if (statusCode < 0)
            {
                var errorName = Marshal.PtrToStringAnsi(UnsafeNativeMethods.uv_err_name(statusCode));
                var errorDescription = Marshal.PtrToStringAnsi(UnsafeNativeMethods.uv_strerror(statusCode));
                error = new Exception("Error " + statusCode + " " + errorName + " " + errorDescription);
            }
        }

        public uv_buf_t buf_init(IntPtr memory, int len)
        {
            return new uv_buf_t(memory, len, IsWindows);
        }

        public struct sockaddr
        {
            long x0;
            long x1;
            long x2;
            long x3;
        }

        public struct uv_buf_t
        {
            public uv_buf_t(IntPtr memory, int len, bool IsWindows)
            {
                if (IsWindows)
                {
                    x0 = (IntPtr)len;
                    x1 = memory;
                }
                else
                {
                    x0 = memory;
                    x1 = (IntPtr)len;
                }
            }

            public IntPtr x0;
            public IntPtr x1;
        }

        public enum HandleType
        {
            Unknown = 0,
            ASYNC,
            CHECK,
            FS_EVENT,
            FS_POLL,
            HANDLE,
            IDLE,
            NAMED_PIPE,
            POLL,
            PREPARE,
            PROCESS,
            STREAM,
            TCP,
            TIMER,
            TTY,
            UDP,
            SIGNAL,
        }

        public enum RequestType
        {
            Unknown = 0,
            REQ,
            CONNECT,
            WRITE,
            SHUTDOWN,
            UDP_SEND,
            FS,
            WORK,
            GETADDRINFO,
            GETNAMEINFO,
        }
        //int handle_size_async;
        //int handle_size_tcp;
        //int req_size_write;
        //int req_size_shutdown;
    }
}
