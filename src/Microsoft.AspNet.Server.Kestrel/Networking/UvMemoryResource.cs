using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public abstract class UvMemoryResource : SafeHandle
    {
        private readonly int _threadId;

        public UvMemoryResource(int threadId, int size)
            : base(IntPtr.Zero, true)
        {
            _threadId = threadId;

            SetHandle(Marshal.AllocCoTaskMem(size));
        }

        public override bool IsInvalid => handle == IntPtr.Zero;
        public int ThreadId => _threadId;

        public void Validate()
        {
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