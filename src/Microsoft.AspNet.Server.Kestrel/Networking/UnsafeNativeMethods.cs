using System;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    internal static class UnsafeNativeMethods
    {
#pragma warning disable 649

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_loop_init_delegate(UvLoopHandle handle);
        public static uv_loop_init_delegate uv_loop_init;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_loop_close_delegate(UvLoopHandle handle);
        public static uv_loop_close_delegate uv_loop_close;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_run_delegate(UvLoopHandle handle, int mode);
        public static uv_run_delegate uv_run;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void uv_ref_delegate(IntPtr handle);
        public static uv_ref_delegate uv_ref;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void uv_unref_delegate(IntPtr handle);
        public static uv_unref_delegate uv_unref;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void uv_close_delegate(IntPtr handle, uv_close_cb close_cb);
        public static uv_close_delegate uv_close;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_async_init_delegate(UvLoopHandle loop, IntPtr handle, uv_async_cb cb);
        public static uv_async_init_delegate uv_async_init;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_async_send_delegate(IntPtr handle);
        public static uv_async_send_delegate uv_async_send;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_tcp_init_delegate(UvLoopHandle loop, IntPtr handle);
        public static uv_tcp_init_delegate uv_tcp_init;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_tcp_bind_delegate(IntPtr handle, ref Sockaddr addr, int flags);
        public static uv_tcp_bind_delegate uv_tcp_bind;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_listen_delegate(IntPtr handle, int backlog, uv_connection_cb cb);
        public static uv_listen_delegate uv_listen;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_accept_delegate(IntPtr server, IntPtr client);
        public static uv_accept_delegate uv_accept;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_read_start_delegate(IntPtr handle, uv_alloc_cb alloc_cb, uv_read_cb read_cb);
        public static uv_read_start_delegate uv_read_start;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_read_stop_delegate(IntPtr handle);
        public static uv_read_stop_delegate uv_read_stop;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_write_delegate(UvWriteReq req, IntPtr handle, UvBuffer[] bufs, int nbufs, uv_write_cb cb);
        public static uv_write_delegate uv_write;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_shutdown_delegate(UvShutdownReq req, IntPtr handle, uv_shutdown_cb cb);
        public static uv_shutdown_delegate uv_shutdown;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        // Cannot use [return: MarshalAs(UnmanagedType.LPStr)]
        // because the source const char* must not be freed,
        // which the marshaling does
        public delegate IntPtr uv_err_name_delegate(int err);
        public static uv_err_name_delegate uv_err_name;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        // Cannot use [return: MarshalAs(UnmanagedType.LPStr)]
        // because the source const char* must not be freed,
        // which the marshaling does
        public delegate IntPtr uv_strerror_delegate(int err);
        public static uv_strerror_delegate uv_strerror;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_loop_size_delegate();
        public static uv_loop_size_delegate uv_loop_size;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_handle_size_delegate(HandleType handleType);
        public static uv_handle_size_delegate uv_handle_size;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_req_size_delegate(RequestType reqType);
        public static uv_req_size_delegate uv_req_size;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_ip4_addr_delegate(string ip, int port, out Sockaddr addr);
        public static uv_ip4_addr_delegate uv_ip4_addr;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_ip6_addr_delegate(string ip, int port, out Sockaddr addr);
        public static uv_ip6_addr_delegate uv_ip6_addr;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int uv_walk_delegate(UvLoopHandle loop, uv_walk_cb walk_cb, IntPtr arg);
        public static uv_walk_delegate uv_walk;
    }
}
