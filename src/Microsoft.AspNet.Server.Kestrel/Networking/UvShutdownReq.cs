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
        private readonly Action<UvShutdownReq, int> _callback;

        public UvShutdownReq(UvLoopHandle loop, UvTcpStreamHandle stream, Action<UvShutdownReq, int> callback)
            : base(loop.ThreadId, GetSize())
        {
            _uv_shutdown_cb = UvShutdownCb;

            _callback = callback;
            Validate();
            stream.Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_shutdown(this, stream.Handle, _uv_shutdown_cb));
        }

        private static int GetSize()
        {
            return UnsafeNativeMethods.uv_req_size(RequestType.SHUTDOWN);
        }

        private void UvShutdownCb(IntPtr ptr, int status)
        {
            _callback(this, status);
            Dispose();
        }
    }
}