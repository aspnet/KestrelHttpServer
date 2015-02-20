// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Networking
{
    public class UvTcpListenHandle : UvTcpHandle
    {
        private readonly uv_connection_cb _uv_connection_cb;
        private readonly GCHandle _selfKeepAlive;
        private readonly Action<int, Exception> _listenCallback;

        public UvTcpListenHandle(
            UvLoopHandle loop,
            IPEndPoint endPoint,
            int backlog,
            Action<int, Exception> callback)
            : base(loop)
        {
            _uv_connection_cb = UvConnectionCb;
            _selfKeepAlive = GCHandle.Alloc(this, GCHandleType.Normal);

            Bind(endPoint);
            _listenCallback = callback;
            Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_listen(Handle, backlog, _uv_connection_cb));
        }

        private void Bind(IPEndPoint endpoint)
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

        private void UvConnectionCb(IntPtr handle, int status)
        {
            var error = Libuv.ExceptionForError(status);
            _listenCallback(status, error);
        }

        public void Accept(UvTcpStreamHandle stream)
        {
            Validate();
            stream.Validate();
            Libuv.ThrowOnError(UnsafeNativeMethods.uv_accept(Handle, stream.Handle));
        }

        protected override void Dispose(bool disposing)
        {
            _selfKeepAlive.Free();
            base.Dispose(disposing);
        }
    }
}
