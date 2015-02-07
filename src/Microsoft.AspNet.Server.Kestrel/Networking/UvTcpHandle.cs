// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
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
                loop.Libuv,
                loop.ThreadId,
                UnsafeNativeMethods.uv_handle_size(HandleType.TCP));

            loop.Validate();
            Validate();
            Libuv.Check(UnsafeNativeMethods.uv_tcp_init(loop, this));
        }

        public void Init(UvLoopHandle loop, Action<Action<IntPtr>, IntPtr> queueCloseHandle)
        {
            CreateHandle(
                loop.Libuv, 
                loop.ThreadId,
                UnsafeNativeMethods.uv_handle_size(HandleType.TCP),
                queueCloseHandle);

            loop.Validate();
            Validate();
            Libuv.Check(UnsafeNativeMethods.uv_tcp_init(loop, this));
        }

        public void Bind(IPEndPoint endpoint)
        {
            Sockaddr addr;
            var addressText = endpoint.Address.ToString();

            Exception error1;
            Libuv.Check(UnsafeNativeMethods.uv_ip4_addr(addressText, endpoint.Port, out addr), out error1);

            if (error1 != null)
            {
                Exception error2;
                Libuv.Check(UnsafeNativeMethods.uv_ip6_addr(addressText, endpoint.Port, out addr), out error2);
                if (error2 != null)
                {
                    throw error1;
                }
            }

            Validate();
            Libuv.Check(UnsafeNativeMethods.uv_tcp_bind(this, ref addr, 0));
        }
    }
}
