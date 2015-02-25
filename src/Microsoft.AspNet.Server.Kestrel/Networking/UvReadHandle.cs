using System;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public sealed class UvReadHandle : IDisposable
    {
        private readonly uv_read_cb _uv_read_cb;
        private readonly uv_alloc_cb _uv_alloc_cb;

        private readonly Action<int, Exception> _readCallback;
        private readonly Func<int, UvBuffer> _allocCallback;

        private readonly UvTcpStreamHandle _tcpStreamHandle;
        private readonly GCHandle _selfKeepAlive;

        public UvReadHandle(
            UvTcpStreamHandle tcpStreamHandle,
            Func<int, UvBuffer> allocCallback,
            Action<int, Exception> readCallback)
        {
            _uv_read_cb = UvReadCb;
            _uv_alloc_cb = UvAllocCb;

            _allocCallback = allocCallback;
            _readCallback = readCallback;
            _tcpStreamHandle = tcpStreamHandle;

            _tcpStreamHandle.Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_read_start(_tcpStreamHandle.Handle, _uv_alloc_cb, _uv_read_cb));

            _selfKeepAlive = GCHandle.Alloc(this, GCHandleType.Normal);
        }

        private void UvAllocCb(IntPtr handle, int suggested_size, out UvBuffer buf)
        {
            buf = _allocCallback(suggested_size);
        }

        private void UvReadCb(IntPtr handle, int nread, ref UvBuffer buf)
        {
            if (nread < 0)
            {
                var error = Libuv.ExceptionForError(nread);
                _readCallback(0, error);
            }
            else
            {
                _readCallback(nread, null);
            }
        }

        public void Dispose()
        {
            _tcpStreamHandle.Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_read_stop(_tcpStreamHandle.Handle));

            Destroy();

            GC.SuppressFinalize(this);
        }

        ~UvReadHandle()
        {
            Destroy();

            // See UvLoopResource's finalizer comment

            Console.WriteLine("TODO: Warning! UvReadHandle was finalized instead of disposed.");
        }

        private void Destroy()
        {
            _selfKeepAlive.Free();
        }
    }
}