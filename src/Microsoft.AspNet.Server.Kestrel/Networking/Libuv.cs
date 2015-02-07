// Copyright (c) .NET Foundation. All rights reserved.
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
                // Until Environment.OSVersion.Platform is exposed on .NET Core, we
                // try to call uname and if that fails we assume we are on Windows.
                return GetUname() == string.Empty;
#else
                var p = (int)Environment.OSVersion.Platform;
                return (p != 4) && (p != 6) && (p != 128);
#endif
            }
        }

        public int Check(int statusCode)
        {
            Exception error;
            var result = Check(statusCode, out error);
            if (error != null)
            {
                throw error;
            }
            return statusCode;
        }

        public int Check(int statusCode, out Exception error)
        {
            if (statusCode < 0)
            {
                var errorName = Marshal.PtrToStringAnsi(UnsafeNativeMethods.uv_err_name(statusCode));
                var errorDescription = Marshal.PtrToStringAnsi(UnsafeNativeMethods.uv_strerror(statusCode));
                error = new Exception("Error " + statusCode + " " + errorName + " " + errorDescription);
            }
            else
            {
                error = null;
            }
            return statusCode;
        }

        public void loop_init(UvLoopHandle handle)
        {
            Check(UnsafeNativeMethods.uv_loop_init(handle));
        }

        public void loop_close(UvLoopHandle handle)
        {
            handle.Validate(closed: true);
            Check(UnsafeNativeMethods.uv_loop_close(handle.InternalGetHandle()));
        }

        public int run(UvLoopHandle handle, int mode)
        {
            handle.Validate();
            return Check(UnsafeNativeMethods.uv_run(handle, mode));
        }

        public void stop(UvLoopHandle handle)
        {
            handle.Validate();
            UnsafeNativeMethods.uv_stop(handle);
        }

        public void @ref(UvHandle handle)
        {
            handle.Validate();
            UnsafeNativeMethods.uv_ref(handle);
        }

        public void unref(UvHandle handle)
        {
            handle.Validate();
            UnsafeNativeMethods.uv_unref(handle);
        }

        public void close(UvHandle handle, uv_close_cb close_cb)
        {
            handle.Validate(closed: true);
            UnsafeNativeMethods.uv_close(handle.InternalGetHandle(), close_cb);
        }
        public void close(IntPtr handle, uv_close_cb close_cb)
        {
            UnsafeNativeMethods.uv_close(handle, close_cb);
        }

        public void async_init(UvLoopHandle loop, UvAsyncHandle handle, uv_async_cb cb)
        {
            loop.Validate();
            handle.Validate();
            Check(UnsafeNativeMethods.uv_async_init(loop, handle, cb));
        }

        public void async_send(UvAsyncHandle handle)
        {
            Check(UnsafeNativeMethods.uv_async_send(handle));
        }

        public void tcp_init(UvLoopHandle loop, UvTcpHandle handle)
        {
            loop.Validate();
            handle.Validate();
            Check(UnsafeNativeMethods.uv_tcp_init(loop, handle));
        }

        public void tcp_bind(UvTcpHandle handle, ref sockaddr addr, int flags)
        {
            handle.Validate();
            Check(UnsafeNativeMethods.uv_tcp_bind(handle, ref addr, flags));
        }

        public void listen(UvStreamHandle handle, int backlog, uv_connection_cb cb)
        {
            handle.Validate();
            Check(UnsafeNativeMethods.uv_listen(handle, backlog, cb));
        }

        public void accept(UvStreamHandle server, UvStreamHandle client)
        {
            server.Validate();
            client.Validate();
            Check(UnsafeNativeMethods.uv_accept(server, client));
        }

        public void read_start(UvStreamHandle handle, uv_alloc_cb alloc_cb, uv_read_cb read_cb)
        {
            handle.Validate();
            Check(UnsafeNativeMethods.uv_read_start(handle, alloc_cb, read_cb));
        }

        public void read_stop(UvStreamHandle handle)
        {
            handle.Validate();
            Check(UnsafeNativeMethods.uv_read_stop(handle));
        }

        unsafe public void write(UvWriteReq req, UvStreamHandle handle, Libuv.uv_buf_t* bufs, int nbufs, uv_write_cb cb)
        {
            req.Validate();
            handle.Validate();
            Check(UnsafeNativeMethods.uv_write(req, handle, bufs, nbufs, cb));
        }

        public void shutdown(UvShutdownReq req, UvStreamHandle handle, uv_shutdown_cb cb)
        {
            req.Validate();
            handle.Validate();
            Check(UnsafeNativeMethods.uv_shutdown(req, handle, cb));
        }

        public int loop_size()
        {
            return UnsafeNativeMethods.uv_loop_size();
        }

        public int handle_size(HandleType handleType)
        {
            return UnsafeNativeMethods.uv_handle_size(handleType);
        }

        public int req_size(RequestType reqType)
        {
            return UnsafeNativeMethods.uv_req_size(reqType);
        }

        public int ip4_addr(string ip, int port, out sockaddr addr, out Exception error)
        {
            return Check(UnsafeNativeMethods.uv_ip4_addr(ip, port, out addr), out error);
        }

        public int ip6_addr(string ip, int port, out sockaddr addr, out Exception error)
        {
            return Check(UnsafeNativeMethods.uv_ip6_addr(ip, port, out addr), out error);
        }

        public void walk(UvLoopHandle loop, uv_walk_cb walk_cb, IntPtr arg)
        {
            loop.Validate();
            UnsafeNativeMethods.uv_walk(loop, walk_cb, arg);
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
