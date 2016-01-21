// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    /// <summary>
    /// Summary description for UvWriteRequest
    /// </summary>
    public class UvWriteReq : UvRequest
    {
        private readonly static Libuv.uv_write_cb _uv_write_cb = (IntPtr ptr, int status) => UvWriteCb(ptr, status);

        private IntPtr _bufs;

        private Action<UvWriteReq, int, Exception, object> _callback;
        private object _state;
        private const int BUFFER_COUNT = 4;

        private GCHandle _pinUvWriteReq;
        private GCHandle _pinBufferArray;
        private bool _bufferArrayIsPinned;

        public UvWriteReq(IKestrelTrace logger) : base(logger)
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

        public unsafe void Write(
            UvStreamHandle handle,
            MemoryPoolIterator2 start,
            MemoryPoolIterator2 end,
            int nBuffers,
            Action<UvWriteReq, int, Exception, object> callback,
            object state)
        {
            try
            {
                // add GCHandle to keeps this SafeHandle alive while request processing
                _pinUvWriteReq = GCHandle.Alloc(this, GCHandleType.Normal);

                var pBuffers = (Libuv.uv_buf_t*)_bufs;
                if (nBuffers > BUFFER_COUNT)
                {
                    // create and pin buffer array when it's larger than the pre-allocated one
                    var bufArray = new Libuv.uv_buf_t[nBuffers];
                    _pinBufferArray = GCHandle.Alloc(bufArray, GCHandleType.Pinned);
                    _bufferArrayIsPinned = true;
                    pBuffers = (Libuv.uv_buf_t*)_pinBufferArray.AddrOfPinnedObject();
                }

                var block = start.Block;
                for (var index = 0; index < nBuffers; index++)
                {
                    var blockStart = block == start.Block ? start.Index : block.Data.Offset;
                    var blockEnd = block == end.Block ? end.Index : block.Data.Offset + block.Data.Count;

                    pBuffers[index] = Libuv.buf_init(
                        block.Pin() + blockStart,
                        blockEnd - blockStart);

                    block = block.Next;
                }

                _callback = callback;
                _state = state;
                _uv.write(this, handle, pBuffers, nBuffers, _uv_write_cb);
            }
            catch
            {
                _callback = null;
                _state = null;
                Unpin(this);

                var block = start.Block;
                for (var index = 0; index < nBuffers; index++)
                {
                    block = block.Next;
                }

                throw;
            }
        }

        private static void Unpin(UvWriteReq req)
        {
            req._pinUvWriteReq.Free();
            if (req._bufferArrayIsPinned)
            {
                req._pinBufferArray.Free();
                req._bufferArrayIsPinned = false;
            }
        }

        private static void UvWriteCb(IntPtr ptr, int status)
        {
            var req = FromIntPtr<UvWriteReq>(ptr);
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
                req._log.LogError("UvWriteCb", ex);
                throw;
            }
        }
    }
}