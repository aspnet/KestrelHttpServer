// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public partial class Http2Frame
    {
        public Http2HeadersFrameFlags HeadersFlags
        {
            get => (Http2HeadersFrameFlags)Flags;
            set => Flags = (byte)value;
        }

        public bool Padded => (HeadersFlags & Http2HeadersFrameFlags.PADDED) == Http2HeadersFrameFlags.PADDED;

        public int PadLength => Padded ? Payload[0] : 0;

        public bool Priority => (HeadersFlags & Http2HeadersFrameFlags.PRIORITY) == Http2HeadersFrameFlags.PRIORITY;

        public Span<byte> HeaderBlockFragment => new Span<byte>(_data, HeaderBlockFragmentOffset, HeaderBlockFragmentLength);

        public int HeaderBlockFragmentOffset => PayloadOffset + (Padded ? 1 : 0) + (Priority ? 5 : 0);

        public int HeaderBlockFragmentLength => Length - ((Padded ? 1 : 0) + (Priority ? 5 : 0)) - PadLength;

        public void PrepareHeaders(Http2HeadersFrameFlags flags, int streamId)
        {
            Length = DefaultFrameSize - HeaderLength;
            Type = Http2FrameType.HEADERS;
            HeadersFlags = flags;
            StreamId = streamId;
        }
    }
}
