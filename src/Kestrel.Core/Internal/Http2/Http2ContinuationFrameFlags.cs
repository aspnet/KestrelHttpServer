// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    [Flags]
    public enum Http2ContinuationFrameFlags : byte
    {
        NONE = 0x0,
        END_HEADERS = 0x4,
    }
}
