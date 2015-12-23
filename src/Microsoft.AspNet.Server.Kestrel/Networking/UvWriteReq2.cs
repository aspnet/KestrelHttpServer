// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    /// <summary>
    /// Summary description for UvWriteRequest2
    /// </summary>
    public class UvWrite2Req : UvRequest
    {
        private readonly static Libuv.uv_write_cb _uv_write2_cb = (IntPtr ptr, int status) => UvWrite2Cb(ptr, status);

        private IntPtr _bufs;

        private Action<UvWrite2Req, int, Exception, object> _callback;
        private object _state;
        private const int BUFFER_COUNT = 1;

        private GCHandle _pinUvWrite2Req;
        private GCHandle _pinBuffer;
        private bool _bufferIsPinned;

        public UvWrite2Req(IKestrelTrace logger) : base(logger)
        {
        }

        public void Init(UvLoopHandle loop)
        {
            var requestSize = loop.Libuv.req_size(Libuv.RequestType.WRITE);
            var bufferSize = Marshal.SizeOf<Libuv.uv_buf_t>() * BUFFER_COUNT;
            CreateMemory(
                loop.Libuv,
                loop.ThreadId,
                requestSize + bufferSize);
            _bufs = handle + requestSize;
        }

        public unsafe void Write2(
            UvStreamHandle handle,
            ArraySegment<byte> buf,
            UvStreamHandle sendHandle,
            Action<UvWrite2Req, int, Exception, object> callback,
            object state)
        {
            try
            {
                // add GCHandle to keeps this SafeHandle alive while request processing
                _pinUvWrite2Req = GCHandle.Alloc(this, GCHandleType.Normal);

                var pBuffers = (Libuv.uv_buf_t*)_bufs;
                _pinBuffer = GCHandle.Alloc(buf.Array, GCHandleType.Pinned);
                _bufferIsPinned = true;

                pBuffers[0] = Libuv.buf_init(
                    _pinBuffer.AddrOfPinnedObject() + buf.Offset,
                    buf.Count);

                _callback = callback;
                _state = state;
                _uv.write2(this, handle, pBuffers, BUFFER_COUNT, sendHandle, _uv_write2_cb);
            }
            catch
            {
                _callback = null;
                _state = null;
                Unpin(this);
                throw;
            }
        }

        private static void Unpin(UvWrite2Req req)
        {
            req._pinUvWrite2Req.Free();
            if (req._bufferIsPinned)
            {
                req._pinBuffer.Free();
                req._bufferIsPinned = false;
            }
        }

        private static void UvWrite2Cb(IntPtr ptr, int status)
        {
            var req = FromIntPtr<UvWrite2Req>(ptr);
            Unpin(req);

            var callback = req._callback;
            req._callback = null;

            var state = req._state;
            req._state = null;

            Exception error = null;
            if (status < 0)
            {
                req.Libuv.Check(status, out error);
            }

            try
            {
                callback(req, status, error, state);
            }
            catch (Exception ex)
            {
                req._log.LogError("UvWrite2Cb", ex);
                throw;
            }
        }
    }
}