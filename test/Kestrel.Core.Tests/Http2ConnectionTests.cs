// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class Http2ConnectionTests : IDisposable
    {
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
            ("upgrade-insecure-requests", "1")
        };

        private static readonly IEnumerable<(string name, string value)> _oneContinuationRequestHeaders = new (string, string)[]
        {
            (":method", "GET"),
            (":path", "/"),
            (":authority", "127.0.0.1"),
            (":scheme", "https"),
            // Should go into CONTINUATION frame
            ("a", new string('a', Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength - 8)),
        };

        private static readonly IEnumerable<(string name, string value)> _twoContinuationsRequestHeaders = new (string, string)[]
        {
            (":method", "GET"),
            (":path", "/"),
            (":authority", "127.0.0.1"),
            (":scheme", "https"),
            // Should go into first CONTINUATION frame
            ("a", new string('a', Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength - 8)),
            // Should go into second CONTINUATION frame
            ("b", new string('b', Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength - 8)),
        };

        private readonly PipeFactory _pipeFactory = new PipeFactory();
        private readonly IPipe _inputPipe;
        private readonly IPipe _outputPipe;
        private readonly Http2ConnectionContext _connectionContext;
        private readonly Http2Connection _connection;
        private readonly Task _connectionTask;
        private readonly HPackEncoder _hpackEncoder = new HPackEncoder();
        private readonly Http2PeerSettings _clientSettings = new Http2PeerSettings();

        private readonly Dictionary<string, string> _receivedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly RequestDelegate _requestDelegate;

        public Http2ConnectionTests()
        {
            _inputPipe = _pipeFactory.Create();
            _outputPipe = _pipeFactory.Create();

            _requestDelegate = (context) =>
            {
                foreach (var header in context.Request.Headers)
                {
                    _receivedHeaders[header.Key] = header.Value.ToString();
                }

                return Task.CompletedTask;
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
            _connectionTask = _connection.ProcessAsync(new DummyApplication(_requestDelegate));
        }

        public void Dispose()
        {
            _pipeFactory.Dispose();
        }

        [Fact]
        public async Task DATA_Received_HeadersNotDone_Error()
        {
            await InitializeConnectionAsync();

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, new[] { (":method", "GET") });
            await SendDataAsync(1, new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f });

            await WaitForConnectionErrorAsync(expectedLastStreamId: 1, expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR);
        }

        [Fact]
        public async Task HEADERS_Received_Decoded()
        {
            await InitializeConnectionAsync();

            Assert.True(await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _browserRequestHeaders));

            await WaitForResponseAsync(expectedStreamId: 1,
                expectedHeaderFrameLengths: new[] { 55 },
                expectedDataFrameLengths: new int[0]);
            VerifyDecodedRequestHeaders(_browserRequestHeaders);

            await StopConnectionAsync(expectedLastStreamId: 1);
        }

        [Fact]
        public async Task SETTINGS_Received_Sends_ACK()
        {
            await SendPreambleAsync();
            await SendClientSettingsAsync();

            await ExpectAsync(Http2FrameType.SETTINGS,
                withLength: 0,
                withFlags: (byte)Http2SettingsFrameFlags.ACK,
                withStreamId: 0);

            await StopConnectionAsync(expectedLastStreamId: 0);
        }

        [Fact]
        public async Task PING_Received_Sends_ACK()
        {
            await InitializeConnectionAsync();

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
            await InitializeConnectionAsync();

            await SendGoAwayAsync();

            await WaitForConnectionStopAsync(expectedLastStreamId: 0);
        }

        [Fact]
        public async Task CONTINUATION_Received_Decoded()
        {
            await InitializeConnectionAsync();

            Assert.False(await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE | Http2HeadersFrameFlags.END_STREAM, _twoContinuationsRequestHeaders));
            Assert.False(await SendContinuationAsync(1, Http2ContinuationFrameFlags.NONE));
            Assert.True(await SendContinuationAsync(1, Http2ContinuationFrameFlags.END_HEADERS));

            await WaitForResponseAsync(expectedStreamId: 1,
                expectedHeaderFrameLengths: new[] { 55 },
                expectedDataFrameLengths: new int[0]);
            VerifyDecodedRequestHeaders(_twoContinuationsRequestHeaders);

            await StopConnectionAsync(expectedLastStreamId: 1);
        }

        [Fact]
        public async Task CONTINUATION_Received_StreamIdMismatch_Error()
        {
            await InitializeConnectionAsync();

            Assert.False(await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _oneContinuationRequestHeaders));
            Assert.True(await SendContinuationAsync(3, Http2ContinuationFrameFlags.END_HEADERS));

            await WaitForConnectionErrorAsync(expectedLastStreamId: 1, expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR);
        }

        [Fact]
        public async Task CONTINUATION_Received_NoPriorHeaders_Error()
        {
            await InitializeConnectionAsync();

            Assert.False(_hpackEncoder.BeginEncode(
                ToHeaderDictionary(_oneContinuationRequestHeaders),
                new byte[Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength],
                out _));
            Assert.True(await SendContinuationAsync(1, Http2ContinuationFrameFlags.END_HEADERS));

            await WaitForConnectionErrorAsync(expectedLastStreamId: 0, expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR);
        }

        private async Task InitializeConnectionAsync()
        {
            await SendPreambleAsync();
            await SendClientSettingsAsync();

            await ExpectAsync(Http2FrameType.SETTINGS,
                withLength: 0,
                withFlags: (byte)Http2SettingsFrameFlags.ACK,
                withStreamId: 0);
        }

        private async Task SendAsync(Span<byte> span)
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

        private async Task WaitForResponseAsync(int expectedStreamId, int[] expectedHeaderFrameLengths, int[] expectedDataFrameLengths)
        {
            for (var i = 0; i < expectedHeaderFrameLengths.Length; i++)
            {
                await ExpectAsync(Http2FrameType.HEADERS,
                    withLength: expectedHeaderFrameLengths[i],
                    withFlags: (byte)(i == expectedHeaderFrameLengths.Length - 1
                        ? Http2HeadersFrameFlags.END_HEADERS
                        : Http2HeadersFrameFlags.NONE),
                    withStreamId: expectedStreamId);
            }

            for (var i = 0; i < expectedDataFrameLengths.Length; i++)
            {
                await ExpectAsync(Http2FrameType.DATA,
                    withLength: expectedDataFrameLengths[i],
                    withFlags: (byte)Http2DataFrameFlags.NONE,
                    withStreamId: expectedStreamId);
            }

            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: expectedStreamId);
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

            var exception = await Assert.ThrowsAsync<Http2ConnectionErrorException>(() => _connectionTask);
            Assert.Equal(expectedErrorCode, exception.ErrorCode);

            _inputPipe.Writer.Complete();
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
