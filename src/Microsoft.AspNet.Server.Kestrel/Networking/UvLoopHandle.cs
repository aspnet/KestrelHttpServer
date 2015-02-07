// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class UvLoopHandle : SafeHandle
    {
        private Libuv _uv;
        private int _threadId;

        public UvLoopHandle() : base(IntPtr.Zero, true) { }

        internal IntPtr InternalGetHandle() => handle;
        public override bool IsInvalid => handle == IntPtr.Zero;
        public Libuv Libuv => _uv;
        public int ThreadId => _threadId;

        public void Validate(bool closed = false)
        {
            Trace.Assert(closed || !IsClosed, "Handle is closed");
            Trace.Assert(!IsInvalid, "Handle is invalid");
            Trace.Assert(_threadId == Thread.CurrentThread.ManagedThreadId, "ThreadId is incorrect");
        }

        public void Init(Libuv uv)
        {
            _uv = uv;
            _threadId = Thread.CurrentThread.ManagedThreadId;
            handle = Marshal.AllocCoTaskMem(UnsafeNativeMethods.uv_loop_size());
            Libuv.Check(UnsafeNativeMethods.uv_loop_init(this));
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

        protected override bool ReleaseHandle()
        {
            Validate(closed: true);
            Libuv.Check(UnsafeNativeMethods.uv_loop_close(InternalGetHandle()));
            Marshal.FreeCoTaskMem(handle);
            return true;
        }
    }
}
