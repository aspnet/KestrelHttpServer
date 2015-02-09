// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class UvAsyncHandle : UvLoopResource
    {
        private readonly uv_async_cb _uv_async_cb;
        private readonly Action _callback;

        public UvAsyncHandle(
            UvLoopHandle loop,
            Action callback)
            : base(loop.ThreadId, getSize())
        {
            _uv_async_cb = AsyncCb;
            _callback = callback;
            loop.Validate();
            Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_async_init(loop, Handle, _uv_async_cb));
        }

        private static int getSize()
        {
            return UnsafeNativeMethods.uv_handle_size(HandleType.ASYNC);
        }

        public void Send()
        {
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_async_send(Handle));
        }

        private void AsyncCb(IntPtr handle)
        {
            _callback.Invoke();
        }

        public void Reference()
        {
            Validate();
            UnsafeNativeMethods.uv_ref(Handle);
        }

        public void Unreference()
        {
            Validate();
            UnsafeNativeMethods.uv_unref(Handle);
        }
    }
}
