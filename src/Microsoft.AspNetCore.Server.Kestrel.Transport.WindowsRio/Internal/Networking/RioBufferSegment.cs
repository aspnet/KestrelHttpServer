// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RioBufferSegment
    {
        public RioBufferSegment(RioRegisteredBuffer bufferId, uint offset, uint length)
        {
            BufferId = bufferId;
            Offset = offset;
            Length = length;
        }

        RioRegisteredBuffer BufferId;
        public readonly uint Offset;
        public uint Length;
    }
}
