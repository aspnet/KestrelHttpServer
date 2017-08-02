// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public interface IHttp2FrameWriter
    {
        void Abort(Exception error);
        Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken));
        Task Write100ContinueAsync(int streamId, CancellationToken cancellationToken = default(CancellationToken));
        Task WriteHeadersAsync(int streamId, int statusCode, IHeaderDictionary headers, CancellationToken cancellationToken = default(CancellationToken));
        Task WriteDataAsync(int streamId, Span<byte> data, CancellationToken cancellationToken = default(CancellationToken));
        Task WriteDataAsync(int streamId, Span<byte> data, bool endStream, CancellationToken cancellationToken = default(CancellationToken));
        Task WriteSettingsAckAsync(CancellationToken cancellationToken = default(CancellationToken));
        Task WritePingAsync(Http2PingFrameFlags flags, Span<byte> payload, CancellationToken cancellationToken = default(CancellationToken));
        Task WriteGoAwayAsync(int lastStreamId, Http2ErrorCode errorCode, CancellationToken cancellationToken = default(CancellationToken));
    }
}
