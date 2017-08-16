// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public interface IHttp2FrameWriter
    {
        void Abort(Exception error);
        Task FlushAsync(CancellationToken cancellationToken);
        Task Write100ContinueAsync(int streamId);
        Task WriteHeadersAsync(int streamId, int statusCode, IHeaderDictionary headers);
        Task WriteDataAsync(int streamId, Span<byte> data, CancellationToken cancellationToken);
        Task WriteDataAsync(int streamId, Span<byte> data, bool endStream, CancellationToken cancellationToken);
        Task WriteRstStreamAsync(int streamId, Http2ErrorCode errorCode);
        Task WriteSettingsAckAsync();
        Task WritePingAsync(Http2PingFrameFlags flags, Span<byte> payload);
        Task WriteGoAwayAsync(int lastStreamId, Http2ErrorCode errorCode);
    }
}
