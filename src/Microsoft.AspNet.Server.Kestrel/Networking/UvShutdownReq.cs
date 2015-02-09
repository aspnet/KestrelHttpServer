// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    /// <summary>
    /// Summary description for UvShutdownRequest
    /// </summary>
    public class UvShutdownReq : UvMemoryResource
    {
        private readonly uv_shutdown_cb _uv_shutdown_cb;

        private Action<UvShutdownReq, int, object> _callback;
        private object _state;

        public UvShutdownReq(UvLoopHandle loop)
            : base(loop.ThreadId, getSize())
        {
            _uv_shutdown_cb = UvShutdownCb;
        }

        private static int getSize()
        {
            return UnsafeNativeMethods.uv_req_size(RequestType.SHUTDOWN);
        }

        public void Shutdown(UvStreamHandle stream, Action<UvShutdownReq, int, object> callback, object state)
        {
            _callback = callback;
            _state = state;
            Validate();
            stream.Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_shutdown(this, stream.Handle, _uv_shutdown_cb));
        }

        private void UvShutdownCb(IntPtr ptr, int status)
        {
            _callback(this, status, _state);
            _callback = null;
            _state = null;
        }
    }
}