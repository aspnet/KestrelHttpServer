// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Server.Abstractions;

namespace Microsoft.AspNetCore.Server.Networking.Uv.Interop
{
    public class UvRequest : UvMemory
    {
        private GCHandle _pin;

        protected UvRequest(IConnectionTrace logger) : base (logger)
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

