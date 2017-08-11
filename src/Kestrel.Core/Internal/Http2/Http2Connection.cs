// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2
{
    public class Http2Connection : ITimeoutControl, IHttp2StreamLifetimeHandler
    {
        public static byte[] Preface { get; } = new byte[]
        {
            0x50, 0x52, 0x49, 0x20, 0x2a, 0x20, 0x48, 0x54, 0x54, 0x50, 0x2f, 0x32, 0x2e, 0x30, 0x0d, 0x0a, 0x0d, 0x0a, 0x53, 0x4d, 0x0d, 0x0a, 0x0d, 0x0a
        };

        private readonly Http2ConnectionContext _context;
        private readonly Http2FrameWriter _frameWriter;
        private readonly HPackDecoder _hpackDecoder;

        private readonly Http2PeerSettings _serverSettings = new Http2PeerSettings();
        private readonly Http2PeerSettings _clientSettings = new Http2PeerSettings();

        private readonly Http2Frame _incomingFrame = new Http2Frame();
        private readonly Http2Frame _outgoingFrame = new Http2Frame();

        private Http2Stream _currentHeadersStream;
        private int _lastStreamId;

        private bool _stopping;

        private readonly Dictionary<int, Http2StreamInformation> _streams = new Dictionary<int, Http2StreamInformation>();

        public Http2Connection(Http2ConnectionContext context)
        {
            _context = context;
            _frameWriter = new Http2FrameWriter(context.Output);
            _hpackDecoder = new HPackDecoder();
        }

        public string ConnectionId => _context.ConnectionId;

        public IPipeReader Input => _context.Input;

        public IPipeWriter Output => _context.Output.Writer;

        public IKestrelTrace Log => _context.ServiceContext.Log;

        bool ITimeoutControl.TimedOut => throw new NotImplementedException();

        public void Abort(Exception ex)
        {
            _stopping = true;
            _frameWriter.Abort(ex);
        }

        public void Stop()
        {
            _stopping = true;
            Input.CancelPendingRead();
        }

        public async Task ProcessAsync<TContext>(IHttpApplication<TContext> application)
        {
            Exception error = null;
            var errorCode = Http2ErrorCode.NO_ERROR;

            try
            {
                while (!_stopping)
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

                if (!_stopping)
                {
                    await _frameWriter.WriteSettingsAsync(_serverSettings);
                }

                while (!_stopping)
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
                                Log.LogTrace($"Connection id {ConnectionId} received {_incomingFrame.Type} frame with flags 0x{_incomingFrame.Flags:x} and length {_incomingFrame.Length} for stream ID {_incomingFrame.StreamId}");
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
            catch (ConnectionAbortedException ex)
            {
                // TODO: log
                error = ex;
            }
            catch (Http2ConnectionErrorException ex)
            {
                // TODO: log
                error = ex;
                errorCode = ex.ErrorCode;
            }
            catch (Exception ex)
            {
                // TODO: log
                error = ex;
                errorCode = Http2ErrorCode.INTERNAL_ERROR;
            }
            finally
            {
                try
                {
                    foreach (var streamInfo in _streams.Values)
                    {
                        streamInfo.Stream?.Abort(error);
                    }

                    await _frameWriter.WriteGoAwayAsync(_lastStreamId, errorCode);
                }
                finally
                {
                    Input.Complete();
                    _frameWriter.Abort(ex: null);
                }
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
            switch (_incomingFrame.Type)
            {
                case Http2FrameType.DATA:
                    return ProcessDataFrameAsync();
                case Http2FrameType.HEADERS:
                    return ProcessHeadersFrameAsync<TContext>(application);
                case Http2FrameType.RST_STREAM:
                    return ProcessRstStreamFrameAsync();
                case Http2FrameType.SETTINGS:
                    return ProcessSettingsFrameAsync();
                case Http2FrameType.PING:
                    return ProcessPingFrameAsync();
                case Http2FrameType.GOAWAY:
                    return ProcessGoAwayFrameAsync();
                case Http2FrameType.CONTINUATION:
                    return ProcessContinuationFrameAsync<TContext>(application);
            }

            return Task.CompletedTask;
        }

        private Task ProcessDataFrameAsync()
        {
            Http2StreamInformation streamInfo = null;

            if (!_streams.TryGetValue(_incomingFrame.StreamId, out streamInfo))
            {
                throw new Http2ConnectionErrorException(Http2ErrorCode.PROTOCOL_ERROR);
            }

            if (streamInfo.State != Http2StreamState.Open && streamInfo.State != Http2StreamState.HalfClosedLocal)
            {
                if (streamInfo.State != Http2StreamState.Closed)
                {
                    // The stream may have already finished processing the request
                    streamInfo.Stream?.Abort(error: null);
                    streamInfo.State = Http2StreamState.Closed;
                }

                return _frameWriter.WriteRstStreamAsync(streamInfo.StreamId, Http2ErrorCode.STREAM_CLOSED);
            }

            var endStream = (_incomingFrame.DataFlags & Http2DataFrameFlags.END_STREAM) == Http2DataFrameFlags.END_STREAM;

            return streamInfo.Stream.MessageBody.OnDataAsync(_incomingFrame.DataPayload, endStream);
        }

        private Task ProcessHeadersFrameAsync<TContext>(IHttpApplication<TContext> application)
        {
            if (_currentHeadersStream != null)
            {
                throw new Http2ConnectionErrorException(Http2ErrorCode.PROTOCOL_ERROR);
            }

            var streamInfo = new Http2StreamInformation
            {
                StreamId = _incomingFrame.StreamId,
                State = Http2StreamState.Idle
            };
            _streams[_incomingFrame.StreamId] = streamInfo;
            _lastStreamId = _incomingFrame.StreamId;

            _currentHeadersStream = new Http2Stream<TContext>(
                application,
                ConnectionId,
                _incomingFrame.StreamId,
                _context.ServiceContext,
                _context.ConnectionInformation,
                timeoutControl: this,
                streamLifetimeHandler: this,
                frameWriter: _frameWriter);
            _currentHeadersStream.Reset();

            _hpackDecoder.Decode(_incomingFrame.HeaderBlockFragment, _currentHeadersStream.RequestHeaders);

            if ((_incomingFrame.HeadersFlags & Http2HeadersFrameFlags.END_HEADERS) == Http2HeadersFrameFlags.END_HEADERS)
            {
                streamInfo.State = Http2StreamState.Open;
                streamInfo.Stream = _currentHeadersStream;
                _ = _currentHeadersStream.ProcessRequestAsync();
                _currentHeadersStream = null;
            }

            if ((_incomingFrame.HeadersFlags & Http2HeadersFrameFlags.END_STREAM) == Http2HeadersFrameFlags.END_STREAM)
            {
                streamInfo.State = Http2StreamState.HalfClosedRemote;
            }

            return Task.CompletedTask;
        }

        private Task ProcessRstStreamFrameAsync()
        {
            if (_currentHeadersStream != null)
            {
                throw new Http2ConnectionErrorException(Http2ErrorCode.PROTOCOL_ERROR);
            }

            if (_incomingFrame.Length != 4)
            {
                throw new Http2ConnectionErrorException(Http2ErrorCode.PROTOCOL_ERROR);
            }

            Http2StreamInformation streamInfo = null;

            if (!_streams.TryGetValue(_incomingFrame.StreamId, out streamInfo) || streamInfo.State == Http2StreamState.Idle)
            {
                throw new Http2ConnectionErrorException(Http2ErrorCode.PROTOCOL_ERROR);
            }

            streamInfo.Stream?.Abort(error: null);

            return Task.CompletedTask;
        }

        private Task ProcessSettingsFrameAsync()
        {
            if (_currentHeadersStream != null)
            {
                throw new Http2ConnectionErrorException(Http2ErrorCode.PROTOCOL_ERROR);
            }

            if ((_incomingFrame.SettingsFlags & Http2SettingsFrameFlags.ACK) == Http2SettingsFrameFlags.ACK)
            {
                // TODO: keep track of this
                return Task.CompletedTask;
            }

            ReadSettings();

            return _frameWriter.WriteSettingsAckAsync();
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

        private Task ProcessPingFrameAsync()
        {
            if (_currentHeadersStream != null)
            {
                throw new Http2ConnectionErrorException(Http2ErrorCode.PROTOCOL_ERROR);
            }

            return _frameWriter.WritePingAsync(Http2PingFrameFlags.ACK, _incomingFrame.Payload);
        }

        private Task ProcessGoAwayFrameAsync()
        {
            Stop();
            return Task.CompletedTask;
        }

        private Task ProcessContinuationFrameAsync<TContext>(IHttpApplication<TContext> application)
        {
            if (_currentHeadersStream == null || _incomingFrame.StreamId != _currentHeadersStream.StreamId)
            {
                throw new Http2ConnectionErrorException(Http2ErrorCode.PROTOCOL_ERROR);
            }

            _hpackDecoder.Decode(_incomingFrame.HeaderBlockFragment, _currentHeadersStream.RequestHeaders);

            if ((_incomingFrame.ContinuationFlags & Http2ContinuationFrameFlags.END_HEADERS) == Http2ContinuationFrameFlags.END_HEADERS)
            {
                var streamInfo = _streams[_currentHeadersStream.StreamId];

                if (streamInfo.State == Http2StreamState.Idle)
                {
                    streamInfo.State = Http2StreamState.Open;
                }

                streamInfo.Stream = _currentHeadersStream;
                _ = _currentHeadersStream.ProcessRequestAsync();
                _currentHeadersStream = null;
            }

            return Task.CompletedTask;
        }

        void IHttp2StreamLifetimeHandler.OnStreamCompleted(int streamId)
        {
            if (_streams.TryGetValue(streamId, out var streamInfo))
            {
                streamInfo.State = Http2StreamState.Closed;
                streamInfo.Stream = null;
            }
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
