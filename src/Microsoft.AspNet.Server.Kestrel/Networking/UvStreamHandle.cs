// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.Framework.Logging;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public abstract class UvStreamHandle : UvHandle
    {
        private readonly static Libuv.uv_connection_cb _uv_connection_cb = UvConnectionCb;
        private readonly static Libuv.uv_alloc_cb _uv_alloc_cb = UvAllocCb;
        private readonly static Libuv.uv_read_cb _uv_read_cb = UvReadCb;

        public Action<UvStreamHandle, int, Exception, object> _listenCallback;
        public object _listenState;
        private GCHandle _listenVitality;

        public Func<UvStreamHandle, int, object, Libuv.uv_buf_t> _allocCallback;
        public Action<UvStreamHandle, int, int, Exception, object> _readCallback;
        public object _readState;
        private GCHandle _readVitality;

        protected UvStreamHandle(IKestrelTrace logger) : base(logger)
        {
        }

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
                _uv.listen(this, backlog, _uv_connection_cb);
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
            _uv.accept(this, handle);
        }

        public void ReadStart(
            Func<UvStreamHandle, int, object, Libuv.uv_buf_t> allocCallback,
            Action<UvStreamHandle, int, int, Exception, object> readCallback,
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
                _uv.read_start(this, _uv_alloc_cb, _uv_read_cb);
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
            _uv.read_stop(this);
        }

        public int TryWrite(Libuv.uv_buf_t buf)
        {
            return _uv.try_write(this, new[] { buf }, 1);
        }


        private static void UvConnectionCb(IntPtr handle, int status)
        {
            var stream = FromIntPtr<UvStreamHandle>(handle);

            Exception error;
            status = stream.Libuv.Check(status, out error);

            try
            {
                stream._listenCallback(stream, status, error, stream._listenState);
            }
            catch (Exception ex)
            {
                stream._log.LogError("UvConnectionCb", ex);
                throw;
            }
        }


        private static void UvAllocCb(IntPtr handle, int suggested_size, out Libuv.uv_buf_t buf)
        {
            var stream = FromIntPtr<UvStreamHandle>(handle);
            try
            {
                buf = stream._allocCallback(stream, suggested_size, stream._readState);
            }
            catch (Exception ex)
            {
                stream._log.LogError("UvAllocCb", ex);
                buf = stream.Libuv.buf_init(IntPtr.Zero, 0);
                throw;
            }
        }

        private static void UvReadCb(IntPtr handle, int nread, ref Libuv.uv_buf_t buf)
        {
            var stream = FromIntPtr<UvStreamHandle>(handle);

            try
            {
                if (nread < 0)
                {
                    Exception error;
                    stream._uv.Check(nread, out error);
                    stream._readCallback(stream, 0, nread, error, stream._readState);
                }
                else
                {
                    stream._readCallback(stream, nread, 0, null, stream._readState);
                }
            }
            catch (Exception ex)
            {
                stream._log.LogError("UbReadCb", ex);
                throw;
            }
        }

    }
}
