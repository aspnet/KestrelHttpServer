// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    /// <summary>
    /// Summary description for UvWriteRequest
    /// </summary>
    public class UvWriteReq : UvReq
    {
        private readonly static uv_write_cb _uv_write_cb = UvWriteCb;

        IntPtr _bufs;

        Action<UvWriteReq, int, Exception, object> _callback;
        object _state;
        const int BUFFER_COUNT = 4;

        List<GCHandle> _pins = new List<GCHandle>();

        public void Init(UvLoopHandle loop)
        {
            var requestSize = UnsafeNativeMethods.uv_req_size(RequestType.WRITE);
            var bufferSize = Marshal.SizeOf(typeof(UvBuffer)) * BUFFER_COUNT;
            CreateMemory(
                loop.ThreadId,
                requestSize + bufferSize);
            _bufs = handle + requestSize;
        }

        public unsafe void Write(
            UvStreamHandle handle,
            ArraySegment<ArraySegment<byte>> bufs,
            Action<UvWriteReq, int, Exception, object> callback,
            object state)
        {
            try
            {
                // add GCHandle to keeps this SafeHandle alive while request processing
                _pins.Add(GCHandle.Alloc(this, GCHandleType.Normal));

                var pBuffers = (UvBuffer*)_bufs;
                var nBuffers = bufs.Count;
                if (nBuffers > BUFFER_COUNT)
                {
                    // create and pin buffer array when it's larger than the pre-allocated one
                    var bufArray = new UvBuffer[nBuffers];
                    var gcHandle = GCHandle.Alloc(bufArray, GCHandleType.Pinned);
                    _pins.Add(gcHandle);
                    pBuffers = (UvBuffer*)gcHandle.AddrOfPinnedObject();
                }

                for (var index = 0; index != nBuffers; ++index)
                {
                    // create and pin each segment being written
                    var buf = bufs.Array[bufs.Offset + index];

                    var gcHandle = GCHandle.Alloc(buf.Array, GCHandleType.Pinned);
                    _pins.Add(gcHandle);
                    pBuffers[index] = new UvBuffer(
                        gcHandle.AddrOfPinnedObject() + buf.Offset,
                        buf.Count);
                }

                _callback = callback;
                _state = state;
                handle.Validate();
                Validate();
                Libuv.ThrowOnError(UnsafeNativeMethods.uv_write(this, handle, pBuffers, nBuffers, _uv_write_cb));
            }
            catch
            {
                _callback = null;
                _state = null;
                Unpin(this);
                throw;
            }
        }

        private static void Unpin(UvWriteReq req)
        {
            foreach (var pin in req._pins)
            {
                pin.Free();
            }
            req._pins.Clear();
        }

        private static void UvWriteCb(IntPtr ptr, int status)
        {
            var req = FromIntPtr<UvWriteReq>(ptr);
            Unpin(req);

            var callback = req._callback;
            req._callback = null;

            var state = req._state;
            req._state = null;

            var error = Libuv.ExceptionForError(status);

            try
            {
                callback(req, status, error, state);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("UvWriteCb " + ex.ToString());
            }
        }
    }

    public abstract class UvReq : UvMemory
    {
        protected override bool ReleaseHandle()
        {
            DestroyMemory(handle);
            handle = IntPtr.Zero;
            return true;
        }
    }
}
