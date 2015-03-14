// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class UvTcpStreamHandle : UvTcpHandle
    {
        public UvTcpStreamHandle(UvLoopHandle loop, UvTcpListenHandle listenHandle)
            : base(loop)
        {
            listenHandle.Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_accept(listenHandle.Handle, Handle));
        }
    }
}
