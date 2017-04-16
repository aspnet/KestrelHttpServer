// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking
{
    public class UvRequest : UvMemory
    {
        protected UvRequest(ILibuvTrace logger) : base (logger, GCHandleType.Normal)
        {
        }

        protected override bool ReleaseHandle()
        {
            DestroyMemory(handle);
            handle = IntPtr.Zero;
            return true;
        }
    }
}

