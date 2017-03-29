// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Networking
{
    /// <summary>
    /// Summary description for UvShutdownRequest
    /// </summary>
    public class UvShutdownReq : UvRequest
    {
        private readonly static Libuv.uv_shutdown_cb _uv_shutdown_cb = UvShutdownCb;

        private Action<UvShutdownReq, int, object> _callback;
        private object _state;

        public UvShutdownReq(IKestrelTrace logger) : base (logger)
        {
        }

        public void Init(UvLoopHandle loop)
        {
            CreateMemory(
                loop.Libuv, 
                loop.ThreadId,
                loop.Libuv.req_size(Libuv.RequestType.SHUTDOWN));
        }

        public void Shutdown(UvStreamHandle handle, Action<UvShutdownReq, int, object> callback, object state)
        {
            _callback = callback;
            _state = state;
            Pin();
            _uv.shutdown(this, handle, _uv_shutdown_cb);
        }

        private static void UvShutdownCb(IntPtr ptr, int status)
        {
            var req = FromIntPtr<UvShutdownReq>(ptr);
            req.Unpin();
            req._callback(req, status, req._state);
            req._callback = null;
            req._state = null;
        }
    }
}