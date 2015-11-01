// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
        private readonly static Libuv.uv_write_cb _uv_write_cb = (ptr, status) => UvWriteCb(ptr, status);

        private IntPtr _nativePointers;
        private ArraySegment<MemoryPoolBlock2> _segments;

        private Action<UvWriteReq, int, Exception, int, object> _callback;
        private object _state;
        public const int BUFFER_COUNT = 8;
        
        public bool SocketShutdownSend;
        public bool SocketDisconnect;

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
            _nativePointers = handle + requestSize;
        }

        public unsafe void Write(
            UvStreamHandle handle,
            ArraySegment<MemoryPoolBlock2> segments,
            Action<UvWriteReq, int, Exception, int, object> callback,
            object state)
        {
            try
            {
                _segments = segments;
                // add GCHandle to keeps this SafeHandle alive while request processing
                Pin();

                var pBuffers = (Libuv.uv_buf_t*)_nativePointers;
                var nBuffers = segments.Count;

                for (var index = 0; index < nBuffers; index++)
                {
                    var buf = segments.Array[segments.Offset + index];
                    var len = buf.End - buf.Start;
                    // create and pin each segment being written
                    pBuffers[index] = Libuv.buf_init(
                        buf.Pin() - len,
                        buf.End - buf.Start);
                }

                _callback = callback;
                _state = state;
                _uv.write(this, handle, pBuffers, nBuffers, _uv_write_cb);
            }
            catch
            {
                _callback = null;
                _state = null;
                Unpin();
                ProcessBlocks();
                throw;
            }
        }

        public unsafe void Write2(
            UvStreamHandle handle,
            ArraySegment<MemoryPoolBlock2> segments,
            UvStreamHandle sendHandle,
            Action<UvWriteReq, int, Exception, int, object> callback,
            object state)
        {
            try
            {
                _segments = segments;
                // add GCHandle to keeps this SafeHandle alive while request processing
                Pin();

                var pBuffers = (Libuv.uv_buf_t*)_nativePointers;
                var nBuffers = segments.Count;

                for (var index = 0; index < nBuffers; index++)
                {
                    var buf = segments.Array[segments.Offset + index];
                    var len = buf.End - buf.Start;

                    // create and pin each segment being written
                    pBuffers[index] = Libuv.buf_init(
                        buf.Pin() - len,
                        buf.End - buf.Start);
                }

                _callback = callback;
                _state = state;
                _uv.write2(this, handle, pBuffers, nBuffers, sendHandle, _uv_write_cb);
            }
            catch
            {
                _callback = null;
                _state = null;
                Unpin();
                ProcessBlocks();
                throw;
            }
        }

        private int ProcessBlocks()
        {
            var bytesWritten = 0;
            var end = _segments.Offset + _segments.Count;
            for (var i = _segments.Offset; i < end; i++)
            {
                var block = _segments.Array[i];
                bytesWritten += block.End - block.Start;

                block.Unpin();

                if (block.Pool != null)
                {
                    block.Pool.Return(block);
                }
            }

            return bytesWritten;
        }

        private static void UvWriteCb(IntPtr ptr, int status)
        {
            var req = FromIntPtr<UvWriteReq>(ptr);
            var bytesWritten = req.ProcessBlocks();

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
                callback(req, status, error, bytesWritten, state);
            }
            catch (Exception ex)
            {
                req._log.LogError("UvWriteCb", ex);
                throw;
            }
            finally
            {
                req.Unpin();
            }
        }
    }
}