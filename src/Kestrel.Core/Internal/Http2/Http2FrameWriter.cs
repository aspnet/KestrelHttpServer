// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2FrameWriter : IHttp2FrameWriter
    {
        private static readonly ArraySegment<byte> _emptyData = new ArraySegment<byte>(new byte[0]);

        private readonly Http2Frame _outgoingFrame = new Http2Frame();
        private readonly SemaphoreSlim _outputSem = new SemaphoreSlim(1);
        private readonly HPackEncoder _hpackEncoder = new HPackEncoder();
        private readonly IPipe _output;
        private readonly ILogger _logger;

        public Http2FrameWriter(IPipe output, ILogger logger)
        {
            _output = output;
            _logger = logger;
        }

        public ILogger Log => _logger;

        public void Abort(Exception ex)
        {
            _output.Reader.CancelPendingRead();
            _output.Writer.Complete(ex);
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return WriteAsync(_emptyData);
        }

        public Task Write100ContinueAsync(int streamId)
        {
            return Task.CompletedTask;
        }

        public async Task WriteHeadersAsync(int streamId, int statusCode, IHeaderDictionary headers)
        {
            await _outputSem.WaitAsync();

            try
            {
                _outgoingFrame.PrepareHeaders(Http2HeadersFrameFlags.END_HEADERS, streamId);

                var done = _hpackEncoder.BeginEncode(statusCode, headers, _outgoingFrame.Payload, out var payloadLength);
                _outgoingFrame.Length = payloadLength;
                await WriteAsync(_outgoingFrame.Raw);

                if (!done)
                {
                    // TODO: send CONTINUATION frames
                    throw new NotSupportedException();
                }
            }
            finally
            {
                _outputSem.Release();
            }
        }

        public Task WriteDataAsync(int streamId, Span<byte> data, CancellationToken cancellationToken)
            => WriteDataAsync(streamId, data, endStream: false, cancellationToken: cancellationToken);

        public async Task WriteDataAsync(int streamId, Span<byte> data, bool endStream, CancellationToken cancellationToken)
        {
            await _outputSem.WaitAsync();

            try
            {
                _outgoingFrame.PrepareData(streamId);

                while (data.Length > _outgoingFrame.Length)
                {
                    data.Slice(0, _outgoingFrame.Length).CopyTo(_outgoingFrame.Payload);
                    data = data.Slice(_outgoingFrame.Length);

                    await WriteAsync(_outgoingFrame.Raw, cancellationToken);
                }

                _outgoingFrame.Length = data.Length;

                if (endStream)
                {
                    _outgoingFrame.DataFlags = Http2DataFrameFlags.END_STREAM;
                }

                data.CopyTo(_outgoingFrame.Payload);

                await WriteAsync(_outgoingFrame.Raw, cancellationToken);
            }
            finally
            {
                _outputSem.Release();
            }
        }

        public async Task WriteSettingsAckAsync()
        {
            await _outputSem.WaitAsync();

            try
            {
                _outgoingFrame.PrepareSettings(Http2SettingsFrameFlags.ACK);
                await WriteAsync(_outgoingFrame.Raw);
            }
            finally
            {
                _outputSem.Release();
            }
        }

        public async Task WritePingAsync(Http2PingFrameFlags flags, Span<byte> payload)
        {
            await _outputSem.WaitAsync();

            try
            {
                _outgoingFrame.PreparePing(Http2PingFrameFlags.ACK);
                payload.CopyTo(_outgoingFrame.Payload);
                await WriteAsync(_outgoingFrame.Raw);
            }
            finally
            {
                _outputSem.Release();
            }
        }

        public async Task WriteGoAwayAsync(int lastStreamId, Http2ErrorCode errorCode)
        {
            await _outputSem.WaitAsync();

            try
            {
                _outgoingFrame.PrepareGoAway(lastStreamId, errorCode);
                await WriteAsync(_outgoingFrame.Raw);
            }
            finally
            {
                _outputSem.Release();
            }
        }

        private async Task WriteAsync(Span<byte> data, CancellationToken cancellationToken = default(CancellationToken))
        {
            var writeableBuffer = _output.Writer.Alloc(1);
            writeableBuffer.Write(data);
            await writeableBuffer.FlushAsync(cancellationToken);
        }
    }
}
