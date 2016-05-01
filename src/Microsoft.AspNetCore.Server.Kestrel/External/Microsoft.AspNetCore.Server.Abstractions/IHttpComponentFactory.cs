// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Http;
using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;

namespace Microsoft.AspNetCore.Server.Abstractions
{
    public interface IHttpComponentFactory
    {
        ServerOptions ServerOptions { get; set; }

        Streams CreateStreams(IFrameContext owner);

        void DisposeStreams(Streams streams);

        Headers CreateHeaders(DateHeaderValueManager dateValueManager);

        void DisposeHeaders(Headers headers);
    }
}
