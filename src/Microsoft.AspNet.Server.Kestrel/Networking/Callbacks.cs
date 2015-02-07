using System;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void uv_close_cb(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void uv_async_cb(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void uv_connection_cb(IntPtr server, int status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void uv_alloc_cb(IntPtr server, int suggested_size, out Libuv.uv_buf_t buf);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void uv_read_cb(IntPtr server, int nread, ref Libuv.uv_buf_t buf);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void uv_write_cb(IntPtr req, int status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void uv_shutdown_cb(IntPtr req, int status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void uv_walk_cb(IntPtr handle, IntPtr arg);
}