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
        private readonly uv_write_cb _uv_write_cb;

        private Action<UvWriteReq, int, Exception, object> _callback;
        private object _state;
        private List<GCHandle> _pins = new List<GCHandle>();

        public UvWriteReq(UvLoopHandle loop)
            : base(loop.ThreadId, getSize())
        {
            _uv_write_cb = UvWriteCb;
        }

        private static int getSize()
        {
            return UnsafeNativeMethods.uv_req_size(RequestType.WRITE);
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
                Unpin();
                throw;
            }
        }

        private void Unpin()
        {
            foreach (var pin in _pins)
            {
                pin.Free();
            }
            _pins.Clear();
        }

        private void UvWriteCb(IntPtr ptr, int status)
        {
            Unpin();

            var callback = _callback;
            _callback = null;

            var state = _state;
            _state = null;

            var error = Libuv.ExceptionForError(status);

            try
            {
                callback(this, status, error, state);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("UvWriteCb " + ex.ToString());
            }
        }
    }
}