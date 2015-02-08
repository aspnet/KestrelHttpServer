// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
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

        Action<UvWriteReq, int, Exception, object> _callback;
        object _state;
        List<GCHandle> _pins = new List<GCHandle>();

        public UvWriteReq(UvLoopHandle loop)
        {
            var requestSize = UnsafeNativeMethods.uv_req_size(RequestType.WRITE);
            CreateMemory(
                loop.ThreadId,
                requestSize);
        }

        public void Write(
            UvStreamHandle handle,
            byte[] buf,
            Action<UvWriteReq, int, Exception, object> callback,
            object state)
        {
            try
            {
                // add GCHandle to keeps this SafeHandle alive while request processing
                _pins.Add(GCHandle.Alloc(this, GCHandleType.Normal));
                var bufHandle = GCHandle.Alloc(buf, GCHandleType.Pinned);
                _pins.Add(bufHandle);

                var uvBuffer = new UvBuffer(
                        bufHandle.AddrOfPinnedObject(),
                        buf.Length);

                _callback = callback;
                _state = state;
                handle.Validate();
                Validate();
                Libuv.ThrowOnError(UnsafeNativeMethods.uv_write(this, handle, ref uvBuffer, 1, _uv_write_cb));
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