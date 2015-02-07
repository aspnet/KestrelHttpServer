// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class UvAsyncHandle : UvHandle
    {
        private static uv_async_cb _uv_async_cb = AsyncCb;
        private Action _callback;

        public void Init(UvLoopHandle loop, Action callback)
        {
            CreateMemory(
                loop.Libuv, 
                loop.ThreadId,
                UnsafeNativeMethods.uv_handle_size(HandleType.ASYNC));

            _callback = callback;
            loop.Validate();
            Validate();
            Libuv.Check(UnsafeNativeMethods.uv_async_init(loop, this, _uv_async_cb));
        }

        public void DangerousClose()
        {
            Dispose();
            ReleaseHandle();
        }

        public void Send()
        {
            Libuv.Check(UnsafeNativeMethods.uv_async_send(this));
        }

        unsafe static void AsyncCb(IntPtr handle)
        {
            FromIntPtr<UvAsyncHandle>(handle)._callback.Invoke();
        }
    }
}
