// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace PlatformBenchmarks
{
    public interface IParsingDevirtualizer<TConnection, TParsingDevirtualizer> 
        : IHttpRequestLineHandler, IHttpHeadersHandler
        where TConnection : HttpConnection<TConnection, TParsingDevirtualizer>, new()
        where TParsingDevirtualizer : struct, IParsingDevirtualizer<TConnection, TParsingDevirtualizer>
    {
        TConnection Connection { get; set; }
    }
}
