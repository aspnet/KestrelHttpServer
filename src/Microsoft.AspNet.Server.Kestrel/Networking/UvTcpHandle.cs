// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class UvTcpHandle : UvStreamHandle
    {
        public void Init(UvLoopHandle loop)
        {
            CreateMemory(
                loop.ThreadId,
                UnsafeNativeMethods.uv_handle_size(HandleType.TCP));

            loop.Validate();
            Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_tcp_init(loop, this));
        }

        public void Init(UvLoopHandle loop, Action<Action<IntPtr>, IntPtr> queueCloseHandle)
        {
            CreateHandle(
                loop.ThreadId,
                UnsafeNativeMethods.uv_handle_size(HandleType.TCP),
                queueCloseHandle);

            loop.Validate();
            Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_tcp_init(loop, this));
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
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_tcp_bind(this, ref addr, 0));
        }
    }
}
