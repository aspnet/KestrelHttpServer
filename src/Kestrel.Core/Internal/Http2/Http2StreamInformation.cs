// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2StreamInformation
    {
        public int StreamId { get; set; }
        public Http2StreamState State { get; set; }
        public Http2Stream Stream { get; set; }
    }
}
