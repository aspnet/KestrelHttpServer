using System;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    internal static class UnsafeNativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDllDirectory(string path);

        private const string libuv = "libuv.dll";

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public static extern int uv_loop_init(UvLoopHandle handle);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public static extern int uv_loop_close(UvLoopHandle handle);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public static extern int uv_run(UvLoopHandle handle, int mode);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static void uv_stop(UvLoopHandle handle);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static void uv_ref(IntPtr handle);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static void uv_unref(IntPtr handle);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static void uv_close(IntPtr handle, uv_close_cb close_cb);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_async_init(UvLoopHandle loop, IntPtr handle, uv_async_cb cb);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_async_send(IntPtr handle);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_tcp_init(UvLoopHandle loop, IntPtr handle);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_tcp_bind(IntPtr handle, ref Sockaddr addr, int flags);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_listen(IntPtr handle, int backlog, uv_connection_cb cb);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_accept(IntPtr server, IntPtr client);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_read_start(IntPtr handle, uv_alloc_cb alloc_cb, uv_read_cb read_cb);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_read_stop(IntPtr handle);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        // uw_write expects an array, but only one buffer is ever written.
        // So a pointer (ref) to a single UvBuffer and a constant length of 1 is sufficient.
        public extern static int uv_write(UvWriteReq req, IntPtr handle, ref UvBuffer bufs, int nbufs, uv_write_cb cb);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_shutdown(UvShutdownReq req, IntPtr handle, uv_shutdown_cb cb);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        // Cannot use [return: MarshalAs(UnmanagedType.LPStr)]
        // because the source const char* must not be freed,
        // which the marshaling does
        public extern static IntPtr uv_err_name(int err);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        // Cannot use [return: MarshalAs(UnmanagedType.LPStr)]
        // because the source const char* must not be freed,
        // which the marshaling does
        public extern static IntPtr uv_strerror(int err);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_loop_size();

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_handle_size(HandleType handleType);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_req_size(RequestType reqType);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_ip4_addr(string ip, int port, out Sockaddr addr);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_ip6_addr(string ip, int port, out Sockaddr addr);

        [DllImport(libuv, CallingConvention = CallingConvention.Cdecl)]
        public extern static int uv_walk(UvLoopHandle loop, uv_walk_cb walk_cb, IntPtr arg);
    }
}