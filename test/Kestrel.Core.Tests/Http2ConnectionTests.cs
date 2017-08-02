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
        private readonly PipeFactory _pipeFactory;
        private readonly IPipe _inputPipe;
        private readonly IPipe _outputPipe;
        private readonly Http2ConnectionContext _connectionContext;
        private readonly Http2PeerSettings _clientSettings;
        private readonly HPackEncoder _hpackEncoder;

        public Http2ConnectionTests()
        {
            _pipeFactory = new PipeFactory();
            _inputPipe = _pipeFactory.Create();
            _outputPipe = _pipeFactory.Create();
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
            _clientSettings = new Http2PeerSettings();
            _hpackEncoder = new HPackEncoder();
        }

        public void Dispose()
        {
            _pipeFactory.Dispose();
        }

        [Fact]
        public async Task SendsSettingsAck()
        {
            var connection = new Http2Connection(_connectionContext);
            var connectionTask = connection.ProcessAsync(new DummyApplication());

            await SendPreambleAsync();
            await SendClientSettingsAsync();
            await ReceiveSettingsAck();

            _inputPipe.Writer.Complete();

            connection.Stop();
            await connectionTask;
        }

        [Fact]
        public async Task SendsPingAck()
        {
            var connection = new Http2Connection(_connectionContext);
            var connectionTask = connection.ProcessAsync(new DummyApplication());

            await SendPreambleAsync();
            await SendClientSettingsAsync();
            await ReceiveSettingsAck();

            var pingFrame = new Http2Frame();
            pingFrame.PreparePing(Http2PingFrameFlags.NONE);
            await SendAsync(pingFrame.Raw);

            var responseFrame = await ReceiveFrameAsync();

            Assert.Equal(Http2FrameType.PING, responseFrame.Type);
            Assert.Equal(Http2PingFrameFlags.ACK, responseFrame.PingFlags);

            _inputPipe.Writer.Complete();

            connection.Stop();
            await connectionTask;
        }

        [Fact]
        public async Task DecodesHeaders()
        {
            var requestHeaders = new Dictionary<string, string>
            {
                [":method"] = "GET",
                [":path"] = "/",
                [":authority"] = "127.0.0.1:5000",
                [":scheme"] = "https",
                ["user-agent"] = "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:54.0) Gecko/20100101 Firefox/54.0",
                ["accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                ["accept-language"] = "en-US,en;q=0.5",
                ["accept-encoding"] = "gzip, deflate, br",
                ["upgrade-insecure-requests"] = "1",
            };

            var receivedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var headersRead = new SemaphoreSlim(0);

            var connection = new Http2Connection(_connectionContext);
            var connectionTask = connection.ProcessAsync(new DummyApplication(context =>
            {
                foreach (var header in context.Request.Headers)
                {
                    receivedHeaders[header.Key] = header.Value.ToString();
                }

                headersRead.Release();

                return Task.CompletedTask;
            }));

            await SendPreambleAsync();
            await SendClientSettingsAsync();
            await ReceiveSettingsAck();

            Assert.True(await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, MakeHeaders(requestHeaders)));

            await headersRead.WaitAsync().TimeoutAfter(TimeSpan.FromSeconds(10));

            foreach (var header in requestHeaders)
            {
                Assert.True(receivedHeaders.TryGetValue(header.Key, out var value), header.Key);
                Assert.Equal(header.Value, value, ignoreCase: true);
            }

            _inputPipe.Writer.Complete();

            await connectionTask;
        }

        [Fact]
        public async Task DecodesHeadersWithContinuations()
        {
            var requestHeaders = new Dictionary<string, string>
            {
                [":method"] = "GET",
                [":path"] = "/",
                [":authority"] = "127.0.0.1:5000",
                [":scheme"] = "https",
                // Should go into first continuation
                ["a"] = new string('a', Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength - 8),
                // Should go into second continuation
                ["b"] = new string('b', Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength - 8),
            };

            var receivedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var headersRead = new SemaphoreSlim(0);

            var connection = new Http2Connection(_connectionContext);
            var connectionTask = connection.ProcessAsync(new DummyApplication(context =>
            {
                foreach (var header in context.Request.Headers)
                {
                    receivedHeaders[header.Key] = header.Value.ToString();
                }

                headersRead.Release();

                return Task.CompletedTask;
            }));

            await SendPreambleAsync();
            await SendClientSettingsAsync();
            await ReceiveSettingsAck();

            Assert.False(await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE | Http2HeadersFrameFlags.END_STREAM, MakeHeaders(requestHeaders)));
            Assert.False(await SendContinuationAsync(1, Http2ContinuationFrameFlags.NONE));
            Assert.True(await SendContinuationAsync(1, Http2ContinuationFrameFlags.END_HEADERS));

            await headersRead.WaitAsync().TimeoutAfter(TimeSpan.FromSeconds(10));

            foreach (var header in requestHeaders)
            {
                Assert.True(receivedHeaders.TryGetValue(header.Key, out var value), header.Key);
                Assert.Equal(header.Value, value, ignoreCase: true);
            }

            _inputPipe.Writer.Complete();

            await connectionTask;
        }

        [Fact]
        public async Task ConnectionStoppedOnGoAwayFrame()
        {
            var connection = new Http2Connection(_connectionContext);
            var connectionTask = connection.ProcessAsync(new DummyApplication());

            await SendPreambleAsync();
            await SendClientSettingsAsync();
            await ReceiveSettingsAck();

            await SendGoAwayAsync();

            await connectionTask;
        }

        [Fact]
        public async Task ConnectionErrorOnHeadersFramesInterleavedWithOtherFrames()
        {
            var connection = new Http2Connection(_connectionContext);
            var connectionTask = connection.ProcessAsync(new DummyApplication());

            await SendPreambleAsync();
            await SendClientSettingsAsync();
            await ReceiveSettingsAck();

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, MakeHeaders(new Dictionary<string, string> { [":method"] = "GET" }));
            await SendDataAsync(1, new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f });

            var responseFrame = await ReceiveFrameAsync();

            Assert.Equal(Http2FrameType.GOAWAY, responseFrame.Type);
            Assert.Equal(1, responseFrame.GoAwayLastStreamId);
            Assert.Equal(Http2ConnectionError.PROTOCOL_ERROR, responseFrame.GoAwayErrorCode);

            await Assert.ThrowsAsync<Http2ConnectionErrorException>(() => connectionTask);

            _inputPipe.Writer.Complete();
        }

        [Fact]
        public async Task ConnectionErrorOnHeadersFramesInterleavedWithOtherHeaders()
        {
            var connection = new Http2Connection(_connectionContext);
            var connectionTask = connection.ProcessAsync(new DummyApplication());

            await SendPreambleAsync();
            await SendClientSettingsAsync();
            await ReceiveSettingsAck();

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, MakeHeaders(new Dictionary<string, string> { [":method"] = "GET" }));
            await SendHeadersAsync(3, Http2HeadersFrameFlags.NONE, MakeHeaders(new Dictionary<string, string> { [":method"] = "POST" }));

            var responseFrame = await ReceiveFrameAsync();

            Assert.Equal(Http2FrameType.GOAWAY, responseFrame.Type);
            Assert.Equal(3, responseFrame.GoAwayLastStreamId);
            Assert.Equal(Http2ConnectionError.PROTOCOL_ERROR, responseFrame.GoAwayErrorCode);

            await Assert.ThrowsAsync<Http2ConnectionErrorException>(() => connectionTask);

            _inputPipe.Writer.Complete();
        }

        [Fact]
        public async Task ConnectionErrorOnHeadersFramesInterleavedWithOtherContinuation()
        {
            var connection = new Http2Connection(_connectionContext);
            var connectionTask = connection.ProcessAsync(new DummyApplication());

            await SendPreambleAsync();
            await SendClientSettingsAsync();
            await ReceiveSettingsAck();

            Assert.False(await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, MakeHeaders(new Dictionary<string, string>
            {
                [":method"] = "GET",
                [":path"] = "/",
                [":authority"] = "127.0.0.1:5000",
                [":scheme"] = "https",
                // Should go into continuation
                ["a"] = new string('a', Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength - 8),
            })));
            Assert.True(await SendContinuationAsync(3, Http2ContinuationFrameFlags.END_HEADERS));

            var responseFrame = await ReceiveFrameAsync();

            Assert.Equal(Http2FrameType.GOAWAY, responseFrame.Type);
            Assert.Equal(3, responseFrame.GoAwayLastStreamId);
            Assert.Equal(Http2ConnectionError.PROTOCOL_ERROR, responseFrame.GoAwayErrorCode);

            await Assert.ThrowsAsync<Http2ConnectionErrorException>(() => connectionTask);

            _inputPipe.Writer.Complete();
        }

        [Fact]
        public async Task ConnectionErrorOnContinuationFrameWithoutPriorHeadersFrame()
        {
            var connection = new Http2Connection(_connectionContext);
            var connectionTask = connection.ProcessAsync(new DummyApplication());

            await SendPreambleAsync();
            await SendClientSettingsAsync();
            await ReceiveSettingsAck();

            Assert.False(_hpackEncoder.BeginEncode(MakeHeaders(new Dictionary<string, string>
            {
                [":method"] = "GET",
                [":path"] = "/",
                [":authority"] = "127.0.0.1:5000",
                [":scheme"] = "https",
                // Should go into continuation
                ["a"] = new string('a', Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength - 8),
            }), new byte[Http2Frame.DefaultFrameSize - Http2Frame.HeaderLength], out var length));
            await SendContinuationAsync(1, Http2ContinuationFrameFlags.END_HEADERS);

            var responseFrame = await ReceiveFrameAsync();

            Assert.Equal(Http2FrameType.GOAWAY, responseFrame.Type);
            Assert.Equal(1, responseFrame.GoAwayLastStreamId);
            Assert.Equal(Http2ConnectionError.PROTOCOL_ERROR, responseFrame.GoAwayErrorCode);

            await Assert.ThrowsAsync<Http2ConnectionErrorException>(() => connectionTask);

            _inputPipe.Writer.Complete();
        }

        private IHeaderDictionary MakeHeaders(Dictionary<string, string> headerData)
        {
            var headers = new FrameRequestHeaders();

            foreach (var header in headerData)
            {
                headers.Append(header.Key, header.Value);
            }

            return headers;
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

        private async Task<bool> SendHeadersAsync(int streamId, Http2HeadersFrameFlags flags, IHeaderDictionary headers)
        {
            var frame = new Http2Frame();

            frame.PrepareHeaders(flags, streamId);
            var done = _hpackEncoder.BeginEncode(headers, frame.Payload, out var length);
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

        private Task SendGoAwayAsync()
        {
            var frame = new Http2Frame();
            frame.PrepareGoAway(0, Http2ConnectionError.NO_ERROR);
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
    }
}
