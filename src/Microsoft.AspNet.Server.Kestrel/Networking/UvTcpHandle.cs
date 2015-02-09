// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class UvTcpHandle : UvStreamHandle
    {
        public UvTcpHandle(UvLoopHandle loop)
            : base(loop.ThreadId, getSize())
        {
            loop.Validate();
            Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_tcp_init(loop, Handle));
        }

        private static int getSize()
        {
            return UnsafeNativeMethods.uv_handle_size(HandleType.TCP);
        }

        public void Bind(IPEndPoint endpoint)
        {
            Sockaddr addr;
            var addressText = endpoint.Address.ToString();

            var error1 = Libuv.ExceptionForError(
                UnsafeNativeMethods.uv_ip4_addr(addressText, endpoint.Port, out addr));

            if (error1 != null)
            {
                var error2 = Libuv.ExceptionForError(
                    UnsafeNativeMethods.uv_ip6_addr(addressText, endpoint.Port, out addr));
                if (error2 != null)
                {
                    throw error1;
                }
            }

            Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_tcp_bind(Handle, ref addr, 0));
        }
    }
}
