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

        private readonly UvStreamHandle _stream;
        private readonly Action<Exception, object> _callback;
        private readonly object _state;

        private readonly GCHandle _selfKeepAlive;
        private readonly UvBuffer[] _uvBuffers;
        private readonly GCHandle[] _bufferHandles;
        private readonly GCHandle _bufferArrayHandle;

        public UvWriteReq(
            UvLoopHandle loop,
            UvStreamHandle stream,
            ArraySegment<byte> buffer,
            Action<Exception, object> callback,
            object state)
            : base(loop.ThreadId, getSize())
        {
            _uv_write_cb = UvWriteCb;
            _stream = stream;
            _callback = callback;
            _state = state;

            _selfKeepAlive = GCHandle.Alloc(this, GCHandleType.Normal);
            _bufferHandles = new GCHandle[1];
            _uvBuffers = new UvBuffer[1];
            _bufferArrayHandle = GCHandle.Alloc(_uvBuffers, GCHandleType.Pinned);

            _bufferHandles[0] = GCHandle.Alloc(buffer.Array, GCHandleType.Pinned);
            _uvBuffers[0] = new UvBuffer(
                _bufferHandles[0].AddrOfPinnedObject() + buffer.Offset,
                buffer.Count);
        }

        private static int getSize()
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
            var error = Libuv.ExceptionForError(status);

            KestrelTrace.Log.ConnectionWriteCallback(0, status);
            //NOTE: pool this?

            Dispose();
            _callback(error, _state);
        }

        protected override bool ReleaseHandle()
        {
            foreach (var bufferHandle in _bufferHandles)
                bufferHandle.Free();
            _bufferArrayHandle.Free();
            _selfKeepAlive.Free();

            return base.ReleaseHandle();
        }
    }
}
