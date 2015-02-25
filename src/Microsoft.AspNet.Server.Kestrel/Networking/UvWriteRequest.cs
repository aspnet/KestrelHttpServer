// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    /// <summary>
    /// Summary description for UvWriteRequest
    /// </summary>
    public class UvWriteReq : UvMemoryResource
    {
        private readonly uv_write_cb _uv_write_cb;

        private readonly UvTcpStreamHandle _stream;

        private readonly UvBuffer[] _uvBuffers;
        private readonly GCHandle[] _bufferHandles;
        private readonly GCHandle _bufferArrayHandle;

        public UvWriteReq(
            UvLoopHandle loop,
            UvTcpStreamHandle stream,
            ArraySegment<byte> buffer)
            : base(loop.ThreadId, GetSize())
        {
            _uv_write_cb = UvWriteCb;
            _stream = stream;

            _bufferHandles = new GCHandle[1];
            _uvBuffers = new UvBuffer[1];
            _bufferArrayHandle = GCHandle.Alloc(_uvBuffers, GCHandleType.Pinned);

            _bufferHandles[0] = GCHandle.Alloc(buffer.Array, GCHandleType.Pinned);
            _uvBuffers[0] = new UvBuffer(
                _bufferHandles[0].AddrOfPinnedObject() + buffer.Offset,
                buffer.Count);
        }

        private static int GetSize()
        {
            return UnsafeNativeMethods.uv_req_size(RequestType.WRITE);
        }

        public void Write()
        {
            try
            {
                _stream.Validate();
                Validate();
                Libuv.ThrowOnError(UnsafeNativeMethods.uv_write(
                    this,
                    _stream.Handle,
                    _uvBuffers,
                    _uvBuffers.Length,
                    _uv_write_cb));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void UvWriteCb(IntPtr ptr, int status)
        {
            KestrelTrace.Log.ConnectionWriteCallback(0, status);

            Dispose();

            if (status == -125 || status == -4081) // UV_ECANCELED on Unix and Windows
                return;
            else
                Libuv.ThrowOnError(status);
        }

        protected override bool ReleaseHandle()
        {
            foreach (var bufferHandle in _bufferHandles)
                bufferHandle.Free();
            _bufferArrayHandle.Free();

            return base.ReleaseHandle();
        }
    }
}
