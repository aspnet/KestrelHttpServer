// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2Connection : IHttp2FrameWriter, ITimeoutControl, IHttp2StreamLifetimeHandler
    {
        public static byte[] Preface { get; } = new byte[]
        {
            0x50, 0x52, 0x49, 0x20, 0x2a, 0x20, 0x48, 0x54, 0x54, 0x50, 0x2f, 0x32, 0x2e, 0x30, 0x0d, 0x0a, 0x0d, 0x0a, 0x53, 0x4d, 0x0d, 0x0a, 0x0d, 0x0a
        };

        private readonly Http2ConnectionContext _context;
        private readonly HPackEncoder _hpackEncoder;
        private readonly HPackDecoder _hpackDecoder;
        private readonly SemaphoreSlim _outputSem = new SemaphoreSlim(1);

        private readonly Http2PeerSettings _serverSettings = new Http2PeerSettings();
        private readonly Http2PeerSettings _clientSettings = new Http2PeerSettings();

        private readonly Http2Frame _incomingFrame = new Http2Frame();
        private readonly Http2Frame _outgoingFrame = new Http2Frame();

        private Http2Stream _currentHeadersStream;

        private bool _requestProcessingStopping;

        private readonly Dictionary<int, Http2Stream> _streams = new Dictionary<int, Http2Stream>();

        public Http2Connection(Http2ConnectionContext context)
        {
            _context = context;

            _hpackEncoder = new HPackEncoder();
            _hpackDecoder = new HPackDecoder(context.ServiceContext.Log);
        }

        public string ConnectionId => _context.ConnectionId;

        public IPipeReader Input => _context.Input;

        public IPipeWriter Output => _context.Output.Writer;

        public IKestrelTrace Log => _context.ServiceContext.Log;

        bool ITimeoutControl.TimedOut => throw new NotImplementedException();

        public void Abort(Exception ex)
        {
            _requestProcessingStopping = true;

            _context.Output.Reader.CancelPendingRead();
            _context.Output.Writer.Complete(ex);
        }

        public void Stop()
        {
            _requestProcessingStopping = true;
            Input.CancelPendingRead();
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task Write100ContinueAsync(int streamId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task WriteHeadersAsync(int streamId, int statusCode, IHeaderDictionary headers, CancellationToken cancellationToken)
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
            catch (Exception ex)
            {
                Log.LogError(0, ex, "error");
                throw;
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

                    await WriteAsync(_outgoingFrame.Raw);
                }

                _outgoingFrame.Length = data.Length;

                if (endStream)
                {
                    _outgoingFrame.DataFlags = Http2DataFrameFlags.END_STREAM;
                }

                data.CopyTo(_outgoingFrame.Payload);

                await WriteAsync(_outgoingFrame.Raw);
            }
            finally
            {
                _outputSem.Release();
            }
        }

        public async Task WriteGoAwayAsync(int lastStreamId, Http2ConnectionError errorCode, CancellationToken cancellationToken)
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

        private async Task ConnectionErrorAsync(Http2ConnectionError errorCode, CancellationToken cancellationToken = default(CancellationToken))
        {
            await WriteGoAwayAsync(_incomingFrame.StreamId, Http2ConnectionError.PROTOCOL_ERROR, cancellationToken);
            throw new Http2ConnectionErrorException(errorCode);
        }

        public async Task ProcessAsync<TContext>(IHttpApplication<TContext> application)
        {
            try
            {
                while (!_requestProcessingStopping)
                {
                    var result = await Input.ReadAsync();
                    var readableBuffer = result.Buffer;
                    var consumed = readableBuffer.Start;
                    var examined = readableBuffer.End;

                    try
                    {
                        if (!readableBuffer.IsEmpty)
                        {
                            if (ParsePreface(readableBuffer, out consumed, out examined))
                            {
                                break;
                            }
                        }
                        else if (result.IsCompleted)
                        {
                            return;
                        }
                    }
                    finally
                    {
                        Input.Advance(consumed, examined);
                    }
                }

                while (!_requestProcessingStopping)
                {
                    var result = await Input.ReadAsync();
                    var readableBuffer = result.Buffer;
                    var consumed = readableBuffer.Start;
                    var examined = readableBuffer.End;

                    try
                    {
                        if (!readableBuffer.IsEmpty)
                        {
                            if (Http2FrameReader.ReadFrame(readableBuffer, _incomingFrame, out consumed, out examined))
                            {
                                Log.LogTrace($"Received {_incomingFrame.Type} frame with flags 0x{_incomingFrame.Flags:x} and length {_incomingFrame.Length} for stream ID {_incomingFrame.StreamId}");
                                await ProcessFrameAsync<TContext>(application);
                            }
                        }
                        else if (result.IsCompleted)
                        {
                            return;
                        }
                    }
                    finally
                    {
                        Input.Advance(consumed, examined);
                    }
                }
            }
            finally
            {
                Input.Complete();
                Output.Complete();
            }
        }

        private bool ParsePreface(ReadableBuffer readableBuffer, out ReadCursor consumed, out ReadCursor examined)
        {
            consumed = readableBuffer.Start;
            examined = readableBuffer.End;

            if (readableBuffer.Length < Preface.Length)
            {
                return false;
            }

            var span = readableBuffer.IsSingleSpan
                ? readableBuffer.First.Span
                : readableBuffer.ToSpan();

            for (var i = 0; i < Preface.Length; i++)
            {
                if (Preface[i] != span[i])
                {
                    throw new Exception("Invalid HTTP/2 connection preface.");
                }
            }

            consumed = examined = readableBuffer.Move(readableBuffer.Start, Preface.Length);
            return true;
        }

        private Task ProcessFrameAsync<TContext>(IHttpApplication<TContext> application)
        {
            // TODO: error if frame type not HEADERS or PRIORITY and stream not open
            // TODO: error if reading headers and not CONTINUATION frame with END_HEADERS flag is received
            // TODO: error if reading headers and frame type other than CONTINUATION is received

            switch (_incomingFrame.Type)
            {
                case Http2FrameType.DATA:
                    return ProcessDataFrameAsync();
                case Http2FrameType.HEADERS:
                    return ProcessHeadersFrameAsync<TContext>(application);
                case Http2FrameType.SETTINGS:
                    return ProcessSettingsFrameAsync();
                case Http2FrameType.PING:
                    return ProcessPingFrameAsync();
                case Http2FrameType.CONTINUATION:
                    return ProcessContinuationFrameAsync<TContext>(application);
            }

            return Task.CompletedTask;
        }

        private async Task ProcessDataFrameAsync()
        {
            if (_streams.TryGetValue(_incomingFrame.StreamId, out var stream))
            {
                var endStream = (_incomingFrame.DataFlags & Http2DataFrameFlags.END_STREAM) == Http2DataFrameFlags.END_STREAM;

                if ((_incomingFrame.DataFlags & Http2DataFrameFlags.PADDED) == Http2DataFrameFlags.PADDED)
                {
                    var padLength = _incomingFrame.Payload[0];
                    await stream.MessageBody.OnDataAsync(_incomingFrame.Payload.Slice(1, _incomingFrame.Length - padLength - 1), endStream);
                }
                else
                {
                    await stream.MessageBody.OnDataAsync(_incomingFrame.Payload, endStream);
                }
            }
            else
            {
                // TODO: error
            }
        }

        private Task ProcessHeadersFrameAsync<TContext>(IHttpApplication<TContext> application)
        {
            if (_currentHeadersStream != null)
            {
                return ConnectionErrorAsync(Http2ConnectionError.PROTOCOL_ERROR);
            }

            _currentHeadersStream = new Http2Stream<TContext>(
                application,
                ConnectionId,
                _incomingFrame.StreamId,
                _context.ServiceContext,
                _context.ConnectionInformation,
                timeoutControl: this,
                streamLifetimeHandler: this,
                frameWriter: this);
            _currentHeadersStream.Reset();

            _hpackDecoder.Decode(_incomingFrame.HeaderBlockFragment, _currentHeadersStream.RequestHeaders);

            if ((_incomingFrame.HeadersFlags & Http2HeadersFrameFlags.END_HEADERS) == Http2HeadersFrameFlags.END_HEADERS)
            {
                _streams[_incomingFrame.StreamId] = _currentHeadersStream;
                _ = _currentHeadersStream.ProcessRequestAsync();
                _currentHeadersStream = null;
            }

            return Task.CompletedTask;
        }

        private async Task ProcessSettingsFrameAsync()
        {
            ReadSettings();

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

        private void ReadSettings()
        {
            // TODO: error handling

            var settingsCount = _incomingFrame.Length / 6;

            for (var i = 0; i < settingsCount; i++)
            {
                var j = i * 6;
                var id = (Http2SettingsFrameParameter)((_incomingFrame.Payload[0] << 8) | _incomingFrame.Payload[1]);
                var value = (uint)((_incomingFrame.Payload[2] << 24)
                    | (_incomingFrame.Payload[3] << 16)
                    | (_incomingFrame.Payload[4] << 8)
                    | _incomingFrame.Payload[5]);

                switch (id)
                {
                    case Http2SettingsFrameParameter.SETTINGS_HEADER_TABLE_SIZE:
                        _clientSettings.HeaderTableSize = value;
                        break;
                    case Http2SettingsFrameParameter.SETTINGS_ENABLE_PUSH:
                        _clientSettings.EnablePush = value == 1;
                        break;
                    case Http2SettingsFrameParameter.SETTINGS_MAX_CONCURRENT_STREAMS:
                        _clientSettings.MaxConcurrentStreams = value;
                        break;
                    case Http2SettingsFrameParameter.SETTINGS_INITIAL_WINDOW_SIZE:
                        _clientSettings.InitialWindowSize = value;
                        break;
                    case Http2SettingsFrameParameter.SETTINGS_MAX_FRAME_SIZE:
                        _clientSettings.MaxFrameSize = value;
                        break;
                    case Http2SettingsFrameParameter.SETTINGS_MAX_HEADER_LIST_SIZE:
                        _clientSettings.MaxHeaderListSize = value;
                        break;
                    default:
                        break;
                }
            }
        }

        private async Task ProcessPingFrameAsync()
        {
            await _outputSem.WaitAsync();

            try
            {
                _outgoingFrame.PreparePing(Http2PingFrameFlags.ACK);
                _incomingFrame.Payload.CopyTo(_outgoingFrame.Payload);
                await WriteAsync(_outgoingFrame.Raw);
            }
            finally
            {
                _outputSem.Release();
            }
        }

        private Task ProcessContinuationFrameAsync<TContext>(IHttpApplication<TContext> application)
        {
            if (_currentHeadersStream == null ||  _incomingFrame.StreamId != _currentHeadersStream.StreamId)
            {
                return ConnectionErrorAsync(Http2ConnectionError.PROTOCOL_ERROR);
            }

            _hpackDecoder.Decode(_incomingFrame.HeaderBlockFragment, _currentHeadersStream.RequestHeaders);

            if ((_incomingFrame.ContinuationFlags & Http2ContinuationFrameFlags.END_HEADERS) == Http2ContinuationFrameFlags.END_HEADERS)
            {
                _streams[_incomingFrame.StreamId] = _currentHeadersStream;
                _ = _currentHeadersStream.ProcessRequestAsync();
                _currentHeadersStream = null;
            }

            return Task.CompletedTask;
        }

        private async Task WriteAsync(Span<byte> data)
        {
            var writeableBuffer = Output.Alloc(1);
            writeableBuffer.Write(data);
            await writeableBuffer.FlushAsync();
        }

        void IHttp2StreamLifetimeHandler.OnStreamCompleted(int streamId)
        {
            _streams.Remove(streamId);
        }

        void ITimeoutControl.SetTimeout(long ticks, TimeoutAction timeoutAction)
        {
        }

        void ITimeoutControl.ResetTimeout(long ticks, TimeoutAction timeoutAction)
        {
        }

        void ITimeoutControl.CancelTimeout()
        {
        }

        void ITimeoutControl.StartTimingReads()
        {
        }

        void ITimeoutControl.PauseTimingReads()
        {
        }

        void ITimeoutControl.ResumeTimingReads()
        {
        }

        void ITimeoutControl.StopTimingReads()
        {
        }

        void ITimeoutControl.BytesRead(long count)
        {
        }

        void ITimeoutControl.StartTimingWrite(long size)
        {
        }

        void ITimeoutControl.StopTimingWrite()
        {
        }
    }
}
