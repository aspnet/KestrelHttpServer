// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public interface IHttpRequestLineHandler
    {
        void OnRequestLine(HttpRequestLineParseInfo parseInfo, Span<byte> target, int queryLength, Span<byte> customMethod);
    }
}