using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public abstract class UvReq : SafeHandle
    {
        private readonly int _threadId;

        public UvReq(int threadId, int size)
            : base(IntPtr.Zero, true)
        {
            _threadId = threadId;

            handle = Marshal.AllocCoTaskMem(size);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        public void Validate(bool closed = false)
        {
            Trace.Assert(closed || !IsClosed, "Handle is closed");
            Trace.Assert(!IsInvalid, "Handle is invalid");
            Trace.Assert(_threadId == Thread.CurrentThread.ManagedThreadId, "ThreadId is incorrect");
        }

        protected override bool ReleaseHandle()
        {
            Marshal.FreeCoTaskMem(handle);
            handle = IntPtr.Zero;
            return true;
        }
    }
}