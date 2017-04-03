using System;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Networking
{
    public class UvRequest : UvMemory
    {
        private GCHandle _pin;

        protected UvRequest(ITransportTrace logger) : base (logger)
        {
        }

        protected override bool ReleaseHandle()
        {
            DestroyMemory(handle);
            handle = IntPtr.Zero;
            return true;
        }

        public virtual void Pin()
        {
            _pin = GCHandle.Alloc(this, GCHandleType.Normal);
        }

        public virtual void Unpin()
        {
            _pin.Free();
        }
    }
}

