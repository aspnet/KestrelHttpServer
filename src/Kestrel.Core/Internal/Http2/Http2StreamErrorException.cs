﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2StreamErrorException : Exception
    {
        public Http2StreamErrorException(int streamId, Http2ErrorCode errorCode)
            : base($"HTTP/2 stream ID {streamId} error: {errorCode}")
        {
            StreamId = streamId;
            ErrorCode = errorCode;
        }

        public int StreamId { get; }

        public Http2ErrorCode ErrorCode { get; }
    }
}
