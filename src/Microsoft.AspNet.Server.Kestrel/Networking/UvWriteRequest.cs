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
        private readonly List<GCHandle> _pins = new List<GCHandle>();

        private readonly UvStreamHandle _stream;
        private readonly byte[] _buffer;
        private readonly Action<Exception, object> _callback;
        private readonly object _state;

        public UvWriteReq(
            UvLoopHandle loop,
            UvStreamHandle stream,
            byte[] buffer,
            Action<Exception, object> callback,
            object state)
            : base(loop.ThreadId, getSize())
        {
            _uv_write_cb = UvWriteCb;
            _stream = stream;
            _buffer = buffer;
            _callback = callback;
            _state = state;
        }

        private static int getSize()
        {
            return UnsafeNativeMethods.uv_req_size(RequestType.WRITE);
        }

        public void Write()
        {
            try
            {
                // add GCHandle to keeps this SafeHandle alive while request processing
                _pins.Add(GCHandle.Alloc(this, GCHandleType.Normal));
                var bufHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
                _pins.Add(bufHandle);

                var uvBuffer = new UvBuffer(
                        bufHandle.AddrOfPinnedObject(),
                        _buffer.Length);

                _stream.Validate();
                Validate();
                Libuv.ThrowOnError(UnsafeNativeMethods.uv_write(this, _stream.Handle, ref uvBuffer, 1, _uv_write_cb));
            }
            catch
            {
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

            var error = Libuv.ExceptionForError(status);

            KestrelTrace.Log.ConnectionWriteCallback(0, status);
            //NOTE: pool this?

            Dispose();
            _callback(error, _state);
        }
    }
}