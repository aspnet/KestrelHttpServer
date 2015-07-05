// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public abstract class UvStreamHandle : UvHandle
    {
        private readonly static uv_connection_cb _uv_connection_cb = UvConnectionCb;
        private readonly static uv_alloc_cb _uv_alloc_cb = UvAllocCb;
        private readonly static uv_read_cb _uv_read_cb = UvReadCb;

        public Action<UvStreamHandle, int, Exception, object> _listenCallback;
        public object _listenState;
        private GCHandle _listenVitality;

        public Func<UvStreamHandle, int, object, UvBuffer> _allocCallback;
        public Action<UvStreamHandle, int, Exception, object> _readCallback;
        public object _readState;
        private GCHandle _readVitality;

        protected override bool ReleaseHandle()
        {
            if (_listenVitality.IsAllocated)
            {
                _listenVitality.Free();
            }
            if (_readVitality.IsAllocated)
            {
                _readVitality.Free();
            }
            return base.ReleaseHandle();
        }

        public void Listen(int backlog, Action<UvStreamHandle, int, Exception, object> callback, object state)
        {
            if (_listenVitality.IsAllocated)
            {
                throw new InvalidOperationException("TODO: Listen may not be called more than once");
            }
            try
            {
                _listenCallback = callback;
                _listenState = state;
                _listenVitality = GCHandle.Alloc(this, GCHandleType.Normal);
                Validate();
                Libuv.ThrowOnError(UnsafeNativeMethods.uv_listen(this, 10, _uv_connection_cb));
            }
            catch
            {
                _listenCallback = null;
                _listenState = null;
                if (_listenVitality.IsAllocated)
                {
                    _listenVitality.Free();
                }
                throw;
            }
        }

        public void Accept(UvStreamHandle handle)
        {
            Validate();
            handle.Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_accept(this, handle));
        }

        public void ReadStart(
            Func<UvStreamHandle, int, object, UvBuffer> allocCallback,
            Action<UvStreamHandle, int, Exception, object> readCallback,
            object state)
        {
            if (_readVitality.IsAllocated)
            {
                throw new InvalidOperationException("TODO: ReadStop must be called before ReadStart may be called again");
            }
            try
            {
                _allocCallback = allocCallback;
                _readCallback = readCallback;
                _readState = state;
                _readVitality = GCHandle.Alloc(this, GCHandleType.Normal);
                Validate();
                Libuv.ThrowOnError(UnsafeNativeMethods.uv_read_start(this, _uv_alloc_cb, _uv_read_cb));
            }
            catch
            {
                _allocCallback = null;
                _readCallback = null;
                _readState = null;
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
            _readState = null;
            _readVitality.Free();
            Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_read_stop(this));
        }

        private static void UvConnectionCb(IntPtr handle, int status)
        {
            var stream = FromIntPtr<UvStreamHandle>(handle);

            var error = Libuv.ExceptionForError(status);

            try
            {
                stream._listenCallback(stream, status, error, stream._listenState);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("UvConnectionCb " + ex.ToString());
            }
        }


        private static void UvAllocCb(IntPtr handle, int suggested_size, out UvBuffer buf)
        {
            var stream = FromIntPtr<UvStreamHandle>(handle);
            try
            {
                buf = stream._allocCallback(stream, suggested_size, stream._readState);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("UvAllocCb " + ex.ToString());
                buf = default(UvBuffer);
                throw;
            }
        }

        private static void UvReadCb(IntPtr handle, int nread, ref UvBuffer buf)
        {
            var stream = FromIntPtr<UvStreamHandle>(handle);

            try
            {
                if (nread < 0)
                {
                    var error = Libuv.ExceptionForError(nread);
                    stream._readCallback(stream, 0, error, stream._readState);
                }
                else
                {
                    stream._readCallback(stream, nread, null, stream._readState);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("UbReadCb " + ex.ToString());
            }
        }

    }
}
