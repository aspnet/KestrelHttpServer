// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    public struct RioRegisteredBuffer : IDisposable
    {
        private IntPtr _handle;

        public bool IsNull => _handle == IntPtr.Zero;

        public static RioRegisteredBuffer Create(IntPtr dataBuffer, uint dataLength)
        {
            return RioFunctions.RegisterBuffer(dataBuffer, dataLength);
        }

        public void Dispose()
        {
            RioFunctions.DeregisterBuffer(this);
            _handle = IntPtr.Zero;
        }
    }
}
