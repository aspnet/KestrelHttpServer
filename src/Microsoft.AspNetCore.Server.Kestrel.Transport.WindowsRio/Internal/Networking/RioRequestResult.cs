// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RioRequestResult
    {
        public int Status;
        public uint BytesTransferred;
        public long ConnectionCorrelation;
        public long RequestCorrelation;
    }
}
