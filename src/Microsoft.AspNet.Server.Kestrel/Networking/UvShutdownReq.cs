// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    /// <summary>
    /// Summary description for UvShutdownRequest
    /// </summary>
    public class UvShutdownReq : UvReq
    {
        private readonly static uv_shutdown_cb _uv_shutdown_cb = UvShutdownCb;

        private Action<UvShutdownReq, int, object> _callback;
        private object _state;

        public UvShutdownReq(UvLoopHandle loop)
            : base(loop.ThreadId, getSize())
        { }

        private static int getSize()
        {
            return UnsafeNativeMethods.uv_req_size(RequestType.SHUTDOWN);
        }

        public void Shutdown(UvStreamHandle handle, Action<UvShutdownReq, int, object> callback, object state)
        {
            _callback = callback;
            _state = state;
            Validate();
            handle.Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_shutdown(this, handle, _uv_shutdown_cb));
        }

        private static void UvShutdownCb(IntPtr ptr, int status)
        {
            var req = FromIntPtr<UvShutdownReq>(ptr);
            req._callback(req, status, req._state);
            req._callback = null;
            req._state = null;
        }
    }
}