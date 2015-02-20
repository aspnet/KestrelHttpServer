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

        private GCHandle _listenVitality;
        private Action<int, Exception> _listenCallback;

        public UvTcpListenHandle(UvLoopHandle loop)
            : base(loop)
        {
            _uv_connection_cb = UvConnectionCb;
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

        public void Listen(int backlog, Action<int, Exception> callback)
        {
            if (_listenVitality.IsAllocated)
            {
                throw new InvalidOperationException("TODO: Listen may not be called more than once");
            }
            try
            {
                _listenCallback = callback;
                _listenVitality = GCHandle.Alloc(this, GCHandleType.Normal);
                Validate();
                Libuv.ThrowOnError(UnsafeNativeMethods.uv_listen(Handle, 10, _uv_connection_cb));
            }
            catch
            {
                _listenCallback = null;
                if (_listenVitality.IsAllocated)
                {
                    _listenVitality.Free();
                }
                throw;
            }
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
            if (_listenVitality.IsAllocated)
            {
                _listenVitality.Free();
            }
            base.Dispose(disposing);
        }
    }
}
