// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public static class KnownStrings
    {
        // Use readonly static rather than const so string compares can compare the pointer rather than the data when equal
        public static readonly string HttpConnectMethod = "CONNECT";
        public static readonly string HttpDeleteMethod = "DELETE";
        public static readonly string HttpGetMethod = "GET";
        public static readonly string HttpHeadMethod = "HEAD";
        public static readonly string HttpPatchMethod = "PATCH";
        public static readonly string HttpPostMethod = "POST";
        public static readonly string HttpPutMethod = "PUT";
        public static readonly string HttpOptionsMethod = "OPTIONS";
        public static readonly string HttpTraceMethod = "TRACE";

        public static readonly string Http10Version = "HTTP/1.0";
        public static readonly string Http11Version = "HTTP/1.1";
    }
}
