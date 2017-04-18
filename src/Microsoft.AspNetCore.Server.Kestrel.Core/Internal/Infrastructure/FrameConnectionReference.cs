// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public class FrameConnectionReference : IDisposable
    {
        private readonly GCHandle _gcHandle;

        public FrameConnectionReference(FrameConnection connection)
        {
            _gcHandle = GCHandle.Alloc(connection, GCHandleType.Weak);
            ConnectionId = connection.ConnectionId;
        }

        public string ConnectionId { get; }

        public FrameConnection Connection => (FrameConnection)_gcHandle.Target;

        public void Dispose()
        {
            _gcHandle.Free();
        }
    }
}
