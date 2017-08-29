// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2OutputProducer : IHttpOutputProducer
    {
        private static readonly ArraySegment<byte> _emptyData = new ArraySegment<byte>(new byte[0]);

        private readonly int _streamId;
        private readonly IHttp2FrameWriter _frameWriter;

        public Http2OutputProducer(int streamId, IHttp2FrameWriter frameWriter)
        {
            _streamId = streamId;
            _frameWriter = frameWriter;
        }

        public void Dispose()
        {
        }

        public void Abort(Exception error)
        {
            // TODO: RST_STREAM?
        }

        public Task FlushAsync(CancellationToken cancellationToken) => _frameWriter.FlushAsync(cancellationToken);

        public Task Write100ContinueAsync(CancellationToken cancellationToken) => _frameWriter.Write100ContinueAsync(_streamId);

        public Task WriteDataAsync(ArraySegment<byte> data, bool chunk, CancellationToken cancellationToken)
        {
            if (chunk)
            {
                throw new ArgumentException("Chunked transfer coding is not supported in HTTP/2", nameof(chunk));
            }

            return _frameWriter.WriteDataAsync(_streamId, data, cancellationToken);
        }

        public Task WriteStreamSuffixAsync(CancellationToken cancellationToken)
        {
            return _frameWriter.WriteDataAsync(_streamId, _emptyData, endStream: true, cancellationToken: cancellationToken);
        }

        public void WriteResponseHeaders(int statusCode, string ReasonPhrase, FrameResponseHeaders responseHeaders)
        {
            _frameWriter.WriteHeaders(_streamId, statusCode, responseHeaders);
        }
    }
}
