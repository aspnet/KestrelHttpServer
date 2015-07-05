// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public abstract class UvHandle : UvMemory
    {
        static uv_close_cb _destroyMemory = DestroyMemory;
        Action<Action<IntPtr>, IntPtr> _queueCloseHandle;

        unsafe protected void CreateHandle(
            int threadId,
            int size,
            Action<Action<IntPtr>, IntPtr> queueCloseHandle)
        {
            _queueCloseHandle = queueCloseHandle;
            CreateMemory(threadId, size);
        }

        protected override bool ReleaseHandle()
        {
            var memory = handle;
            if (memory != IntPtr.Zero)
            {
                handle = IntPtr.Zero;

                if (Thread.CurrentThread.ManagedThreadId == ThreadId)
                {
                    UnsafeNativeMethods.uv_close(memory, _destroyMemory);
                }
                else if (_queueCloseHandle != null)
                {
                    _queueCloseHandle(memory2 => UnsafeNativeMethods.uv_close(memory2, _destroyMemory), memory);
                }
            }
            return true;
        }

        public void Reference()
        {
            Validate();
            UnsafeNativeMethods.uv_ref(this);
        }

        public void Unreference()
        {
            Validate();
            UnsafeNativeMethods.uv_unref(this);
        }
    }
}
