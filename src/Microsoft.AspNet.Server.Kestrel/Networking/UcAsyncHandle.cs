// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class UvAsyncHandle : UvHandle
    {
        private static uv_async_cb _uv_async_cb = AsyncCb;
        private Action _callback;

        public UvAsyncHandle(UvLoopHandle loop, Action callback)
            :base(loop.ThreadId, getSize(), null)
        {
            _callback = callback;
            loop.Validate();
            Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_async_init(loop, this, _uv_async_cb));
        }

        private static int getSize()
        {
            return UnsafeNativeMethods.uv_handle_size(HandleType.ASYNC);
        }

        public void DangerousClose()
        {
            Dispose();
            ReleaseHandle();
        }

        public void Send()
        {
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_async_send(this));
        }

        static void AsyncCb(IntPtr handle)
        {
            FromIntPtr<UvAsyncHandle>(handle)._callback.Invoke();
        }
    }
}
