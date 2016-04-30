// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    public interface IFrameContext : IConnectionContext
    {
        IFrameControl FrameControl { get; }
    }
}
