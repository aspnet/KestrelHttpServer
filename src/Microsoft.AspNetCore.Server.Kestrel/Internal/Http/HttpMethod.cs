// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public enum HttpMethod
    {
        Get = 0,
        Put = 1,
        Delete = 2,
        Post = 3,
        Head = 4,
        Trace = 5,
        Patch = 6,
        Connect = 7,
        Options = 8,

        Custom = 9,
    }
}