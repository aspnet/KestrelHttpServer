using System;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public abstract class UvReq : UvMemory
    {
        public UvReq(int threadId, int size)
            : base(threadId, size)
        { }

        protected override bool ReleaseHandle()
        {
            DestroyMemory(handle);
            handle = IntPtr.Zero;
            return true;
        }
    }
}