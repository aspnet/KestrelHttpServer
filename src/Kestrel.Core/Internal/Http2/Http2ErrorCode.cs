// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.


namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public enum Http2ErrorCode : uint
    {
        NO_ERROR = 0x0,
        PROTOCOL_ERROR = 0x1,
        INTERNAL_ERROR = 0x2,
        FLOW_CONTROL_ERROR = 0x3,
        SETTINGS_TIMEOUT = 0x4,
        STREAM_CLOSED = 0x5,
        FRAME_SIZE_ERROR = 0x6,
        REFUSED_STREAM = 0x7,
        CANCEL = 0x8,
        COMPRESSION_ERROR = 0x9,
        CONNECT_ERROR = 0xa,
        ENHANCE_YOUR_CALM = 0xb,
        INADEQUATE_SECURITY = 0xc,
        HTTP_1_1_REQUIRED = 0xd,
    }
}
