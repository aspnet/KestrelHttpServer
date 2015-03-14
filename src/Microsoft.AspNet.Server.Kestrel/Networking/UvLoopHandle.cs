// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class UvLoopHandle : UvMemoryResource
    {
        public UvLoopHandle()
            : base(Thread.CurrentThread.ManagedThreadId, GetSize())
        {
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_loop_init(this));
        }

        private static int GetSize()
        {
            return UnsafeNativeMethods.uv_loop_size();
        }

        public void Run(int mode = 0)
        {
            Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_run(this, mode));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Libuv.ThrowOnError(UnsafeNativeMethods.uv_loop_close(this));

            base.Dispose(disposing);
        }
    }
}
