// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class Http2ConnectionTests : IDisposable
    {
        private static readonly string _largeHeaderA = new string('a', Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength - 8);

        private static readonly string _largeHeaderB = new string('b', Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength - 8);

        private static readonly IEnumerable<(string name, string value)> _postRequestHeaders = new (string, string)[]
        {
            (":method", "POST"),
            (":path", "/"),
            (":authority", "127.0.0.1"),
            (":scheme", "https"),
        };

        private static readonly IEnumerable<(string name, string value)> _browserRequestHeaders = new (string, string)[]
        {
            (":method", "GET"),
            (":path", "/"),
            (":authority", "127.0.0.1"),
            (":scheme", "https"),
            ("user-agent", "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:54.0) Gecko/20100101 Firefox/54.0"),
            ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
            ("accept-language", "en-US,en;q=0.5"),
            ("accept-encoding", "gzip, deflate, br"),
            ("upgrade-insecure-requests", "1"),
        };

        private static readonly IEnumerable<(string name, string value)> _oneContinuationRequestHeaders = new (string, string)[]
        {
            (":method", "GET"),
            (":path", "/"),
            (":authority", "127.0.0.1"),
            (":scheme", "https"),
            ("a", _largeHeaderA)
        };

        private static readonly IEnumerable<(string name, string value)> _twoContinuationsRequestHeaders = new (string, string)[]
        {
            (":method", "GET"),
            (":path", "/"),
            (":authority", "127.0.0.1"),
            (":scheme", "https"),
            ("a", _largeHeaderA),
            ("b", _largeHeaderB)
        };

        private static readonly byte[] _helloWorldBytes = Encoding.ASCII.GetBytes("hello, world");

        private readonly PipeFactory _pipeFactory = new PipeFactory();
        private readonly IPipe _inputPipe;
        private readonly IPipe _outputPipe;
        private readonly Http2ConnectionContext _connectionContext;
        private readonly Http2Connection _connection;
        private readonly HPackEncoder _hpackEncoder = new HPackEncoder();
        private readonly HPackDecoder _hpackDecoder = new HPackDecoder();
        private readonly Http2PeerSettings _clientSettings = new Http2PeerSettings();

        private readonly Dictionary<string, string> _receivedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly RequestDelegate _noopApplication;
        private readonly RequestDelegate _readHeadersApplication;
        private readonly RequestDelegate _largeHeadersApplication;
        private readonly RequestDelegate _waitForAbortApplication;
        private readonly RequestDelegate _waitForAbortFlushingApplication;

        private Task _connectionTask;

        public Http2ConnectionTests()
        {
            _inputPipe = _pipeFactory.Create();
            _outputPipe = _pipeFactory.Create();

            _noopApplication = context => Task.CompletedTask;

            _readHeadersApplication = context =>
            {
                foreach (var header in context.Request.Headers)
                {
                    _receivedHeaders[header.Key] = header.Value.ToString();
                }

                return Task.CompletedTask;
            };

            _largeHeadersApplication = context =>
            {
                context.Response.Headers["a"] = _largeHeaderA;
                context.Response.Headers["b"] = _largeHeaderB;

                return Task.CompletedTask;
            };

            _waitForAbortApplication = async context =>
            {
                var sem = new SemaphoreSlim(0);

                context.RequestAborted.Register(() =>
                {
                    sem.Release();
                });

                await sem.WaitAsync().TimeoutAfter(TimeSpan.FromSeconds(10));
            };

            _waitForAbortFlushingApplication = async context =>
            {
                var sem = new SemaphoreSlim(0);

                context.RequestAborted.Register(() =>
                {
                    sem.Release();
                });

                await sem.WaitAsync().TimeoutAfter(TimeSpan.FromSeconds(10));

                await context.Response.Body.FlushAsync();
            };

            _connectionContext = new Http2ConnectionContext
            {
                ServiceContext = new TestServiceContext(),
                ConnectionInformation = new MockConnectionInformation
                {
                    PipeFactory = _pipeFactory
                },
                Input = _inputPipe.Reader,
                Output = _outputPipe
            };
            _connection = new Http2Connection(_connectionContext);
        }

        public void Dispose()
        {
            _pipeFactory.Dispose();
        }

        [Fact]
        public async Task DATA_Received_StreamIdZero_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendDataAsync(0, new byte[0]);

            await WaitForConnectionErrorAsync(expectedLastStreamId: 0, expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR);
        }

        [Fact]
        public async Task DATA_Received_StreamIdle_StreamError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _postRequestHeaders);
            await SendDataAsync(1, _helloWorldBytes);

            await WaitForStreamErrorAsync(expectedStreamId: 1, expectedErrorCode: Http2ErrorCode.STREAM_CLOSED);

            await StopConnectionAsync(expectedLastStreamId: 1);
        }

        [Fact]
        public async Task DATA_Received_StreamHalfClosedRemote_StreamError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _postRequestHeaders);
            await SendDataAsync(1, _helloWorldBytes);

            // Headers were fully received, so the app was started
            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await WaitForStreamErrorAsync(expectedStreamId: 1, expectedErrorCode: Http2ErrorCode.STREAM_CLOSED);

            await StopConnectionAsync(expectedLastStreamId: 1);
        }

        [Fact]
        public async Task HEADERS_Received_Decoded()
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            Assert.True(await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _browserRequestHeaders));

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            VerifyDecodedRequestHeaders(_browserRequestHeaders);

            await StopConnectionAsync(expectedLastStreamId: 1);
        }

        [Fact]
        public async Task RST_STREAM_Received_StreamIdZero_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendRstStreamAsync(0);

            await WaitForConnectionErrorAsync(expectedLastStreamId: 0, expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR);
        }

        [Fact]
        public async Task RST_STREAM_Received_LengthLessThan4_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            // Initialize stream 1 so it's legal to send it RST_STREAM frames
            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _browserRequestHeaders);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            var rstStreamFrame = new Http2Frame();
            rstStreamFrame.PrepareRstStream(1, Http2ErrorCode.CANCEL);
            rstStreamFrame.Length = 3;
            await SendAsync(rstStreamFrame.Raw);

            await WaitForConnectionErrorAsync(expectedLastStreamId: 1, expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR);
        }

        [Fact]
        public async Task RST_STREAM_Received_LengthGreaterThan4_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            // Initialize stream 1 so it's legal to send it RST_STREAM frames
            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _browserRequestHeaders);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            var rstStreamFrame = new Http2Frame();
            rstStreamFrame.PrepareRstStream(1, Http2ErrorCode.CANCEL);
            rstStreamFrame.Length = 5;
            await SendAsync(rstStreamFrame.Raw);

            await WaitForConnectionErrorAsync(expectedLastStreamId: 1, expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR);
        }

        [Fact]
        public async Task RST_STREAM_Received_AbortsStream()
        {
            await InitializeConnectionAsync(_waitForAbortApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _browserRequestHeaders);
            await SendRstStreamAsync(1);

            // No data is received from the stream since it was aborted before writing anything

            await StopConnectionAsync(expectedLastStreamId: 1);
        }

        [Fact]
        public async Task RST_STREAM_Received_AbortsStream_FlushedDataIsSent()
        {
            await InitializeConnectionAsync(_waitForAbortFlushingApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _browserRequestHeaders);
            await SendRstStreamAsync(1);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            // No END_STREAM DATA frame is received since the stream was aborted

            await StopConnectionAsync(expectedLastStreamId: 1);
        }

        [Fact]
        public async Task SETTINGS_Received_Sends_ACK()
        {
            await InitializeConnectionAsync(_noopApplication);

            await StopConnectionAsync(expectedLastStreamId: 0);
        }

        [Fact]
        public async Task PING_Received_Sends_ACK()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendPingAsync();
            await ExpectAsync(Http2FrameType.PING,
                withLength: 8,
                withFlags: (byte)Http2PingFrameFlags.ACK,
                withStreamId: 0);

            await StopConnectionAsync(expectedLastStreamId: 0);
        }

        [Fact]
        public async Task GOAWAY_Received_ConnectionStops()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendGoAwayAsync();

            await WaitForConnectionStopAsync(expectedLastStreamId: 0);
        }

        [Fact]
        public async Task CONTINUATION_Received_Decoded()
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            Assert.False(await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE | Http2HeadersFrameFlags.END_STREAM, _twoContinuationsRequestHeaders));
            Assert.False(await SendContinuationAsync(1, Http2ContinuationFrameFlags.NONE));
            Assert.True(await SendContinuationAsync(1, Http2ContinuationFrameFlags.END_HEADERS));

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2HeadersFrameFlags.END_STREAM,
                withStreamId: 1);

            VerifyDecodedRequestHeaders(_twoContinuationsRequestHeaders);

            await StopConnectionAsync(expectedLastStreamId: 1);
        }

        [Fact]
        public async Task CONTINUATION_Received_StreamIdMismatch_ConnectionError()
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            Assert.False(await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _oneContinuationRequestHeaders));
            Assert.True(await SendContinuationAsync(3, Http2ContinuationFrameFlags.END_HEADERS));

            await WaitForConnectionErrorAsync(expectedLastStreamId: 1, expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR);
        }

        [Fact]
        public async Task CONTINUATION_Received_NoPriorHeaders_ConnectionError()
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            Assert.False(_hpackEncoder.BeginEncode(
                ToHeaderDictionary(_oneContinuationRequestHeaders),
                new byte[Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength],
                out _));
            Assert.True(await SendContinuationAsync(1, Http2ContinuationFrameFlags.END_HEADERS));

            await WaitForConnectionErrorAsync(expectedLastStreamId: 0, expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR);
        }

        [Fact]
        public async Task CONTINUATION_Sent_WhenHeadersLargerThanFrameLength()
        {
            await InitializeConnectionAsync(_largeHeadersApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _browserRequestHeaders);

            var headersFrame = await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.NONE,
                withStreamId: 1);
            var continuationFrame1 = await ExpectAsync(Http2FrameType.CONTINUATION,
                withLength: 16373,
                withFlags: (byte)Http2ContinuationFrameFlags.NONE,
                withStreamId: 1);
            var continuationFrame2 = await ExpectAsync(Http2FrameType.CONTINUATION,
                withLength: 16373,
                withFlags: (byte)Http2ContinuationFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(1);

            var responseHeaders = new FrameResponseHeaders();
            _hpackDecoder.Decode(headersFrame.HeaderBlockFragment, responseHeaders);
            _hpackDecoder.Decode(continuationFrame1.HeaderBlockFragment, responseHeaders);
            _hpackDecoder.Decode(continuationFrame2.HeaderBlockFragment, responseHeaders);

            var responseHeadersDictionary = (IDictionary<string, StringValues>)responseHeaders;
            Assert.Equal(5, responseHeadersDictionary.Count);
            Assert.Contains("date", responseHeadersDictionary.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", responseHeadersDictionary[":status"]);
            Assert.Equal("0", responseHeadersDictionary["content-length"]);
            Assert.Equal(_largeHeaderA, responseHeadersDictionary["a"]);
            Assert.Equal(_largeHeaderB, responseHeadersDictionary["b"]);
        }

        private async Task InitializeConnectionAsync(RequestDelegate application)
        {
            _connectionTask = _connection.ProcessAsync(new DummyApplication(application));

            await SendPreambleAsync().ConfigureAwait(false);
            await SendClientSettingsAsync();

            await ExpectAsync(Http2FrameType.SETTINGS,
                withLength: 0,
                withFlags: 0,
                withStreamId: 0);

            await ExpectAsync(Http2FrameType.SETTINGS,
                withLength: 0,
                withFlags: (byte)Http2SettingsFrameFlags.ACK,
                withStreamId: 0);
        }

        private async Task SendAsync(ArraySegment<byte> span)
        {
            var writableBuffer = _inputPipe.Writer.Alloc(1);
            writableBuffer.Write(span);
            await writableBuffer.FlushAsync();
        }

        private Task SendPreambleAsync() => SendAsync(Http2Connection.Preface);

        private Task SendClientSettingsAsync()
        {
            var frame = new Http2Frame();
            frame.PrepareSettings(Http2SettingsFrameFlags.NONE, _clientSettings);
            return SendAsync(frame.Raw);
        }

        private async Task<bool> SendHeadersAsync(int streamId, Http2HeadersFrameFlags flags, IEnumerable<(string, string)> headers)
        {
            var headerDictionary = ToHeaderDictionary(headers);
            var frame = new Http2Frame();

            frame.PrepareHeaders(flags, streamId);
            var done = _hpackEncoder.BeginEncode(headerDictionary, frame.Payload, out var length);
            frame.Length = length;

            await SendAsync(frame.Raw);

            return done;
        }

        private async Task<bool> SendContinuationAsync(int streamId, Http2ContinuationFrameFlags flags)
        {
            var frame = new Http2Frame();

            frame.PrepareContinuation(flags, streamId);
            var done =_hpackEncoder.Encode(frame.Payload, out var length);
            frame.Length = length;

            await SendAsync(frame.Raw);

            return done;
        }

        private Task SendDataAsync(int streamId, Span<byte> data)
        {
            var frame = new Http2Frame();

            frame.PrepareData(streamId);
            frame.Length = data.Length;
            data.CopyTo(frame.Payload);

            return SendAsync(frame.Raw);
        }

        private Task SendPingAsync()
        {
            var pingFrame = new Http2Frame();
            pingFrame.PreparePing(Http2PingFrameFlags.NONE);
            return SendAsync(pingFrame.Raw);
        }

        private Task SendRstStreamAsync(int streamId)
        {
            var rstStreamFrame = new Http2Frame();
            rstStreamFrame.PrepareRstStream(streamId, Http2ErrorCode.CANCEL);
            return SendAsync(rstStreamFrame.Raw);
        }

        private Task SendGoAwayAsync()
        {
            var frame = new Http2Frame();
            frame.PrepareGoAway(0, Http2ErrorCode.NO_ERROR);
            return SendAsync(frame.Raw);
        }

        private async Task<Http2Frame> ReceiveFrameAsync()
        {
            var frame = new Http2Frame();

            while (true)
            {
                var result = await _outputPipe.Reader.ReadAsync();
                var buffer = result.Buffer;
                var consumed = buffer.Start;
                var examined = buffer.End;

                try
                {
                    Assert.True(buffer.Length > 0);

                    if (Http2FrameReader.ReadFrame(buffer, frame, out consumed, out examined))
                    {
                        return frame;
                    }
                }
                finally
                {
                    _outputPipe.Reader.Advance(consumed, examined);
                }
            }
        }

        private async Task ReceiveSettingsAck()
        {
            var frame = await ReceiveFrameAsync();

            Assert.Equal(Http2FrameType.SETTINGS, frame.Type);
            Assert.Equal(Http2SettingsFrameFlags.ACK, frame.SettingsFlags);
        }

        private async Task<Http2Frame> ExpectAsync(Http2FrameType type, int withLength, byte withFlags, int withStreamId)
        {
            var frame = await ReceiveFrameAsync();

            Assert.Equal(type, frame.Type);
            Assert.Equal(withLength, frame.Length);
            Assert.Equal(withFlags, frame.Flags);
            Assert.Equal(withStreamId, frame.StreamId);

            return frame;
        }

        private Task StopConnectionAsync(int expectedLastStreamId)
        {
            _inputPipe.Writer.Complete();

            return WaitForConnectionStopAsync(expectedLastStreamId);
        }

        private async Task WaitForConnectionStopAsync(int expectedLastStreamId)
        {
            var goAwayFrame = await ExpectAsync(Http2FrameType.GOAWAY,
                withLength: 8,
                withFlags: 0,
                withStreamId: 0);
            Assert.Equal(expectedLastStreamId, goAwayFrame.GoAwayLastStreamId);
            Assert.Equal(Http2ErrorCode.NO_ERROR, goAwayFrame.GoAwayErrorCode);

            await _connectionTask;
        }

        private async Task WaitForConnectionErrorAsync(int expectedLastStreamId, Http2ErrorCode expectedErrorCode)
        {
            var goAwayFrame = await ExpectAsync(Http2FrameType.GOAWAY,
                withLength: 8,
                withFlags: 0,
                withStreamId: 0);
            Assert.Equal(expectedLastStreamId, goAwayFrame.GoAwayLastStreamId);
            Assert.Equal(expectedErrorCode, goAwayFrame.GoAwayErrorCode);

            await _connectionTask;
            _inputPipe.Writer.Complete();
        }

        private async Task WaitForStreamErrorAsync(int expectedStreamId, Http2ErrorCode expectedErrorCode)
        {
            var rstStreamFrame = await ExpectAsync(Http2FrameType.RST_STREAM,
                withLength: 4,
                withFlags: 0,
                withStreamId: expectedStreamId);
            Assert.Equal(expectedErrorCode, rstStreamFrame.RstStreamErrorCode);
        }

        private IHeaderDictionary ToHeaderDictionary(IEnumerable<(string name, string value)> headerData)
        {
            var headers = new FrameRequestHeaders();

            foreach (var header in headerData)
            {
                headers.Append(header.name, header.value);
            }

            return headers;
        }

        private void VerifyDecodedRequestHeaders(IEnumerable<(string name, string value)> expectedHeaders)
        {
            foreach (var header in expectedHeaders)
            {
                Assert.True(_receivedHeaders.TryGetValue(header.name, out var value), header.value);
                Assert.Equal(header.value, value, ignoreCase: true);
            }
        }
    }
}
