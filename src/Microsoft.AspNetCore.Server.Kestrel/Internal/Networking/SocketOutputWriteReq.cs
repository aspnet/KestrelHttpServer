// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Networking
{
    /// <summary>
    /// Summary description for SocketOutputWriteReq
    /// </summary>
    public class SocketOutputWriteReq : UvRequest
    {
        private readonly static Libuv.uv_write_cb _uv_write_cb = 
            (IntPtr ptr, int status) => FromIntPtr<SocketOutputWriteReq>(ptr).UvWriteCallback(status);

        private IntPtr _bufs;

        private SocketOutput.WriteContext _writeContext;
        private const int BUFFER_COUNT = 4;

        private readonly List<GCHandle> _pins = new List<GCHandle>(BUFFER_COUNT + 1);

        public SocketOutputWriteReq(IKestrelTrace logger) : base(logger)
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
            SocketOutput.WriteContext context)
        {
            try
            {
                var bufferCount = context._bufferCount;
                // add GCHandle to keeps this SafeHandle alive while request processing
                _pins.Add(GCHandle.Alloc(this, GCHandleType.Normal));

                var pBuffers = (Libuv.uv_buf_t*)_bufs;
                if (bufferCount > BUFFER_COUNT)
                {
                    // create and pin buffer array when it's larger than the pre-allocated one
                    var bufArray = new Libuv.uv_buf_t[bufferCount];
                    var gcHandle = GCHandle.Alloc(bufArray, GCHandleType.Pinned);
                    _pins.Add(gcHandle);
                    pBuffers = (Libuv.uv_buf_t*)gcHandle.AddrOfPinnedObject();
                }

                var block = context._lockedStart.Block;
                for (var index = 0; index < bufferCount; index++)
                {
                    var blockStart = block == context._lockedStart.Block ? context._lockedStart.Index : block.Data.Offset;
                    var blockEnd = block == context._lockedEnd.Block ? context._lockedEnd.Index : block.Data.Offset + block.Data.Count;

                    // create and pin each segment being written
                    pBuffers[index] = Libuv.buf_init(
                        block.DataArrayPtr + blockStart,
                        blockEnd - blockStart);

                    block = block.Next;
                }

                _writeContext = context;
                _uv.write(this, handle, pBuffers, bufferCount, _uv_write_cb);
            }
            catch
            {
                _writeContext = null;
                UnpinGcHandles();
                throw;
            }
        }

        private void UnpinGcHandles()
        {
            var count = _pins.Count;
            for (var i = 0; i < count; i++)
            {
                _pins[i].Free();
            }
            _pins.Clear();
        }

        private void UvWriteCallback(int status)
        {
            UnpinGcHandles();

            var writeContext = _writeContext;
            _writeContext = null;

            Exception error = null;
            if (status < 0)
            {
                Libuv.Check(status, out error);
            }

            try
            {
                writeContext.WriteCallback(status, error);
            }
            catch (Exception ex)
            {
                _log.LogError(0, ex, "UvWriteCallback");
                throw;
            }
        }
    }
}