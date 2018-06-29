// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2FrameWriter
    {
        // Literal Header Field without Indexing - Indexed Name (Index 8 - :status)
        private static readonly byte[] _continueBytes = new byte[] { 0x08, 0x03, (byte)'1', (byte)'0', (byte)'0' };

        private readonly Http2Frame _outgoingFrame = new Http2Frame();
        private readonly object _writeLock = new object();
        private readonly HPackEncoder _hpackEncoder = new HPackEncoder();
        private readonly PipeWriter _outputWriter;
        private readonly PipeReader _outputReader;
        private readonly Http2FlowControl _connectionOutputFlowControl;
        private readonly SafePipeWriterFlusher _flusher;

        private bool _completed;

        public Http2FrameWriter(
            PipeWriter outputPipeWriter,
            PipeReader outputPipeReader,
            Http2FlowControl connectionOutputFlowControl,
            ITimeoutControl timeoutControl)
        {
            _outputWriter = outputPipeWriter;
            _outputReader = outputPipeReader;

            _connectionOutputFlowControl = connectionOutputFlowControl;
            _flusher = new SafePipeWriterFlusher(outputPipeWriter, timeoutControl);
        }

        public void Complete()
        {
            lock (_writeLock)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                _outputWriter.Complete();
            }
        }

        public void Abort(ConnectionAbortedException ex)
        {
            lock (_writeLock)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                _outputReader.CancelPendingRead();
                _outputWriter.Complete(ex);
            }
        }

        // The outputProducer allows a canceled flush to abort an individual stream.
        public Task FlushAsync()
        {
            lock (_writeLock)
            {
                if (_completed)
                {
                    return Task.CompletedTask;
                }

                return _flusher.FlushAsync();
            }
        }

        public Task Write100ContinueAsync(int streamId)
        {
            lock (_writeLock)
            {
                _outgoingFrame.PrepareHeaders(Http2HeadersFrameFlags.END_HEADERS, streamId);
                _outgoingFrame.Length = _continueBytes.Length;
                _continueBytes.CopyTo(_outgoingFrame.HeadersPayload);

                return WriteUnsynchronizedAsync(_outgoingFrame.Raw);
            }
        }

        public void WriteResponseHeaders(int streamId, int statusCode, IHeaderDictionary headers)
        {
            lock (_writeLock)
            {
                if (_completed)
                {
                    return;
                }

                _outgoingFrame.PrepareHeaders(Http2HeadersFrameFlags.NONE, streamId);

                var done = _hpackEncoder.BeginEncode(statusCode, EnumerateHeaders(headers), _outgoingFrame.Payload, out var payloadLength);
                _outgoingFrame.Length = payloadLength;

                if (done)
                {
                    _outgoingFrame.HeadersFlags = Http2HeadersFrameFlags.END_HEADERS;
                }

                _outputWriter.Write(_outgoingFrame.Raw);

                while (!done)
                {
                    _outgoingFrame.PrepareContinuation(Http2ContinuationFrameFlags.NONE, streamId);

                    done = _hpackEncoder.Encode(_outgoingFrame.Payload, out var length);
                    _outgoingFrame.Length = length;

                    if (done)
                    {
                        _outgoingFrame.ContinuationFlags = Http2ContinuationFrameFlags.END_HEADERS;
                    }

                    _outputWriter.Write(_outgoingFrame.Raw);
                }
            }
        }

        public Task WriteDataAsync(int streamId, Http2StreamFlowControl flowControl, ReadOnlySequence<byte> data, bool endStream)
        {
            // The Length property of a ReadOnlySequence can be expensive, so we cache the value.
            long dataLength = data.Length;

            lock (_writeLock)
            {
                // Zero-length data frames are allowed even if there is no space available in the flow control window.
                // https://httpwg.org/specs/rfc7540.html#rfc.section.6.9.1
                if (dataLength != 0 && dataLength > flowControl.Available)
                {
                    return WriteDataAsyncAwaited(streamId, flowControl, data, dataLength, endStream);
                }

                flowControl.Advance((int)dataLength);
                return WriteDataUnsynchronizedAsync(streamId, data, endStream);
            }
        }

        private Task WriteDataUnsynchronizedAsync(int streamId, ReadOnlySequence<byte> data, bool endStream)
        {
            if (_completed)
            {
                return Task.CompletedTask;
            }

            _outgoingFrame.PrepareData(streamId);

            var payload = _outgoingFrame.Payload;
            var unwrittenPayloadLength = 0;

            foreach (var buffer in data)
            {
                var current = buffer;

                while (current.Length > payload.Length)
                {
                    current.Span.Slice(0, payload.Length).CopyTo(payload);
                    current = current.Slice(payload.Length);

                    _outputWriter.Write(_outgoingFrame.Raw);
                    payload = _outgoingFrame.Payload;
                    unwrittenPayloadLength = 0;
                }

                if (current.Length > 0)
                {
                    current.Span.CopyTo(payload);
                    payload = payload.Slice(current.Length);
                    unwrittenPayloadLength += current.Length;
                }
            }

            if (endStream)
            {
                _outgoingFrame.DataFlags = Http2DataFrameFlags.END_STREAM;
            }

            _outgoingFrame.Length = unwrittenPayloadLength;
            _outputWriter.Write(_outgoingFrame.Raw);

            return _flusher.FlushAsync();
        }

        private async Task WriteDataAsyncAwaited(int streamId, Http2StreamFlowControl flowControl, ReadOnlySequence<byte> data, long dataLength, bool endStream)
        {
            while (dataLength > 0)
            {
                var writeTask = Task.CompletedTask;
                var availabilityTask = Task.CompletedTask;

                lock (_writeLock)
                {
                    var available = flowControl.Available;

                    if (available <= 0)
                    {
                        availabilityTask = flowControl.AvailabilityTask;
                    }
                    else if (dataLength > available)
                    {
                        writeTask = WriteDataUnsynchronizedAsync(streamId, data.Slice(0, available), endStream: false);
                        data = data.Slice(available);

                        flowControl.Advance(available);
                        availabilityTask = flowControl.AvailabilityTask;

                        dataLength -= available;
                    }
                    else
                    {
                        writeTask = WriteDataUnsynchronizedAsync(streamId, data, endStream);

                        flowControl.Advance((int)dataLength);

                        dataLength = 0;
                    }
                }

                await writeTask;
                await availabilityTask;
            }
        }

        public Task WriteRstStreamAsync(int streamId, Http2ErrorCode errorCode)
        {
            lock (_writeLock)
            {
                _outgoingFrame.PrepareRstStream(streamId, errorCode);
                return WriteUnsynchronizedAsync(_outgoingFrame.Raw);
            }
        }

        public Task WriteSettingsAsync(Http2PeerSettings settings)
        {
            lock (_writeLock)
            {
                // TODO: actually send settings
                _outgoingFrame.PrepareSettings(Http2SettingsFrameFlags.NONE);
                return WriteUnsynchronizedAsync(_outgoingFrame.Raw);
            }
        }

        public Task WriteSettingsAckAsync()
        {
            lock (_writeLock)
            {
                _outgoingFrame.PrepareSettings(Http2SettingsFrameFlags.ACK);
                return WriteUnsynchronizedAsync(_outgoingFrame.Raw);
            }
        }

        public Task WritePingAsync(Http2PingFrameFlags flags, ReadOnlySpan<byte> payload)
        {
            lock (_writeLock)
            {
                _outgoingFrame.PreparePing(Http2PingFrameFlags.ACK);
                payload.CopyTo(_outgoingFrame.Payload);
                return WriteUnsynchronizedAsync(_outgoingFrame.Raw);
            }
        }

        public Task WriteGoAwayAsync(int lastStreamId, Http2ErrorCode errorCode)
        {
            lock (_writeLock)
            {
                _outgoingFrame.PrepareGoAway(lastStreamId, errorCode);
                return WriteUnsynchronizedAsync(_outgoingFrame.Raw);
            }
        }

        public bool TryUpdateStreamWindow(Http2StreamFlowControl flowControl, int bytes)
        {
            lock (_writeLock)
            {
                return flowControl.TryUpdateWindow(bytes);
            }
        }

        public bool TryUpdateConnectionWindow(int bytes)
        {
            lock (_writeLock)
            {
                return _connectionOutputFlowControl.TryUpdateWindow(bytes);
            }
        }

        private Task WriteUnsynchronizedAsync(ReadOnlySpan<byte> data)
        {
            if (_completed)
            {
                return Task.CompletedTask;
            }

            _outputWriter.Write(data);
            return _flusher.FlushAsync();
        }

        private static IEnumerable<KeyValuePair<string, string>> EnumerateHeaders(IHeaderDictionary headers)
        {
            foreach (var header in headers)
            {
                foreach (var value in header.Value)
                {
                    yield return new KeyValuePair<string, string>(header.Key, value);
                }
            }
        }
    }
}
