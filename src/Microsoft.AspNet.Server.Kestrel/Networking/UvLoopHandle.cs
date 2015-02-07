// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class UvLoopHandle : UvHandle
    {
        public void Init(Libuv uv)
        {
            CreateMemory(
                uv, 
                Thread.CurrentThread.ManagedThreadId,
                UnsafeNativeMethods.uv_loop_size());

            UnsafeNativeMethods.uv_loop_init(this);
        }

        public void Run(int mode = 0)
        {
            Validate();
            Libuv.Check(UnsafeNativeMethods.uv_run(this, mode));
        }

        public void Stop()
        {
            Validate();
            UnsafeNativeMethods.uv_stop(this);
        }

        unsafe protected override bool ReleaseHandle()
        {
            var memory = this.handle;
            if (memory != IntPtr.Zero)
            {
                // loop_close clears the gcHandlePtr
                var gcHandlePtr = *(IntPtr*)memory;

                UnsafeNativeMethods.uv_loop_close(this);
                handle = IntPtr.Zero;

                DestroyMemory(memory, gcHandlePtr);
            }
            return true;
        }
    }
}
