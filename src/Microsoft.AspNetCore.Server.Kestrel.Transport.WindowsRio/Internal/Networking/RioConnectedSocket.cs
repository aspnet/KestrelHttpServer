// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    public struct RioConnectedSocket : IDisposable
    {
        private IntPtr _handle;

        public bool IsInvalid => _handle == (IntPtr)(-1);
        public bool IsNull => _handle == IntPtr.Zero;

        public void Dispose()
        {
            RioFunctions.CloseSocket(this);
            _handle = IntPtr.Zero;
        }

        public static explicit operator IntPtr(RioConnectedSocket socket)
        {
            return socket._handle;
        }

        public static explicit operator RioConnectedSocket(IntPtr socket)
        {
            return new RioConnectedSocket() { _handle = socket };
        }
    }
}
