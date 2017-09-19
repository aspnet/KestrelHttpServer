// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core
{
    [Flags]
    public enum EndPointProtocols
    {
        Http1 = 0x1,
        Http2 = 0x2
    }
}
