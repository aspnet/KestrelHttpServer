// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class UvTcpStreamHandle : UvTcpHandle
    {
        private readonly uv_alloc_cb _uv_alloc_cb;
        private readonly uv_read_cb _uv_read_cb;

        private Func<UvTcpStreamHandle, int, UvBuffer> _allocCallback;
        private Action<UvTcpStreamHandle, int, Exception> _readCallback;

        private GCHandle _readVitality;

        public UvTcpStreamHandle(UvLoopHandle loop, UvTcpListenHandle listenHandle)
            : base(loop)
        {
            _uv_alloc_cb = UvAllocCb;
            _uv_read_cb = UvReadCb;

            listenHandle.Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_accept(listenHandle.Handle, Handle));
        }

        protected override void Dispose(bool disposing)
        {
            if (_readVitality.IsAllocated)
            {
                _readVitality.Free();
            }
            base.Dispose(disposing);
        }

        public void ReadStart(
            Func<UvTcpStreamHandle, int, UvBuffer> allocCallback,
            Action<UvTcpStreamHandle, int, Exception> readCallback)
        {
            if (_readVitality.IsAllocated)
            {
                throw new InvalidOperationException("TODO: ReadStop must be called before ReadStart may be called again");
            }
            try
            {
                _allocCallback = allocCallback;
                _readCallback = readCallback;
                _readVitality = GCHandle.Alloc(this, GCHandleType.Normal);
                Validate();

                Libuv.ThrowOnError(UnsafeNativeMethods.uv_read_start(Handle, _uv_alloc_cb, _uv_read_cb));
            }
            catch
            {
                _allocCallback = null;
                _readCallback = null;
                if (_readVitality.IsAllocated)
                {
                    _readVitality.Free();
                }
                throw;
            }
        }

        public void ReadStop()
        {
            if (!_readVitality.IsAllocated)
            {
                throw new InvalidOperationException("TODO: ReadStart must be called before ReadStop may be called");
            }
            _allocCallback = null;
            _readCallback = null;
            _readVitality.Free();
            Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_read_stop(Handle));
        }


        private void UvAllocCb(IntPtr handle, int suggested_size, out UvBuffer buf)
        {
            buf = _allocCallback(this, suggested_size);
        }

        private void UvReadCb(IntPtr handle, int nread, ref UvBuffer buf)
        {
            if (nread < 0)
            {
                var error = Libuv.ExceptionForError(nread);
                _readCallback(this, 0, error);
            }
            else
            {
                _readCallback(this, nread, null);
            }
        }
    }
}
