// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class Http2ConnectionTests : Http2TestBase
    {
        private static readonly IEnumerable<KeyValuePair<string, string>> _postRequestHeaders = new[]
        {
            new KeyValuePair<string, string>(HeaderNames.Method, "POST"),
            new KeyValuePair<string, string>(HeaderNames.Path, "/"),
            new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
            new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
        };

        private static readonly IEnumerable<KeyValuePair<string, string>> _expectContinueRequestHeaders = new[]
        {
            new KeyValuePair<string, string>(HeaderNames.Method, "POST"),
            new KeyValuePair<string, string>(HeaderNames.Path, "/"),
            new KeyValuePair<string, string>(HeaderNames.Authority, "127.0.0.1"),
            new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
            new KeyValuePair<string, string>("expect", "100-continue"),
        };

        private static readonly IEnumerable<KeyValuePair<string, string>> _requestTrailers = new[]
        {
            new KeyValuePair<string, string>("trailer-one", "1"),
            new KeyValuePair<string, string>("trailer-two", "2"),
        };

        private static readonly IEnumerable<KeyValuePair<string, string>> _oneContinuationRequestHeaders = new[]
        {
            new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
            new KeyValuePair<string, string>(HeaderNames.Path, "/"),
            new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
            new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
            new KeyValuePair<string, string>("a", _largeHeaderValue),
            new KeyValuePair<string, string>("b", _largeHeaderValue),
            new KeyValuePair<string, string>("c", _largeHeaderValue),
            new KeyValuePair<string, string>("d", _largeHeaderValue)
        };

        private static readonly IEnumerable<KeyValuePair<string, string>> _twoContinuationsRequestHeaders = new[]
        {
            new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
            new KeyValuePair<string, string>(HeaderNames.Path, "/"),
            new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
            new KeyValuePair<string, string>(HeaderNames.Authority, "localhost:80"),
            new KeyValuePair<string, string>("a", _largeHeaderValue),
            new KeyValuePair<string, string>("b", _largeHeaderValue),
            new KeyValuePair<string, string>("c", _largeHeaderValue),
            new KeyValuePair<string, string>("d", _largeHeaderValue),
            new KeyValuePair<string, string>("e", _largeHeaderValue),
            new KeyValuePair<string, string>("f", _largeHeaderValue),
            new KeyValuePair<string, string>("g", _largeHeaderValue),
            new KeyValuePair<string, string>("h", _largeHeaderValue)
        };

        private static readonly byte[] _helloBytes = Encoding.ASCII.GetBytes("hello");
        private static readonly byte[] _worldBytes = Encoding.ASCII.GetBytes("world");
        private static readonly byte[] _helloWorldBytes = Encoding.ASCII.GetBytes("hello, world");
        private static readonly byte[] _noData = new byte[0];
        private static readonly byte[] _maxData = Encoding.ASCII.GetBytes(new string('a', Http2Frame.MinAllowedMaxFrameSize));

        [Fact]
        public async Task Frame_Received_OverMaxSize_FrameError()
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            // Manually craft a frame where the size is too large. Our own frame class won't allow this.
            // See Http2Frame.Length
            var length = Http2Frame.MinAllowedMaxFrameSize + 1; // Too big
            var frame = new byte[9 + length];
            frame[0] = (byte)((length & 0x00ff0000) >> 16);
            frame[1] = (byte)((length & 0x0000ff00) >> 8);
            frame[2] = (byte)(length & 0x000000ff);
            await SendAsync(frame);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: true,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.FRAME_SIZE_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorFrameOverLimit(length, Http2Frame.MinAllowedMaxFrameSize));
        }

        [Fact]
        public async Task DATA_Received_ReadByStream()
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendDataAsync(1, _helloWorldBytes, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            var dataFrame = await ExpectAsync(Http2FrameType.DATA,
                withLength: 12,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);

            Assert.Equal(_helloWorldBytes, dataFrame.DataPayload);
        }

        [Fact]
        public async Task DATA_Received_MaxSize_ReadByStream()
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendDataAsync(1, _maxData, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            var dataFrame = await ExpectAsync(Http2FrameType.DATA,
                withLength: _maxData.Length,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);

            Assert.Equal(_maxData, dataFrame.DataPayload);
        }

        [Fact]
        public async Task DATA_Received_GreaterThanDefaultInitialWindowSize_ReadByStream()
        {
            // _maxData should be 1/4th of the default initial window size + 1.
            Assert.Equal(Http2PeerSettings.DefaultInitialWindowSize + 1, (uint)_maxData.Length * 4);

            // Double the client stream windows to 128KiB so no stream WINDOW_UPDATEs need to be sent.
            _clientSettings.InitialWindowSize = Http2PeerSettings.DefaultInitialWindowSize * 2;

            await InitializeConnectionAsync(_echoApplication);

            // Double the client connection window to 128KiB.
            await SendWindowUpdateAsync(0, (int)Http2PeerSettings.DefaultInitialWindowSize);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendDataAsync(1, _maxData, endStream: false);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            var dataFrame1 = await ExpectAsync(Http2FrameType.DATA,
                withLength: _maxData.Length,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            // Writing over half the initial window size induces both a connection-level and stream-level window update.
            await SendDataAsync(1, _maxData, endStream: false);

            var streamWindowUpdateFrame1 = await ExpectAsync(Http2FrameType.WINDOW_UPDATE,
                withLength: 4,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            var connectionWindowUpdateFrame1 = await ExpectAsync(Http2FrameType.WINDOW_UPDATE,
                withLength: 4,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 0);

            var dataFrame2 = await ExpectAsync(Http2FrameType.DATA,
                withLength: _maxData.Length,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            await SendDataAsync(1, _maxData, endStream: false);

            var dataFrame3 = await ExpectAsync(Http2FrameType.DATA,
                withLength: _maxData.Length,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            await SendDataAsync(1, _maxData, endStream: true);

            var connectionWindowUpdateFrame2 = await ExpectAsync(Http2FrameType.WINDOW_UPDATE,
                withLength: 4,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 0);

            var dataFrame4 = await ExpectAsync(Http2FrameType.DATA,
                withLength: _maxData.Length,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);

            Assert.Equal(_maxData, dataFrame1.DataPayload);
            Assert.Equal(_maxData, dataFrame2.DataPayload);
            Assert.Equal(_maxData, dataFrame3.DataPayload);
            Assert.Equal(_maxData, dataFrame4.DataPayload);
            Assert.Equal(_maxData.Length * 2, streamWindowUpdateFrame1.WindowUpdateSizeIncrement);
            Assert.Equal(_maxData.Length * 2, connectionWindowUpdateFrame1.WindowUpdateSizeIncrement);
            Assert.Equal(_maxData.Length * 2, connectionWindowUpdateFrame2.WindowUpdateSizeIncrement);
        }

        [Fact]
        public async Task DATA_Received_Multiple_ReadByStream()
        {
            await InitializeConnectionAsync(_bufferingApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);

            for (var i = 0; i < _helloWorldBytes.Length; i++)
            {
                await SendDataAsync(1, new ArraySegment<byte>(_helloWorldBytes, i, 1), endStream: false);
            }

            await SendDataAsync(1, _noData, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            var dataFrame = await ExpectAsync(Http2FrameType.DATA,
                withLength: 12,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);

            Assert.Equal(_helloWorldBytes, dataFrame.DataPayload);
        }

        [Fact]
        public async Task DATA_Received_Multiplexed_ReadByStreams()
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await StartStreamAsync(3, _browserRequestHeaders, endStream: false);

            await SendDataAsync(1, _helloBytes, endStream: false);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            var stream1DataFrame1 = await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            await SendDataAsync(3, _helloBytes, endStream: false);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);
            var stream3DataFrame1 = await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 3);

            await SendDataAsync(3, _worldBytes, endStream: false);

            var stream3DataFrame2 = await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 3);

            await SendDataAsync(1, _worldBytes, endStream: false);

            var stream1DataFrame2 = await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            await SendDataAsync(1, _noData, endStream: true);

            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await SendDataAsync(3, _noData, endStream: true);

            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 3);

            await StopConnectionAsync(expectedLastStreamId: 3, ignoreNonGoAwayFrames: false);

            Assert.Equal(stream1DataFrame1.DataPayload, _helloBytes);
            Assert.Equal(stream1DataFrame2.DataPayload, _worldBytes);
            Assert.Equal(stream3DataFrame1.DataPayload, _helloBytes);
            Assert.Equal(stream3DataFrame2.DataPayload, _worldBytes);
        }

        [Fact]
        public async Task DATA_Received_Multiplexed_GreaterThanDefaultInitialWindowSize_ReadByStream()
        {
            // _maxData should be 1/4th of the default initial window size + 1.
            Assert.Equal(Http2PeerSettings.DefaultInitialWindowSize + 1, (uint)_maxData.Length * 4);

            // Double the client stream windows to 128KiB so no stream WINDOW_UPDATEs need to be sent.
            _clientSettings.InitialWindowSize = Http2PeerSettings.DefaultInitialWindowSize * 2;

            await InitializeConnectionAsync(_echoApplication);

            // Double the client connection window to 128KiB.
            await SendWindowUpdateAsync(0, (int)Http2PeerSettings.DefaultInitialWindowSize);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendDataAsync(1, _maxData, endStream: false);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            var dataFrame1 = await ExpectAsync(Http2FrameType.DATA,
                withLength: _maxData.Length,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            // Writing over half the initial window size induces both a connection-level and stream-level window update.
            await SendDataAsync(1, _maxData, endStream: false);

            var streamWindowUpdateFrame = await ExpectAsync(Http2FrameType.WINDOW_UPDATE,
                withLength: 4,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            var connectionWindowUpdateFrame1 = await ExpectAsync(Http2FrameType.WINDOW_UPDATE,
                withLength: 4,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 0);

            var dataFrame2 = await ExpectAsync(Http2FrameType.DATA,
                withLength: _maxData.Length,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            await SendDataAsync(1, _maxData, endStream: false);

            var dataFrame3 = await ExpectAsync(Http2FrameType.DATA,
                withLength: _maxData.Length,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            // Uploading data to a new stream induces a second connection-level but not stream-level window update.
            await StartStreamAsync(3, _browserRequestHeaders, endStream: false);
            await SendDataAsync(3, _maxData, endStream: true);

            var connectionWindowUpdateFrame2 = await ExpectAsync(Http2FrameType.WINDOW_UPDATE,
                withLength: 4,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 0);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);

            var dataFrame4 = await ExpectAsync(Http2FrameType.DATA,
                withLength: _maxData.Length,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 3);

            await SendDataAsync(1, _maxData, endStream: true);

            var dataFrame5 = await ExpectAsync(Http2FrameType.DATA,
                withLength: _maxData.Length,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 3, ignoreNonGoAwayFrames: false);

            Assert.Equal(_maxData, dataFrame1.DataPayload);
            Assert.Equal(_maxData, dataFrame2.DataPayload);
            Assert.Equal(_maxData, dataFrame3.DataPayload);
            Assert.Equal(_maxData.Length * 2, streamWindowUpdateFrame.WindowUpdateSizeIncrement);
            Assert.Equal(_maxData.Length * 2, connectionWindowUpdateFrame1.WindowUpdateSizeIncrement);

            Assert.Equal(_maxData, dataFrame4.DataPayload);
            Assert.Equal(_maxData.Length * 2, connectionWindowUpdateFrame2.WindowUpdateSizeIncrement);

            Assert.Equal(_maxData, dataFrame5.DataPayload);
        }

        [Fact]
        public async Task DATA_Received_Multiplexed_AppMustNotBlockOtherFrames()
        {
            var stream1Read = new ManualResetEvent(false);
            var stream1ReadFinished = new ManualResetEvent(false);
            var stream3Read = new ManualResetEvent(false);
            var stream3ReadFinished = new ManualResetEvent(false);
            await InitializeConnectionAsync(async context =>
            {
                var data = new byte[10];
                var read = await context.Request.Body.ReadAsync(new byte[10], 0, 10);
                if (context.Features.Get<IHttp2StreamIdFeature>().StreamId == 1)
                {
                    stream1Read.Set();
                    Assert.True(stream1ReadFinished.WaitOne(TimeSpan.FromSeconds(10)));
                }
                else
                {
                    stream3Read.Set();
                    Assert.True(stream3ReadFinished.WaitOne(TimeSpan.FromSeconds(10)));
                }
                await context.Response.Body.WriteAsync(data, 0, read);
            });

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await StartStreamAsync(3, _browserRequestHeaders, endStream: false);

            await SendDataAsync(1, _helloBytes, endStream: false);
            Assert.True(stream1Read.WaitOne(TimeSpan.FromSeconds(10)));

            await SendDataAsync(3, _helloBytes, endStream: false);
            Assert.True(stream3Read.WaitOne(TimeSpan.FromSeconds(10)));

            stream3ReadFinished.Set();

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 3);

            stream1ReadFinished.Set();

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 3, ignoreNonGoAwayFrames: false);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(255)]
        public async Task DATA_Received_WithPadding_ReadByStream(byte padLength)
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendDataWithPaddingAsync(1, _helloWorldBytes, padLength, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            var dataFrame = await ExpectAsync(Http2FrameType.DATA,
                withLength: 12,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);

            Assert.Equal(_helloWorldBytes, dataFrame.DataPayload);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(255)]
        public async Task DATA_Received_WithPadding_CountsTowardsInputFlowControl(byte padLength)
        {
            // _maxData should be 1/4th of the default initial window size + 1.
            Assert.Equal(Http2PeerSettings.DefaultInitialWindowSize + 1, (uint)_maxData.Length * 4);

            var maxDataMinusPadding = new ArraySegment<byte>(_maxData, 0, _maxData.Length - padLength - 1);

            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendDataWithPaddingAsync(1, maxDataMinusPadding, padLength, endStream: false);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            var dataFrame1 = await ExpectAsync(Http2FrameType.DATA,
                withLength: maxDataMinusPadding.Count,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            // Writing over half the initial window size induces both a connection-level and stream-level window update.
            await SendDataAsync(1, _maxData, endStream: true);

            var connectionWindowUpdateFrame = await ExpectAsync(Http2FrameType.WINDOW_UPDATE,
                withLength: 4,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 0);

            var dataFrame2 = await ExpectAsync(Http2FrameType.DATA,
                withLength: _maxData.Length,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);

            Assert.Equal(maxDataMinusPadding, dataFrame1.DataPayload);
            Assert.Equal(_maxData, dataFrame2.DataPayload);

            Assert.Equal(_maxData.Length * 2, connectionWindowUpdateFrame.WindowUpdateSizeIncrement);
        }

        [Fact]
        public async Task DATA_Received_ButNotConsumedByApp_CountsTowardsInputFlowControl()
        {
            // _maxData should be 1/4th of the default initial window size + 1.
            Assert.Equal(Http2PeerSettings.DefaultInitialWindowSize + 1, (uint)_maxData.Length * 4);

            await InitializeConnectionAsync(_noopApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendDataAsync(1, _maxData, endStream: false);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            // Writing over half the initial window size induces both a connection-level window update.
            await SendDataAsync(1, _maxData, endStream: true);

            var connectionWindowUpdateFrame = await ExpectAsync(Http2FrameType.WINDOW_UPDATE,
                withLength: 4,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 0);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);

            Assert.Equal(_maxData.Length * 2, connectionWindowUpdateFrame.WindowUpdateSizeIncrement);
        }

        [Fact]
        public async Task DATA_Received_StreamIdZero_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendDataAsync(0, _noData, endStream: false);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdZero(Http2FrameType.DATA));
        }

        [Fact]
        public async Task DATA_Received_StreamIdEven_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendDataAsync(2, _noData, endStream: false);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdEven(Http2FrameType.DATA, streamId: 2));
        }

        [Fact]
        public async Task DATA_Received_PaddingEqualToFramePayloadLength_ConnectionError()
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendInvalidDataFrameAsync(1, frameLength: 5, padLength: 5);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: true,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorPaddingTooLong(Http2FrameType.DATA));
        }

        [Fact]
        public async Task DATA_Received_PaddingGreaterThanFramePayloadLength_ConnectionError()
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendInvalidDataFrameAsync(1, frameLength: 5, padLength: 6);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: true,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorPaddingTooLong(Http2FrameType.DATA));
        }

        [Fact]
        public async Task DATA_Received_FrameLengthZeroPaddingZero_ConnectionError()
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendInvalidDataFrameAsync(1, frameLength: 0, padLength: 0);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: true,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorPaddingTooLong(Http2FrameType.DATA));
        }

        [Fact]
        public async Task DATA_Received_InterleavedWithHeaders_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _browserRequestHeaders);
            await SendDataAsync(1, _helloWorldBytes, endStream: true);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorHeadersInterleaved(Http2FrameType.DATA, streamId: 1, headersStreamId: 1));
        }

        [Fact]
        public async Task DATA_Received_StreamIdle_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendDataAsync(1, _helloWorldBytes, endStream: false);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdle(Http2FrameType.DATA, streamId: 1));
        }

        [Fact]
        public async Task DATA_Received_StreamHalfClosedRemote_ConnectionError()
        {
            // Use _waitForAbortApplication so we know the stream will still be active when we send the illegal DATA frame
            await InitializeConnectionAsync(_waitForAbortApplication);

            await StartStreamAsync(1, _postRequestHeaders, endStream: true);

            await SendDataAsync(1, _helloWorldBytes, endStream: false);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.STREAM_CLOSED,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamHalfClosedRemote(Http2FrameType.DATA, streamId: 1));
        }

        [Fact]
        public async Task DATA_Received_StreamClosed_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await StartStreamAsync(1, _postRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await SendDataAsync(1, _helloWorldBytes, endStream: false);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.STREAM_CLOSED,
                expectedErrorMessage: null);

            // There's a race where either of these messages could be logged, depending on if the stream cleanup has finished yet.
            var closedMessage = CoreStrings.FormatHttp2ErrorStreamClosed(Http2FrameType.DATA, streamId: 1);
            var halfClosedMessage = CoreStrings.FormatHttp2ErrorStreamHalfClosedRemote(Http2FrameType.DATA, streamId: 1);

            var message = Assert.Single(TestApplicationErrorLogger.Messages, m => m.Exception is Http2ConnectionErrorException);
            Assert.True(message.Exception.Message.IndexOf(closedMessage) >= 0
                || message.Exception.Message.IndexOf(halfClosedMessage) >= 0);
        }

        [Fact]
        public async Task DATA_Received_StreamClosedImplicitly_ConnectionError()
        {
            // http://httpwg.org/specs/rfc7540.html#rfc.section.5.1.1
            //
            // The first use of a new stream identifier implicitly closes all streams in the "idle" state that
            // might have been initiated by that peer with a lower-valued stream identifier. For example, if a
            // client sends a HEADERS frame on stream 7 without ever sending a frame on stream 5, then stream 5
            // transitions to the "closed" state when the first frame for stream 7 is sent or received.

            await InitializeConnectionAsync(_noopApplication);

            await StartStreamAsync(3, _browserRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 3);

            await SendDataAsync(1, _helloWorldBytes, endStream: true);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 3,
                expectedErrorCode: Http2ErrorCode.STREAM_CLOSED,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamClosed(Http2FrameType.DATA, streamId: 1));
        }

        [Fact]
        public async Task DATA_Received_NoStreamWindowSpace_ConnectionError()
        {
            // _maxData should be 1/4th of the default initial window size + 1.
            Assert.Equal(Http2PeerSettings.DefaultInitialWindowSize + 1, (uint)_maxData.Length * 4);

            await InitializeConnectionAsync(_waitForAbortApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);

            await SendDataAsync(1, _maxData, endStream: false);
            await SendDataAsync(1, _maxData, endStream: false);
            await SendDataAsync(1, _maxData, endStream: false);
            await SendDataAsync(1, _maxData, endStream: false);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.FLOW_CONTROL_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorFlowControlWindowExceeded);
        }

        [Fact]
        public async Task DATA_Received_NoConnectionWindowSpace_ConnectionError()
        {
            // _maxData should be 1/4th of the default initial window size + 1.
            Assert.Equal(Http2PeerSettings.DefaultInitialWindowSize + 1, (uint)_maxData.Length * 4);

            await InitializeConnectionAsync(_waitForAbortApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendDataAsync(1, _maxData, endStream: false);
            await SendDataAsync(1, _maxData, endStream: false);

            await StartStreamAsync(3, _browserRequestHeaders, endStream: false);
            await SendDataAsync(3, _maxData, endStream: false);
            await SendDataAsync(3, _maxData, endStream: false);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 3,
                expectedErrorCode: Http2ErrorCode.FLOW_CONTROL_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorFlowControlWindowExceeded);
        }

        [Fact]
        public async Task DATA_Sent_DespiteConnectionOutputFlowControl_IfEmptyAndEndsStream()
        {
            // Zero-length data frames are allowed to be sent even if there is no space available in the flow control window.
            // https://httpwg.org/specs/rfc7540.html#rfc.section.6.9.1

            var expectedFullFrameCountBeforeBackpressure = Http2PeerSettings.DefaultInitialWindowSize / _maxData.Length;
            var remainingBytesBeforeBackpressure = (int)Http2PeerSettings.DefaultInitialWindowSize % _maxData.Length;
            var remainingBytesAfterBackpressure = _maxData.Length - remainingBytesBeforeBackpressure;

            // Double the stream window to be 128KiB so it doesn't interfere with the rest of the test.
            _clientSettings.InitialWindowSize = Http2PeerSettings.DefaultInitialWindowSize * 2;

            await InitializeConnectionAsync(async context =>
            {
                var streamId = context.Features.Get<IHttp2StreamIdFeature>().StreamId;

                try
                {
                    if (streamId == 1)
                    {
                        for (var i = 0; i < expectedFullFrameCountBeforeBackpressure + 1; i++)
                        {
                            await context.Response.Body.WriteAsync(_maxData, 0, _maxData.Length);
                        }
                    }

                    _runningStreams[streamId].SetResult(null);
                }
                catch (Exception ex)
                {
                    _runningStreams[streamId].SetException(ex);
                    throw;
                }
            });

            // Start one stream that consumes the entire connection output window.
            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            for (var i = 0; i < expectedFullFrameCountBeforeBackpressure; i++)
            {
                await ExpectAsync(Http2FrameType.DATA,
                    withLength: _maxData.Length,
                    withFlags: (byte)Http2DataFrameFlags.NONE,
                    withStreamId: 1);
            }

            await ExpectAsync(Http2FrameType.DATA,
                withLength: remainingBytesBeforeBackpressure,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            // Start one more stream that receives an empty response despite connection backpressure.
            await StartStreamAsync(3, _browserRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 3);

            // Relieve connection backpressure to receive the rest of the first streams body.
            await SendWindowUpdateAsync(0, remainingBytesAfterBackpressure);

            await ExpectAsync(Http2FrameType.DATA,
                withLength: remainingBytesAfterBackpressure,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 3, ignoreNonGoAwayFrames: false);
            await WaitForAllStreamsAsync();
        }

        [Fact]
        public async Task DATA_Sent_DespiteStreamOutputFlowControl_IfEmptyAndEndsStream()
        {
            // Zero-length data frames are allowed to be sent even if there is no space available in the flow control window.
            // https://httpwg.org/specs/rfc7540.html#rfc.section.6.9.1

            // This only affects the stream windows. The connection-level window is always initialized at 64KiB.
            _clientSettings.InitialWindowSize = 0;

            await InitializeConnectionAsync(_noopApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task HEADERS_Received_Decoded()
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            VerifyDecodedRequestHeaders(_browserRequestHeaders);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(255)]
        public async Task HEADERS_Received_WithPadding_Decoded(byte padLength)
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            await SendHeadersWithPaddingAsync(1, _browserRequestHeaders, padLength, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            VerifyDecodedRequestHeaders(_browserRequestHeaders);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task HEADERS_Received_WithPriority_Decoded()
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            await SendHeadersWithPriorityAsync(1, _browserRequestHeaders, priority: 42, streamDependency: 0, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            VerifyDecodedRequestHeaders(_browserRequestHeaders);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(255)]
        public async Task HEADERS_Received_WithPriorityAndPadding_Decoded(byte padLength)
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            await SendHeadersWithPaddingAndPriorityAsync(1, _browserRequestHeaders, padLength, priority: 42, streamDependency: 0, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            VerifyDecodedRequestHeaders(_browserRequestHeaders);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task HEADERS_Received_WithTrailers_Decoded(bool sendData)
        {
            await InitializeConnectionAsync(_readTrailersApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS, _browserRequestHeaders);

            // Initialize another stream with a higher stream ID, and verify that after trailers are
            // decoded by the other stream, the highest opened stream ID is not reset to the lower ID
            // (the highest opened stream ID is sent by the server in the GOAWAY frame when shutting
            // down the connection).
            await SendHeadersAsync(3, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _browserRequestHeaders);

            // The second stream should end first, since the first one is waiting for the request body.
            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 3);

            if (sendData)
            {
                await SendDataAsync(1, _helloBytes, endStream: false);
            }

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _requestTrailers);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            VerifyDecodedRequestHeaders(_browserRequestHeaders.Concat(_requestTrailers));

            await StopConnectionAsync(expectedLastStreamId: 3, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task HEADERS_Received_ContainsExpect100Continue_100ContinueSent()
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _expectContinueRequestHeaders, false);

            var frame = await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 5,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            await SendDataAsync(1, _helloBytes, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            Assert.Equal(new byte[] { 0x08, 0x03, (byte)'1', (byte)'0', (byte)'0' }, frame.HeadersPayload.ToArray());

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task HEADERS_Received_AppCannotBlockOtherFrames()
        {
            var firstRequestReceived = new ManualResetEvent(false);
            var finishFirstRequest = new ManualResetEvent(false);
            var secondRequestReceived = new ManualResetEvent(false);
            var finishSecondRequest = new ManualResetEvent(false);
            await InitializeConnectionAsync(context =>
            {
                if (!firstRequestReceived.WaitOne(0))
                {
                    firstRequestReceived.Set();
                    Assert.True(finishFirstRequest.WaitOne(TimeSpan.FromSeconds(10)));
                }
                else
                {
                    secondRequestReceived.Set();
                    Assert.True(finishSecondRequest.WaitOne(TimeSpan.FromSeconds(10)));
                }

                return Task.CompletedTask;
            });

            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);
            Assert.True(firstRequestReceived.WaitOne(TimeSpan.FromSeconds(10)));

            await StartStreamAsync(3, _browserRequestHeaders, endStream: true);
            Assert.True(secondRequestReceived.WaitOne(TimeSpan.FromSeconds(10)));

            finishSecondRequest.Set();

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)(Http2HeadersFrameFlags.END_HEADERS),
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 3);

            finishFirstRequest.Set();

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)(Http2HeadersFrameFlags.END_HEADERS),
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 3, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task HEADERS_OverMaxStreamLimit_Refused()
        {
            _connection.ServerSettings.MaxConcurrentStreams = 1;

            var requestBlocker = new TaskCompletionSource<object>();
            await InitializeConnectionAsync(context => requestBlocker.Task);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await StartStreamAsync(3, _browserRequestHeaders, endStream: true);

            await WaitForStreamErrorAsync(3, Http2ErrorCode.REFUSED_STREAM, CoreStrings.Http2ErrorMaxStreams);

            requestBlocker.SetResult(0);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)(Http2HeadersFrameFlags.END_HEADERS),
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 3, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task HEADERS_Received_StreamIdZero_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await StartStreamAsync(0, _browserRequestHeaders, endStream: true);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdZero(Http2FrameType.HEADERS));
        }

        [Fact]
        public async Task HEADERS_Received_StreamIdEven_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await StartStreamAsync(2, _browserRequestHeaders, endStream: true);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdEven(Http2FrameType.HEADERS, streamId: 2));
        }

        [Fact]
        public async Task HEADERS_Received_StreamClosed_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            // Try to re-use the stream ID (http://httpwg.org/specs/rfc7540.html#rfc.section.5.1.1)
            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.STREAM_CLOSED,
                expectedErrorMessage: null);

            // There's a race where either of these messages could be logged, depending on if the stream cleanup has finished yet.
            var closedMessage = CoreStrings.FormatHttp2ErrorStreamClosed(Http2FrameType.HEADERS, streamId: 1);
            var halfClosedMessage = CoreStrings.FormatHttp2ErrorStreamHalfClosedRemote(Http2FrameType.HEADERS, streamId: 1);

            var message = Assert.Single(TestApplicationErrorLogger.Messages, m => m.Exception is Http2ConnectionErrorException);
            Assert.True(message.Exception.Message.IndexOf(closedMessage) >= 0
                || message.Exception.Message.IndexOf(halfClosedMessage) >= 0);
        }

        [Fact]
        public async Task HEADERS_Received_StreamHalfClosedRemote_ConnectionError()
        {
            // Use _waitForAbortApplication so we know the stream will still be active when we send the illegal DATA frame
            await InitializeConnectionAsync(_waitForAbortApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _browserRequestHeaders);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.STREAM_CLOSED,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamHalfClosedRemote(Http2FrameType.HEADERS, streamId: 1));
        }

        [Fact]
        public async Task HEADERS_Received_StreamClosedImplicitly_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await StartStreamAsync(3, _browserRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 3);

            // Stream 1 was implicitly closed by opening stream 3 before (http://httpwg.org/specs/rfc7540.html#rfc.section.5.1.1)
            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 3,
                expectedErrorCode: Http2ErrorCode.STREAM_CLOSED,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamClosed(Http2FrameType.HEADERS, streamId: 1));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(255)]
        public async Task HEADERS_Received_PaddingEqualToFramePayloadLength_ConnectionError(byte padLength)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendInvalidHeadersFrameAsync(1, frameLength: padLength, padLength: padLength);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: true,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorPaddingTooLong(Http2FrameType.HEADERS));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        [InlineData(254, 255)]
        public async Task HEADERS_Received_PaddingGreaterThanFramePayloadLength_ConnectionError(int frameLength, byte padLength)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendInvalidHeadersFrameAsync(1, frameLength, padLength);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: true,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorPaddingTooLong(Http2FrameType.HEADERS));
        }

        [Fact]
        public async Task HEADERS_Received_InterleavedWithHeaders_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _browserRequestHeaders);
            await SendHeadersAsync(3, Http2HeadersFrameFlags.NONE, _browserRequestHeaders);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorHeadersInterleaved(Http2FrameType.HEADERS, streamId: 3, headersStreamId: 1));
        }

        [Fact]
        public async Task HEADERS_Received_WithPriority_StreamDependencyOnSelf_ConnectionError()
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            await SendHeadersWithPriorityAsync(1, _browserRequestHeaders, priority: 42, streamDependency: 1, endStream: true);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamSelfDependency(Http2FrameType.HEADERS, streamId: 1));
        }

        [Fact]
        public async Task HEADERS_Received_IncompleteHeaderBlock_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendIncompleteHeadersFrameAsync(streamId: 1);

            await WaitForConnectionErrorAsync<HPackDecodingException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.COMPRESSION_ERROR,
                expectedErrorMessage: CoreStrings.HPackErrorIncompleteHeaderBlock);
        }

        [Theory]
        [MemberData(nameof(IllegalTrailerData))]
        public async Task HEADERS_Received_WithTrailers_ContainsIllegalTrailer_ConnectionError(byte[] trailers, string expectedErrorMessage)
        {
            await InitializeConnectionAsync(_readTrailersApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS, _browserRequestHeaders);
            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, trailers);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: expectedErrorMessage);
        }

        [Theory]
        [InlineData(Http2HeadersFrameFlags.NONE)]
        [InlineData(Http2HeadersFrameFlags.END_HEADERS)]
        public async Task HEADERS_Received_WithTrailers_EndStreamNotSet_ConnectionError(Http2HeadersFrameFlags flags)
        {
            await InitializeConnectionAsync(_readTrailersApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS, _browserRequestHeaders);
            await SendHeadersAsync(1, flags, _requestTrailers);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorHeadersWithTrailersNoEndStream);
        }

        [Theory]
        [MemberData(nameof(UpperCaseHeaderNameData))]
        public async Task HEADERS_Received_HeaderNameContainsUpperCaseCharacter_StreamError(byte[] headerBlock)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS, headerBlock);
            await WaitForStreamErrorAsync(
                expectedStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorHeaderNameUppercase);

            // Verify that the stream ID can't be re-used
            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS, _browserRequestHeaders);
            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.STREAM_CLOSED,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamClosed(Http2FrameType.HEADERS, streamId: 1));
        }

        [Fact]
        public Task HEADERS_Received_HeaderBlockContainsUnknownPseudoHeaderField_StreamError()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(":unknown", "0"),
            };

            return HEADERS_Received_InvalidHeaderFields_StreamError(headers, expectedErrorMessage: CoreStrings.Http2ErrorUnknownPseudoHeaderField);
        }

        [Fact]
        public Task HEADERS_Received_HeaderBlockContainsResponsePseudoHeaderField_StreamError()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>(HeaderNames.Status, "200"),
            };

            return HEADERS_Received_InvalidHeaderFields_StreamError(headers, expectedErrorMessage: CoreStrings.Http2ErrorResponsePseudoHeaderField);
        }

        [Theory]
        [MemberData(nameof(DuplicatePseudoHeaderFieldData))]
        public Task HEADERS_Received_HeaderBlockContainsDuplicatePseudoHeaderField_StreamError(IEnumerable<KeyValuePair<string, string>> headers)
        {
            return HEADERS_Received_InvalidHeaderFields_StreamError(headers, expectedErrorMessage: CoreStrings.Http2ErrorDuplicatePseudoHeaderField);
        }

        [Theory]
        [MemberData(nameof(MissingPseudoHeaderFieldData))]
        public Task HEADERS_Received_HeaderBlockDoesNotContainMandatoryPseudoHeaderField_StreamError(IEnumerable<KeyValuePair<string, string>> headers)
        {
            return HEADERS_Received_InvalidHeaderFields_StreamError(headers, expectedErrorMessage: CoreStrings.Http2ErrorMissingMandatoryPseudoHeaderFields);
        }

        [Theory]
        [MemberData(nameof(ConnectMissingPseudoHeaderFieldData))]
        public async Task HEADERS_Received_HeaderBlockDoesNotContainMandatoryPseudoHeaderField_MethodIsCONNECT_NoError(IEnumerable<KeyValuePair<string, string>> headers)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, headers);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2HeadersFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Theory]
        [MemberData(nameof(PseudoHeaderFieldAfterRegularHeadersData))]
        public Task HEADERS_Received_HeaderBlockContainsPseudoHeaderFieldAfterRegularHeaders_StreamError(IEnumerable<KeyValuePair<string, string>> headers)
        {
            return HEADERS_Received_InvalidHeaderFields_StreamError(headers, expectedErrorMessage: CoreStrings.Http2ErrorPseudoHeaderFieldAfterRegularHeaders);
        }

        private async Task HEADERS_Received_InvalidHeaderFields_StreamError(IEnumerable<KeyValuePair<string, string>> headers, string expectedErrorMessage)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS, headers);
            await WaitForStreamErrorAsync(
                expectedStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: expectedErrorMessage);

            // Verify that the stream ID can't be re-used
            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS, _browserRequestHeaders);
            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.STREAM_CLOSED,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamClosed(Http2FrameType.HEADERS, streamId: 1));
        }

        [Fact]
        public Task HEADERS_Received_HeaderBlockContainsConnectionHeader_StreamError()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>("connection", "keep-alive")
            };

            return HEADERS_Received_InvalidHeaderFields_StreamError(headers, CoreStrings.Http2ErrorConnectionSpecificHeaderField);
        }

        [Fact]
        public Task HEADERS_Received_HeaderBlockContainsTEHeader_ValueIsNotTrailers_StreamError()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>("te", "trailers, deflate")
            };

            return HEADERS_Received_InvalidHeaderFields_StreamError(headers, CoreStrings.Http2ErrorConnectionSpecificHeaderField);
        }

        [Fact]
        public async Task HEADERS_Received_HeaderBlockContainsTEHeader_ValueIsTrailers_NoError()
        {
            var headers = new[]
            {
                new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                new KeyValuePair<string, string>("te", "trailers")
            };

            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, headers);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2HeadersFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task PRIORITY_Received_StreamIdZero_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendPriorityAsync(0);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdZero(Http2FrameType.PRIORITY));
        }

        [Fact]
        public async Task PRIORITY_Received_StreamIdEven_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendPriorityAsync(2);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdEven(Http2FrameType.PRIORITY, streamId: 2));
        }

        [Theory]
        [InlineData(4)]
        [InlineData(6)]
        public async Task PRIORITY_Received_LengthNotFive_ConnectionError(int length)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendInvalidPriorityFrameAsync(1, length);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.FRAME_SIZE_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorUnexpectedFrameLength(Http2FrameType.PRIORITY, expectedLength: 5));
        }

        [Fact]
        public async Task PRIORITY_Received_InterleavedWithHeaders_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _browserRequestHeaders);
            await SendPriorityAsync(1);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorHeadersInterleaved(Http2FrameType.PRIORITY, streamId: 1, headersStreamId: 1));
        }

        [Fact]
        public async Task PRIORITY_Received_StreamDependencyOnSelf_ConnectionError()
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            await SendPriorityAsync(1, streamDependency: 1);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamSelfDependency(Http2FrameType.PRIORITY, 1));
        }

        [Fact]
        public async Task RST_STREAM_Received_ContinuesAppsAwaitingConnectionOutputFlowControl()
        {
            var writeTasks = new Task[4];

            var expectedFullFrameCountBeforeBackpressure = Http2PeerSettings.DefaultInitialWindowSize / _maxData.Length;
            var remainingBytesBeforeBackpressure = (int)Http2PeerSettings.DefaultInitialWindowSize % _maxData.Length;

            // Double the stream window to be 128KiB so it doesn't interfere with the rest of the test.
            _clientSettings.InitialWindowSize = Http2PeerSettings.DefaultInitialWindowSize * 2;

            await InitializeConnectionAsync(async context =>
            {
                var streamId = context.Features.Get<IHttp2StreamIdFeature>().StreamId;

                var abortedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                var writeTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

                context.RequestAborted.Register(() =>
                {
                    lock (_abortedStreamIdsLock)
                    {
                        _abortedStreamIds.Add(streamId);
                        abortedTcs.SetResult(null);
                    }
                });

                try
                {
                    writeTasks[streamId] = writeTcs.Task;

                    // Flush headers even if the body can't yet be written because of flow control.
                    await context.Response.Body.FlushAsync();

                    for (var i = 0; i < expectedFullFrameCountBeforeBackpressure; i++)
                    {
                        await context.Response.Body.WriteAsync(_maxData, 0, _maxData.Length);
                    }

                    await context.Response.Body.WriteAsync(_maxData, 0, remainingBytesBeforeBackpressure + 1);

                    writeTcs.SetResult(null);

                    await abortedTcs.Task;

                    _runningStreams[streamId].SetResult(null);
                }
                catch (Exception ex)
                {
                    _runningStreams[streamId].SetException(ex);
                    throw;
                }
            });

            // Start one stream that consumes the entire connection output window.
            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            for (var i = 0; i < expectedFullFrameCountBeforeBackpressure; i++)
            {
                await ExpectAsync(Http2FrameType.DATA,
                    withLength: _maxData.Length,
                    withFlags: (byte)Http2DataFrameFlags.NONE,
                    withStreamId: 1);
            }

            await ExpectAsync(Http2FrameType.DATA,
                withLength: remainingBytesBeforeBackpressure,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            // Ensure connection-level backpressure was hit.
            Assert.False(writeTasks[1].IsCompleted);

            // Start another stream that immediately experiences backpressure.
            await StartStreamAsync(3, _browserRequestHeaders, endStream: true);

            // The headers, but not the data for stream 3, can be sent prior to any window updates.
            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);

            await SendRstStreamAsync(1);
            // Any paused writes for stream 1 should complete after an RST_STREAM
            // even without any preceeding window updates.
            await _runningStreams[1].Task.DefaultTimeout();

            // A connection-level window update allows the non-reset stream to continue.
            await SendWindowUpdateAsync(0, (int)Http2PeerSettings.DefaultInitialWindowSize);

            for (var i = 0; i < expectedFullFrameCountBeforeBackpressure; i++)
            {
                await ExpectAsync(Http2FrameType.DATA,
                    withLength: _maxData.Length,
                    withFlags: (byte)Http2DataFrameFlags.NONE,
                    withStreamId: 3);
            }

            await ExpectAsync(Http2FrameType.DATA,
                withLength: remainingBytesBeforeBackpressure,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 3);

            Assert.False(writeTasks[3].IsCompleted);

            await SendRstStreamAsync(3);
            await _runningStreams[3].Task.DefaultTimeout();

            await StopConnectionAsync(expectedLastStreamId: 3, ignoreNonGoAwayFrames: false);

            await WaitForAllStreamsAsync();
            Assert.Contains(1, _abortedStreamIds);
            Assert.Contains(3, _abortedStreamIds);
        }

        [Fact]
        public async Task RST_STREAM_Received_ContinuesAppsAwaitingStreamOutputFlowControl()
        {
            var writeTasks = new Task[6];
            var initialWindowSize = _helloWorldBytes.Length / 2;

            // This only affects the stream windows. The connection-level window is always initialized at 64KiB.
            _clientSettings.InitialWindowSize = (uint)initialWindowSize;

            await InitializeConnectionAsync(async context =>
            {
                var streamId = context.Features.Get<IHttp2StreamIdFeature>().StreamId;

                var abortedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                var writeTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

                context.RequestAborted.Register(() =>
                {
                    lock (_abortedStreamIdsLock)
                    {
                        _abortedStreamIds.Add(streamId);
                        abortedTcs.SetResult(null);
                    }
                });

                try
                {
                    writeTasks[streamId] = writeTcs.Task;
                    await context.Response.Body.WriteAsync(_helloWorldBytes, 0, _helloWorldBytes.Length);
                    writeTcs.SetResult(null);

                    await abortedTcs.Task;

                    _runningStreams[streamId].SetResult(null);
                }
                catch (Exception ex)
                {
                    _runningStreams[streamId].SetException(ex);
                    throw;
                }
            });

            async Task VerifyStreamBackpressure(int streamId)
            {
                await StartStreamAsync(streamId, _browserRequestHeaders, endStream: true);

                await ExpectAsync(Http2FrameType.HEADERS,
                    withLength: 37,
                    withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                    withStreamId: streamId);

                var dataFrame = await ExpectAsync(Http2FrameType.DATA,
                    withLength: initialWindowSize,
                    withFlags: (byte)Http2DataFrameFlags.NONE,
                    withStreamId: streamId);

                Assert.Equal(new ArraySegment<byte>(_helloWorldBytes, 0, initialWindowSize), dataFrame.DataPayload);
                Assert.False(writeTasks[streamId].IsCompleted);
            }

            await VerifyStreamBackpressure(1);
            await VerifyStreamBackpressure(3);
            await VerifyStreamBackpressure(5);

            await SendRstStreamAsync(1);
            await writeTasks[1].DefaultTimeout();
            Assert.False(writeTasks[3].IsCompleted);
            Assert.False(writeTasks[5].IsCompleted);

            await SendRstStreamAsync(3);
            await writeTasks[3].DefaultTimeout();
            Assert.False(writeTasks[5].IsCompleted);

            await SendRstStreamAsync(5);
            await writeTasks[5].DefaultTimeout();

            await StopConnectionAsync(expectedLastStreamId: 5, ignoreNonGoAwayFrames: false);

            await WaitForAllStreamsAsync();
            Assert.Contains(1, _abortedStreamIds);
            Assert.Contains(3, _abortedStreamIds);
            Assert.Contains(5, _abortedStreamIds);
        }

        [Fact]
        public async Task RST_STREAM_Received_ReturnsSpaceToConnectionInputFlowControlWindow()
        {
            // _maxData should be 1/4th of the default initial window size + 1.
            Assert.Equal(Http2PeerSettings.DefaultInitialWindowSize + 1, (uint)_maxData.Length * 4);

            await InitializeConnectionAsync(_waitForAbortApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendDataAsync(1, _maxData, endStream: false);
            await SendDataAsync(1, _maxData, endStream: false);
            await SendDataAsync(1, _maxData, endStream: false);

            await SendRstStreamAsync(1);
            await WaitForAllStreamsAsync();

            var connectionWindowUpdateFrame = await ExpectAsync(Http2FrameType.WINDOW_UPDATE,
                withLength: 4,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 0);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);

            Assert.Contains(1, _abortedStreamIds);
            Assert.Equal(_maxData.Length * 3, connectionWindowUpdateFrame.WindowUpdateSizeIncrement);
        }

        [Fact]
        public async Task RST_STREAM_Received_StreamIdZero_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendRstStreamAsync(0);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdZero(Http2FrameType.RST_STREAM));
        }

        [Fact]
        public async Task RST_STREAM_Received_StreamIdEven_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendRstStreamAsync(2);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdEven(Http2FrameType.RST_STREAM, streamId: 2));
        }

        [Fact]
        public async Task RST_STREAM_Received_StreamIdle_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendRstStreamAsync(1);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdle(Http2FrameType.RST_STREAM, streamId: 1));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(5)]
        public async Task RST_STREAM_Received_LengthNotFour_ConnectionError(int length)
        {
            await InitializeConnectionAsync(_noopApplication);

            // Start stream 1 so it's legal to send it RST_STREAM frames
            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await SendInvalidRstStreamFrameAsync(1, length);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: true,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.FRAME_SIZE_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorUnexpectedFrameLength(Http2FrameType.RST_STREAM, expectedLength: 4));
        }

        [Fact]
        public async Task RST_STREAM_Received_InterleavedWithHeaders_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _browserRequestHeaders);
            await SendRstStreamAsync(1);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorHeadersInterleaved(Http2FrameType.RST_STREAM, streamId: 1, headersStreamId: 1));
        }

        [Fact]
        public async Task SETTINGS_KestrelDefaults_Sent()
        {
            _connectionTask = _connection.ProcessRequestsAsync(new DummyApplication(_noopApplication));

            await SendPreambleAsync().ConfigureAwait(false);
            await SendSettingsAsync();

            var frame = await ExpectAsync(Http2FrameType.SETTINGS,
                withLength: 6,
                withFlags: 0,
                withStreamId: 0);

            // Only non protocol defaults are sent
            Assert.Equal(1, frame.SettingsCount);
            var settings = frame.GetSettings();
            Assert.Equal(1, settings.Count);
            var setting = settings[0];
            Assert.Equal(Http2SettingsParameter.SETTINGS_MAX_CONCURRENT_STREAMS, setting.Parameter);
            Assert.Equal(100u, setting.Value);

            await ExpectAsync(Http2FrameType.SETTINGS,
                withLength: 0,
                withFlags: (byte)Http2SettingsFrameFlags.ACK,
                withStreamId: 0);

            await StopConnectionAsync(expectedLastStreamId: 0, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task SETTINGS_Custom_Sent()
        {
            _connection.ServerSettings.MaxConcurrentStreams = 1;

            _connectionTask = _connection.ProcessRequestsAsync(new DummyApplication(_noopApplication));

            await SendPreambleAsync().ConfigureAwait(false);
            await SendSettingsAsync();

            var frame = await ExpectAsync(Http2FrameType.SETTINGS,
                withLength: 6,
                withFlags: 0,
                withStreamId: 0);

            // Only non protocol defaults are sent
            Assert.Equal(1, frame.SettingsCount);
            var settings = frame.GetSettings();
            Assert.Equal(1, settings.Count);
            var setting = settings[0];
            Assert.Equal(Http2SettingsParameter.SETTINGS_MAX_CONCURRENT_STREAMS, setting.Parameter);
            Assert.Equal(1u, setting.Value);

            await ExpectAsync(Http2FrameType.SETTINGS,
                withLength: 0,
                withFlags: (byte)Http2SettingsFrameFlags.ACK,
                withStreamId: 0);

            await StopConnectionAsync(expectedLastStreamId: 0, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task SETTINGS_Received_Sends_ACK()
        {
            await InitializeConnectionAsync(_noopApplication);

            await StopConnectionAsync(expectedLastStreamId: 0, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task SETTINGS_ACK_Received_DoesNotSend_ACK()
        {
            await InitializeConnectionAsync(_noopApplication);

            var frame = new Http2Frame();
            frame.PrepareSettings(Http2SettingsFrameFlags.ACK);
            await SendAsync(frame.Raw);

            await StopConnectionAsync(expectedLastStreamId: 0, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task SETTINGS_Received_StreamIdNotZero_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendSettingsWithInvalidStreamIdAsync(1);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdNotZero(Http2FrameType.SETTINGS));
        }

        [Theory]
        [InlineData(Http2SettingsParameter.SETTINGS_ENABLE_PUSH, 2, Http2ErrorCode.PROTOCOL_ERROR)]
        [InlineData(Http2SettingsParameter.SETTINGS_ENABLE_PUSH, uint.MaxValue, Http2ErrorCode.PROTOCOL_ERROR)]
        [InlineData(Http2SettingsParameter.SETTINGS_INITIAL_WINDOW_SIZE, (uint)int.MaxValue + 1, Http2ErrorCode.FLOW_CONTROL_ERROR)]
        [InlineData(Http2SettingsParameter.SETTINGS_INITIAL_WINDOW_SIZE, uint.MaxValue, Http2ErrorCode.FLOW_CONTROL_ERROR)]
        [InlineData(Http2SettingsParameter.SETTINGS_MAX_FRAME_SIZE, 0, Http2ErrorCode.PROTOCOL_ERROR)]
        [InlineData(Http2SettingsParameter.SETTINGS_MAX_FRAME_SIZE, 1, Http2ErrorCode.PROTOCOL_ERROR)]
        [InlineData(Http2SettingsParameter.SETTINGS_MAX_FRAME_SIZE, 16 * 1024 - 1, Http2ErrorCode.PROTOCOL_ERROR)]
        [InlineData(Http2SettingsParameter.SETTINGS_MAX_FRAME_SIZE, 16 * 1024 * 1024, Http2ErrorCode.PROTOCOL_ERROR)]
        [InlineData(Http2SettingsParameter.SETTINGS_MAX_FRAME_SIZE, uint.MaxValue, Http2ErrorCode.PROTOCOL_ERROR)]
        public async Task SETTINGS_Received_InvalidParameterValue_ConnectionError(Http2SettingsParameter parameter, uint value, Http2ErrorCode expectedErrorCode)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendSettingsWithInvalidParameterValueAsync(parameter, value);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: expectedErrorCode,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorSettingsParameterOutOfRange(parameter));
        }

        [Fact]
        public async Task SETTINGS_Received_InterleavedWithHeaders_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _browserRequestHeaders);
            await SendSettingsAsync();

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorHeadersInterleaved(Http2FrameType.SETTINGS, streamId: 0, headersStreamId: 1));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(16 * 1024 - 9)] // Min. max. frame size minus header length
        public async Task SETTINGS_Received_WithACK_LengthNotZero_ConnectionError(int length)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendSettingsAckWithInvalidLengthAsync(length);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.FRAME_SIZE_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorSettingsAckLengthNotZero);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(34)]
        [InlineData(37)]
        public async Task SETTINGS_Received_LengthNotMultipleOfSix_ConnectionError(int length)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendSettingsWithInvalidLengthAsync(length);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.FRAME_SIZE_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorSettingsLengthNotMultipleOfSix);
        }

        [Fact]
        public async Task SETTINGS_Received_WithInitialWindowSizePushingStreamWindowOverMax_ConnectionError()
        {
            await InitializeConnectionAsync(_waitForAbortApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);

            await SendWindowUpdateAsync(1, (int)(Http2PeerSettings.MaxWindowSize - _clientSettings.InitialWindowSize));

            _clientSettings.InitialWindowSize += 1;
            await SendSettingsAsync();

            await ExpectAsync(Http2FrameType.SETTINGS,
                withLength: 0,
                withFlags: (byte)Http2SettingsFrameFlags.ACK,
                withStreamId: 0);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.FLOW_CONTROL_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorInitialWindowSizeInvalid);
        }

        [Fact]
        public async Task PUSH_PROMISE_Received_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendPushPromiseFrameAsync();

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorPushPromiseReceived);
        }

        [Fact]
        public async Task PING_Received_SendsACK()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendPingAsync(Http2PingFrameFlags.NONE);
            await ExpectAsync(Http2FrameType.PING,
                withLength: 8,
                withFlags: (byte)Http2PingFrameFlags.ACK,
                withStreamId: 0);

            await StopConnectionAsync(expectedLastStreamId: 0, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task PING_Received_WithACK_DoesNotSendACK()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendPingAsync(Http2PingFrameFlags.ACK);

            await StopConnectionAsync(expectedLastStreamId: 0, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task PING_Received_InterleavedWithHeaders_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _browserRequestHeaders);
            await SendPingAsync(Http2PingFrameFlags.NONE);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorHeadersInterleaved(Http2FrameType.PING, streamId: 0, headersStreamId: 1));
        }

        [Fact]
        public async Task PING_Received_StreamIdNotZero_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendPingWithInvalidStreamIdAsync(streamId: 1);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdNotZero(Http2FrameType.PING));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(9)]
        public async Task PING_Received_LengthNotEight_ConnectionError(int length)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendPingWithInvalidLengthAsync(length);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.FRAME_SIZE_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorUnexpectedFrameLength(Http2FrameType.PING, expectedLength: 8));
        }

        [Fact]
        public async Task GOAWAY_Received_ConnectionStops()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendGoAwayAsync();

            await WaitForConnectionStopAsync(expectedLastStreamId: 0, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task GOAWAY_Received_SetsConnectionStateToClosingAndWaitForAllStreamsToComplete()
        {
            await InitializeConnectionAsync(_echoApplication);

            // Start some streams
            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await StartStreamAsync(3, _browserRequestHeaders, endStream: false);

            await SendGoAwayAsync();

            await _closingStateReached.Task.DefaultTimeout();

            await SendDataAsync(1, _helloBytes, true);
            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);
            await SendDataAsync(3, _helloBytes, true);
            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 3);

            await WaitForConnectionStopAsync(expectedLastStreamId: 3, ignoreNonGoAwayFrames: false);
            await _closedStateReached.Task.DefaultTimeout();
        }

        [Fact]
        public async Task GOAWAY_Received_ContinuesAppsAwaitingConnectionOutputFlowControl()
        {
            var writeTasks = new Task[6];
            var expectedFullFrameCountBeforeBackpressure = Http2PeerSettings.DefaultInitialWindowSize / _maxData.Length;
            var remainingBytesBeforeBackpressure = (int)Http2PeerSettings.DefaultInitialWindowSize % _maxData.Length;

            // Double the stream window to be 128KiB so it doesn't interfere with the rest of the test.
            _clientSettings.InitialWindowSize = Http2PeerSettings.DefaultInitialWindowSize * 2;

            await InitializeConnectionAsync(async context =>
            {
                var streamId = context.Features.Get<IHttp2StreamIdFeature>().StreamId;

                var abortedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                var writeTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

                context.RequestAborted.Register(() =>
                {
                    lock (_abortedStreamIdsLock)
                    {
                        _abortedStreamIds.Add(streamId);
                        abortedTcs.SetResult(null);
                    }
                });

                try
                {
                    writeTasks[streamId] = writeTcs.Task;

                    // Flush headers even if the body can't yet be written because of flow control.
                    await context.Response.Body.FlushAsync();

                    for (var i = 0; i < expectedFullFrameCountBeforeBackpressure; i++)
                    {
                        await context.Response.Body.WriteAsync(_maxData, 0, _maxData.Length);
                    }

                    await context.Response.Body.WriteAsync(_maxData, 0, remainingBytesBeforeBackpressure + 1);

                    writeTcs.SetResult(null);

                    await abortedTcs.Task;

                    _runningStreams[streamId].SetResult(null);
                }
                catch (Exception ex)
                {
                    _runningStreams[streamId].SetException(ex);
                    throw;
                }
            });

            // Start one stream that consumes the entire connection output window.
            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            for (var i = 0; i < expectedFullFrameCountBeforeBackpressure; i++)
            {
                await ExpectAsync(Http2FrameType.DATA,
                    withLength: _maxData.Length,
                    withFlags: (byte)Http2DataFrameFlags.NONE,
                    withStreamId: 1);
            }

            await ExpectAsync(Http2FrameType.DATA,
                withLength: remainingBytesBeforeBackpressure,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            Assert.False(writeTasks[1].IsCompleted);

            // Start two more streams that immediately experience backpressure.
            // The headers, but not the data for the stream, can still be sent.
            await StartStreamAsync(3, _browserRequestHeaders, endStream: true);
            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);

            await StartStreamAsync(5, _browserRequestHeaders, endStream: true);
            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 5);

            // Close all pipes and wait for response to drain
            _pair.Application.Output.Complete();
            _pair.Transport.Input.Complete();
            _pair.Transport.Output.Complete();

            await WaitForConnectionStopAsync(expectedLastStreamId: 5, ignoreNonGoAwayFrames: false);

            await WaitForAllStreamsAsync();
            Assert.Contains(1, _abortedStreamIds);
            Assert.Contains(3, _abortedStreamIds);
            Assert.Contains(5, _abortedStreamIds);
        }

        [Fact]
        public async Task GOAWAY_Received_ContinuesAppsAwaitingStreamOutputFlowControle()
        {
            var writeTasks = new Task[6];
            var initialWindowSize = _helloWorldBytes.Length / 2;

            // This only affects the stream windows. The connection-level window is always initialized at 64KiB.
            _clientSettings.InitialWindowSize = (uint)initialWindowSize;

            await InitializeConnectionAsync(async context =>
            {
                var streamId = context.Features.Get<IHttp2StreamIdFeature>().StreamId;

                var abortedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                var writeTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

                context.RequestAborted.Register(() =>
                {
                    lock (_abortedStreamIdsLock)
                    {
                        _abortedStreamIds.Add(streamId);
                        abortedTcs.SetResult(null);
                    }
                });

                try
                {
                    writeTasks[streamId] = writeTcs.Task;
                    await context.Response.Body.WriteAsync(_helloWorldBytes, 0, _helloWorldBytes.Length);
                    writeTcs.SetResult(null);

                    await abortedTcs.Task;

                    _runningStreams[streamId].SetResult(null);
                }
                catch (Exception ex)
                {
                    _runningStreams[streamId].SetException(ex);
                    throw;
                }
            });

            async Task VerifyStreamBackpressure(int streamId)
            {
                await StartStreamAsync(streamId, _browserRequestHeaders, endStream: true);

                await ExpectAsync(Http2FrameType.HEADERS,
                    withLength: 37,
                    withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                    withStreamId: streamId);

                var dataFrame = await ExpectAsync(Http2FrameType.DATA,
                    withLength: initialWindowSize,
                    withFlags: (byte)Http2DataFrameFlags.NONE,
                    withStreamId: streamId);

                Assert.Equal(new ArraySegment<byte>(_helloWorldBytes, 0, initialWindowSize), dataFrame.DataPayload);
                Assert.False(writeTasks[streamId].IsCompleted);
            }

            await VerifyStreamBackpressure(1);
            await VerifyStreamBackpressure(3);
            await VerifyStreamBackpressure(5);

            // Close all pipes and wait for response to drain
            _pair.Application.Output.Complete();
            _pair.Transport.Input.Complete();
            _pair.Transport.Output.Complete();

            await WaitForConnectionStopAsync(expectedLastStreamId: 5, ignoreNonGoAwayFrames: false);

            await WaitForAllStreamsAsync();
            Assert.Contains(1, _abortedStreamIds);
            Assert.Contains(3, _abortedStreamIds);
            Assert.Contains(5, _abortedStreamIds);
        }

        [Fact]
        public async Task GOAWAY_Received_StreamIdNotZero_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendInvalidGoAwayFrameAsync();

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdNotZero(Http2FrameType.GOAWAY));
        }

        [Fact]
        public async Task GOAWAY_Received_InterleavedWithHeaders_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _browserRequestHeaders);
            await SendGoAwayAsync();

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorHeadersInterleaved(Http2FrameType.GOAWAY, streamId: 0, headersStreamId: 1));
        }

        [Fact]
        public async Task WINDOW_UPDATE_Received_StreamIdEven_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendWindowUpdateAsync(2, sizeIncrement: 42);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdEven(Http2FrameType.WINDOW_UPDATE, streamId: 2));
        }

        [Fact]
        public async Task WINDOW_UPDATE_Received_InterleavedWithHeaders_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _browserRequestHeaders);
            await SendWindowUpdateAsync(1, sizeIncrement: 42);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorHeadersInterleaved(Http2FrameType.WINDOW_UPDATE, streamId: 1, headersStreamId: 1));
        }

        [Theory]
        [InlineData(0, 3)]
        [InlineData(0, 5)]
        [InlineData(1, 3)]
        [InlineData(1, 5)]
        public async Task WINDOW_UPDATE_Received_LengthNotFour_ConnectionError(int streamId, int length)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendInvalidWindowUpdateAsync(streamId, sizeIncrement: 42, length: length);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.FRAME_SIZE_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorUnexpectedFrameLength(Http2FrameType.WINDOW_UPDATE, expectedLength: 4));
        }

        [Fact]
        public async Task WINDOW_UPDATE_Received_OnConnection_SizeIncrementZero_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendWindowUpdateAsync(0, sizeIncrement: 0);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorWindowUpdateIncrementZero);
        }

        [Fact]
        public async Task WINDOW_UPDATE_Received_OnStream_SizeIncrementZero_ConnectionError()
        {
            await InitializeConnectionAsync(_waitForAbortApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);
            await SendWindowUpdateAsync(1, sizeIncrement: 0);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorWindowUpdateIncrementZero);
        }

        [Fact]
        public async Task WINDOW_UPDATE_Received_StreamIdle_ConnectionError()
        {
            await InitializeConnectionAsync(_waitForAbortApplication);

            await SendWindowUpdateAsync(1, sizeIncrement: 1);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdle(Http2FrameType.WINDOW_UPDATE, streamId: 1));
        }

        [Fact]
        public async Task WINDOW_UPDATE_Received_OnConnection_IncreasesWindowAboveMaxValue_ConnectionError()
        {
            var maxIncrement = (int)(Http2PeerSettings.MaxWindowSize - Http2PeerSettings.DefaultInitialWindowSize);

            await InitializeConnectionAsync(_noopApplication);

            await SendWindowUpdateAsync(0, sizeIncrement: maxIncrement);
            await SendWindowUpdateAsync(0, sizeIncrement: 1);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http2ErrorCode.FLOW_CONTROL_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorWindowUpdateSizeInvalid);
        }

        [Fact]
        public async Task WINDOW_UPDATE_Received_OnStream_IncreasesWindowAboveMaxValue_StreamError()
        {
            var maxIncrement = (int)(Http2PeerSettings.MaxWindowSize - Http2PeerSettings.DefaultInitialWindowSize);

            await InitializeConnectionAsync(_waitForAbortApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);
            await SendWindowUpdateAsync(1, sizeIncrement: maxIncrement);
            await SendWindowUpdateAsync(1, sizeIncrement: 1);

            await WaitForStreamErrorAsync(
                expectedStreamId: 1,
                expectedErrorCode: Http2ErrorCode.FLOW_CONTROL_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorWindowUpdateSizeInvalid);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task WINDOW_UPDATE_Received_OnConnection_Respected()
        {
            var expectedFullFrameCountBeforeBackpressure = Http2PeerSettings.DefaultInitialWindowSize / _maxData.Length;

            // Use this semaphore to wait until a new data frame is expected before trying to send it.
            // This way we're sure that if Response.Body.WriteAsync returns an incomplete task, it's because
            // of the flow control window and not Pipe backpressure.
            var expectingDataSem = new SemaphoreSlim(0);
            var backpressureObservedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backpressureReleasedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Double the stream window to be 128KiB so it doesn't interfere with the rest of the test.
            _clientSettings.InitialWindowSize = Http2PeerSettings.DefaultInitialWindowSize * 2;

            await InitializeConnectionAsync(async context =>
            {
                try
                {
                    // Flush the headers so expectingDataSem is released.
                    await context.Response.Body.FlushAsync();

                    for (var i = 0; i < expectedFullFrameCountBeforeBackpressure; i++)
                    {
                        await expectingDataSem.WaitAsync();
                        Assert.True(context.Response.Body.WriteAsync(_maxData, 0, _maxData.Length).IsCompleted);
                    }

                    await expectingDataSem.WaitAsync();
                    var lastWriteTask = context.Response.Body.WriteAsync(_maxData, 0, _maxData.Length);

                    Assert.False(lastWriteTask.IsCompleted);
                    backpressureObservedTcs.TrySetResult(null);

                    await lastWriteTask;
                    backpressureReleasedTcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    backpressureObservedTcs.TrySetException(ex);
                    backpressureReleasedTcs.TrySetException(ex);
                    throw;
                }
            });

            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            for (var i = 0; i < expectedFullFrameCountBeforeBackpressure; i++)
            {
                expectingDataSem.Release();
                await ExpectAsync(Http2FrameType.DATA,
                    withLength: _maxData.Length,
                    withFlags: (byte)Http2DataFrameFlags.NONE,
                    withStreamId: 1);
            }

            var remainingBytesBeforeBackpressure = (int)Http2PeerSettings.DefaultInitialWindowSize % _maxData.Length;
            var remainingBytesAfterBackpressure = _maxData.Length - remainingBytesBeforeBackpressure;

            expectingDataSem.Release();
            await ExpectAsync(Http2FrameType.DATA,
                withLength: remainingBytesBeforeBackpressure,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            await backpressureObservedTcs.Task.DefaultTimeout();

            await SendWindowUpdateAsync(0, remainingBytesAfterBackpressure);

            await backpressureReleasedTcs.Task.DefaultTimeout();

            // This is the remaining data that could have come in the last frame if not for the flow control window,
            // so there's no need to release the semaphore again.
            await ExpectAsync(Http2FrameType.DATA,
                withLength: remainingBytesAfterBackpressure,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task WINDOW_UPDATE_Received_OnStream_Respected()
        {
            var initialWindowSize = _helloWorldBytes.Length / 2;

            // This only affects the stream windows. The connection-level window is always initialized at 64KiB.
            _clientSettings.InitialWindowSize = (uint)initialWindowSize;

            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendDataAsync(1, _helloWorldBytes, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            var dataFrame1 = await ExpectAsync(Http2FrameType.DATA,
                withLength: initialWindowSize,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            await SendWindowUpdateAsync(1, initialWindowSize);

            var dataFrame2 = await ExpectAsync(Http2FrameType.DATA,
                withLength: initialWindowSize,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);

            Assert.Equal(new ArraySegment<byte>(_helloWorldBytes, 0, initialWindowSize), dataFrame1.DataPayload);
            Assert.Equal(new ArraySegment<byte>(_helloWorldBytes, initialWindowSize, initialWindowSize), dataFrame2.DataPayload);
        }

        [Fact]
        public async Task WINDOW_UPDATE_Received_OnStream_Respected_WhenInitialWindowSizeReducedMidStream()
        {
            // This only affects the stream windows. The connection-level window is always initialized at 64KiB.
            _clientSettings.InitialWindowSize = 6;

            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);
            await SendDataAsync(1, _helloWorldBytes, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);

            var dataFrame1 = await ExpectAsync(Http2FrameType.DATA,
                withLength: 6,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            // Reduce the initial window size for response data by 3 bytes.
            _clientSettings.InitialWindowSize = 3;
            await SendSettingsAsync();

            await ExpectAsync(Http2FrameType.SETTINGS,
                withLength: 0,
                withFlags: (byte)Http2SettingsFrameFlags.ACK,
                withStreamId: 0);

            await SendWindowUpdateAsync(1, 6);

            var dataFrame2 = await ExpectAsync(Http2FrameType.DATA,
                withLength: 3,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            await SendWindowUpdateAsync(1, 3);

            var dataFrame3 = await ExpectAsync(Http2FrameType.DATA,
                withLength: 3,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);

            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);

            Assert.Equal(new ArraySegment<byte>(_helloWorldBytes, 0, 6), dataFrame1.DataPayload);
            Assert.Equal(new ArraySegment<byte>(_helloWorldBytes, 6, 3), dataFrame2.DataPayload);
            Assert.Equal(new ArraySegment<byte>(_helloWorldBytes, 9, 3), dataFrame3.DataPayload);
        }

        [Fact]
        public async Task CONTINUATION_Received_Decoded()
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            await StartStreamAsync(1, _twoContinuationsRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2HeadersFrameFlags.END_STREAM,
                withStreamId: 1);

            VerifyDecodedRequestHeaders(_twoContinuationsRequestHeaders);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CONTINUATION_Received_WithTrailers_Decoded(bool sendData)
        {
            await InitializeConnectionAsync(_readTrailersApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS, _browserRequestHeaders);

            // Initialize another stream with a higher stream ID, and verify that after trailers are
            // decoded by the other stream, the highest opened stream ID is not reset to the lower ID
            // (the highest opened stream ID is sent by the server in the GOAWAY frame when shutting
            // down the connection).
            await SendHeadersAsync(3, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _browserRequestHeaders);

            // The second stream should end first, since the first one is waiting for the request body.
            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 3);

            if (sendData)
            {
                await SendDataAsync(1, _helloBytes, endStream: false);
            }

            // Trailers encoded as Literal Header Field without Indexing - New Name
            //   trailer-1: 1
            //   trailer-2: 2
            var trailers = new byte[] { 0x00, 0x09 }
                .Concat(Encoding.ASCII.GetBytes("trailer-1"))
                .Concat(new byte[] { 0x01, (byte)'1' })
                .Concat(new byte[] { 0x00, 0x09 })
                .Concat(Encoding.ASCII.GetBytes("trailer-2"))
                .Concat(new byte[] { 0x01, (byte)'2' })
                .ToArray();
            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_STREAM, new byte[0]);
            await SendContinuationAsync(1, Http2ContinuationFrameFlags.END_HEADERS, trailers);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            VerifyDecodedRequestHeaders(_browserRequestHeaders.Concat(new[]
            {
                new KeyValuePair<string, string>("trailer-1", "1"),
                new KeyValuePair<string, string>("trailer-2", "2")
            }));

            await StopConnectionAsync(expectedLastStreamId: 3, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task CONTINUATION_Received_StreamIdMismatch_ConnectionError()
        {
            await InitializeConnectionAsync(_readHeadersApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _oneContinuationRequestHeaders);
            await SendContinuationAsync(3, Http2ContinuationFrameFlags.END_HEADERS);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorHeadersInterleaved(Http2FrameType.CONTINUATION, streamId: 3, headersStreamId: 1));
        }

        [Fact]
        public async Task CONTINUATION_Received_IncompleteHeaderBlock_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _postRequestHeaders);
            await SendIncompleteContinuationFrameAsync(streamId: 1);

            await WaitForConnectionErrorAsync<HPackDecodingException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.COMPRESSION_ERROR,
                expectedErrorMessage: CoreStrings.HPackErrorIncompleteHeaderBlock);
        }

        [Theory]
        [MemberData(nameof(IllegalTrailerData))]
        public async Task CONTINUATION_Received_WithTrailers_ContainsIllegalTrailer_ConnectionError(byte[] trailers, string expectedErrorMessage)
        {
            await InitializeConnectionAsync(_readTrailersApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS, _browserRequestHeaders);
            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_STREAM, new byte[0]);
            await SendContinuationAsync(1, Http2ContinuationFrameFlags.END_HEADERS, trailers);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: expectedErrorMessage);
        }

        [Theory]
        [MemberData(nameof(MissingPseudoHeaderFieldData))]
        public async Task CONTINUATION_Received_HeaderBlockDoesNotContainMandatoryPseudoHeaderField_StreamError(IEnumerable<KeyValuePair<string, string>> headers)
        {
            await InitializeConnectionAsync(_noopApplication);

            Assert.True(await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, headers));
            await SendEmptyContinuationFrameAsync(1, Http2ContinuationFrameFlags.END_HEADERS);

            await WaitForStreamErrorAsync(
                expectedStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.Http2ErrorMissingMandatoryPseudoHeaderFields);

            // Verify that the stream ID can't be re-used
            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS, headers);
            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.STREAM_CLOSED,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamClosed(Http2FrameType.HEADERS, streamId: 1));
        }

        [Theory]
        [MemberData(nameof(ConnectMissingPseudoHeaderFieldData))]
        public async Task CONTINUATION_Received_HeaderBlockDoesNotContainMandatoryPseudoHeaderField_MethodIsCONNECT_NoError(IEnumerable<KeyValuePair<string, string>> headers)
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_STREAM, headers);
            await SendEmptyContinuationFrameAsync(1, Http2ContinuationFrameFlags.END_HEADERS);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2HeadersFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task CONTINUATION_Sent_WhenHeadersLargerThanFrameLength()
        {
            await InitializeConnectionAsync(_largeHeadersApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            var headersFrame = await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 12361,
                withFlags: (byte)Http2HeadersFrameFlags.NONE,
                withStreamId: 1);
            var continuationFrame1 = await ExpectAsync(Http2FrameType.CONTINUATION,
                withLength: 12306,
                withFlags: (byte)Http2ContinuationFrameFlags.NONE,
                withStreamId: 1);
            var continuationFrame2 = await ExpectAsync(Http2FrameType.CONTINUATION,
                withLength: 8204,
                withFlags: (byte)Http2ContinuationFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await StopConnectionAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);

            _hpackDecoder.Decode(headersFrame.HeadersPayload, endHeaders: false, handler: this);
            _hpackDecoder.Decode(continuationFrame1.HeadersPayload, endHeaders: false, handler: this);
            _hpackDecoder.Decode(continuationFrame2.HeadersPayload, endHeaders: true, handler: this);

            Assert.Equal(11, _decodedHeaders.Count);
            Assert.Contains("date", _decodedHeaders.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("200", _decodedHeaders[HeaderNames.Status]);
            Assert.Equal("0", _decodedHeaders["content-length"]);
            Assert.Equal(_largeHeaderValue, _decodedHeaders["a"]);
            Assert.Equal(_largeHeaderValue, _decodedHeaders["b"]);
            Assert.Equal(_largeHeaderValue, _decodedHeaders["c"]);
            Assert.Equal(_largeHeaderValue, _decodedHeaders["d"]);
            Assert.Equal(_largeHeaderValue, _decodedHeaders["e"]);
            Assert.Equal(_largeHeaderValue, _decodedHeaders["f"]);
            Assert.Equal(_largeHeaderValue, _decodedHeaders["g"]);
            Assert.Equal(_largeHeaderValue, _decodedHeaders["h"]);
        }

        [Fact]
        public async Task UnknownFrameType_Received_Ignored()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendUnknownFrameTypeAsync(streamId: 1, frameType: 42);

            // Check that the connection is still alive
            await SendPingAsync(Http2PingFrameFlags.NONE);
            await ExpectAsync(Http2FrameType.PING,
                withLength: 8,
                withFlags: (byte)Http2PingFrameFlags.ACK,
                withStreamId: 0);

            await StopConnectionAsync(0, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task UnknownFrameType_Received_InterleavedWithHeaders_ConnectionError()
        {
            await InitializeConnectionAsync(_noopApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.NONE, _browserRequestHeaders);
            await SendUnknownFrameTypeAsync(streamId: 1, frameType: 42);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 1,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorHeadersInterleaved(frameType: 42, streamId: 1, headersStreamId: 1));
        }

        [Fact]
        public async Task ConnectionErrorAbortsAllStreams()
        {
            await InitializeConnectionAsync(_waitForAbortApplication);

            // Start some streams
            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);
            await StartStreamAsync(3, _browserRequestHeaders, endStream: true);
            await StartStreamAsync(5, _browserRequestHeaders, endStream: true);

            // Cause a connection error by sending an invalid frame
            await SendDataAsync(0, _noData, endStream: false);

            await WaitForConnectionErrorAsync<Http2ConnectionErrorException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 5,
                expectedErrorCode: Http2ErrorCode.PROTOCOL_ERROR,
                expectedErrorMessage: CoreStrings.FormatHttp2ErrorStreamIdZero(Http2FrameType.DATA));

            await WaitForAllStreamsAsync();
            Assert.Contains(1, _abortedStreamIds);
            Assert.Contains(3, _abortedStreamIds);
            Assert.Contains(5, _abortedStreamIds);
        }

        [Fact]
        public async Task ConnectionResetLoggedWithActiveStreams()
        {
            await InitializeConnectionAsync(_waitForAbortApplication);

            await SendHeadersAsync(1, Http2HeadersFrameFlags.END_HEADERS | Http2HeadersFrameFlags.END_STREAM, _browserRequestHeaders);

            _pair.Application.Output.Complete(new ConnectionResetException(string.Empty));

            await StopConnectionAsync(1, ignoreNonGoAwayFrames: false);
            Assert.Single(TestApplicationErrorLogger.Messages, m => m.Exception is ConnectionResetException);
        }

        [Fact]
        public async Task ConnectionResetNotLoggedWithNoActiveStreams()
        {
            await InitializeConnectionAsync(_waitForAbortApplication);

            _pair.Application.Output.Complete(new ConnectionResetException(string.Empty));

            var result = await _pair.Application.Input.ReadAsync();
            Assert.True(result.IsCompleted);
            Assert.DoesNotContain(TestApplicationErrorLogger.Messages, m => m.Exception is ConnectionResetException);
        }

        [Fact]
        public async Task OnInputOrOutputCompletedSendsFinalGOAWAY()
        {
            await InitializeConnectionAsync(_noopApplication);

            _connection.OnInputOrOutputCompleted();
            await _closedStateReached.Task.DefaultTimeout();

            VerifyGoAway(await ReceiveFrameAsync(), 0, Http2ErrorCode.NO_ERROR);
        }

        [Fact]
        public async Task AbortSendsFinalGOAWAY()
        {
            await InitializeConnectionAsync(_noopApplication);

            _connection.Abort(new ConnectionAbortedException());
            await _closedStateReached.Task.DefaultTimeout();

            VerifyGoAway(await ReceiveFrameAsync(), 0, Http2ErrorCode.INTERNAL_ERROR);
        }

        [Fact]
        public async Task CompletionSendsFinalGOAWAY()
        {
            await InitializeConnectionAsync(_noopApplication);

            // Completes ProcessRequestsAsync
            _pair.Application.Output.Complete();
            await _closedStateReached.Task.DefaultTimeout();

            VerifyGoAway(await ReceiveFrameAsync(), 0, Http2ErrorCode.NO_ERROR);
        }

        [Fact]
        public async Task StopProcessingNextRequestSendsGracefulGOAWAYAndWaitsForStreamsToComplete()
        {
            var task = Task.CompletedTask;
            await InitializeConnectionAsync(context => task);

            // Send and receive an unblocked request
            await StartStreamAsync(1, _browserRequestHeaders, endStream: true);

            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 55,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            // Send a blocked request
            var tcs = new TaskCompletionSource<object>(TaskContinuationOptions.RunContinuationsAsynchronously);
            task = tcs.Task;
            await StartStreamAsync(3, _browserRequestHeaders, endStream: false);

            // Close pipe
            _pair.Application.Output.Complete();

            // Assert connection closed
            await _closedStateReached.Task.DefaultTimeout();
            VerifyGoAway(await ReceiveFrameAsync(), 3, Http2ErrorCode.NO_ERROR);

            // Assert connection shutdown is still blocked
            // ProcessRequestsAsync completes the connection's Input pipe
            var readTask = _pair.Application.Input.ReadAsync();
            _pair.Application.Input.CancelPendingRead();
            var result = await readTask;
            Assert.False(result.IsCompleted);

            // Unblock the request and ProcessRequestsAsync
            tcs.TrySetResult(null);
            await _connectionTask;

            // Assert connection's Input pipe is completed
            readTask = _pair.Application.Input.ReadAsync();
            _pair.Application.Input.CancelPendingRead();
            result = await readTask;
            Assert.True(result.IsCompleted);
        }

        [Fact]
        public async Task StopProcessingNextRequestSendsGracefulGOAWAYThenFinalGOAWAYWhenAllStreamsComplete()
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);

            _connection.StopProcessingNextRequest();
            await _closingStateReached.Task.DefaultTimeout();

            VerifyGoAway(await ReceiveFrameAsync(), Int32.MaxValue, Http2ErrorCode.NO_ERROR);

            await SendDataAsync(1, _helloBytes, true);
            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);

            await _closedStateReached.Task.DefaultTimeout();
            VerifyGoAway(await ReceiveFrameAsync(), 1, Http2ErrorCode.NO_ERROR);
        }

        [Fact]
        public async Task AcceptNewStreamsDuringClosingConnection()
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);

            _connection.StopProcessingNextRequest();
            VerifyGoAway(await ReceiveFrameAsync(), Int32.MaxValue, Http2ErrorCode.NO_ERROR);

            await _closingStateReached.Task.DefaultTimeout();

            await StartStreamAsync(3, _browserRequestHeaders, endStream: false);

            await SendDataAsync(1, _helloBytes, true);
            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 1);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 1);
            await SendDataAsync(3, _helloBytes, true);
            await ExpectAsync(Http2FrameType.HEADERS,
                withLength: 37,
                withFlags: (byte)Http2HeadersFrameFlags.END_HEADERS,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 5,
                withFlags: (byte)Http2DataFrameFlags.NONE,
                withStreamId: 3);
            await ExpectAsync(Http2FrameType.DATA,
                withLength: 0,
                withFlags: (byte)Http2DataFrameFlags.END_STREAM,
                withStreamId: 3);

            await WaitForConnectionStopAsync(expectedLastStreamId: 3, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public async Task IgnoreNewStreamsDuringClosedConnection()
        {
            await InitializeConnectionAsync(_echoApplication);

            await StartStreamAsync(1, _browserRequestHeaders, endStream: false);

            _connection.OnInputOrOutputCompleted();
            await _closedStateReached.Task.DefaultTimeout();

            await StartStreamAsync(3, _browserRequestHeaders, endStream: false);

            await WaitForConnectionStopAsync(expectedLastStreamId: 1, ignoreNonGoAwayFrames: false);
        }

        [Fact]
        public void IOExceptionDuringFrameProcessingLoggedAsInfo()
        {
            var ioException = new IOException();
            _pair.Application.Output.Complete(ioException);

            Assert.Equal(TaskStatus.RanToCompletion, _connection.ProcessRequestsAsync(new DummyApplication(_noopApplication)).Status);

            var logMessage = TestApplicationErrorLogger.Messages.Single(m => m.LogLevel >= LogLevel.Information);

            Assert.Equal(LogLevel.Information, logMessage.LogLevel);
            Assert.Equal("Connection id \"(null)\" request processing ended abnormally.", logMessage.Message);
            Assert.Same(ioException, logMessage.Exception);
        }

        [Fact]
        public void UnexpectedExceptionDuringFrameProcessingLoggedAWarning()
        {
            var exception = new Exception();
            _pair.Application.Output.Complete(exception);

            Assert.Equal(TaskStatus.RanToCompletion, _connection.ProcessRequestsAsync(new DummyApplication(_noopApplication)).Status);

            var logMessage = TestApplicationErrorLogger.Messages.Single(m => m.LogLevel >= LogLevel.Information);

            Assert.Equal(LogLevel.Warning, logMessage.LogLevel);
            Assert.Equal(CoreStrings.RequestProcessingEndError, logMessage.Message);
            Assert.Same(exception, logMessage.Exception);
        }

        public static TheoryData<byte[]> UpperCaseHeaderNameData
        {
            get
            {
                // We can't use HPackEncoder here because it will convert header names to lowercase
                var headerName = "abcdefghijklmnopqrstuvwxyz";

                var headerBlockStart = new byte[]
                {
                    0x82,                    // Indexed Header Field - :method: GET
                    0x84,                    // Indexed Header Field - :path: /
                    0x86,                    // Indexed Header Field - :scheme: http
                    0x00,                    // Literal Header Field without Indexing - New Name
                    (byte)headerName.Length, // Header name length
                };

                var headerBlockEnd = new byte[]
                {
                    0x01, // Header value length
                    0x30  // "0"
                };

                var data = new TheoryData<byte[]>();

                for (var i = 0; i < headerName.Length; i++)
                {
                    var bytes = Encoding.ASCII.GetBytes(headerName);
                    bytes[i] &= 0xdf;

                    var headerBlock = headerBlockStart.Concat(bytes).Concat(headerBlockEnd).ToArray();
                    data.Add(headerBlock);
                }

                return data;
            }
        }

        public static TheoryData<IEnumerable<KeyValuePair<string, string>>> DuplicatePseudoHeaderFieldData
        {
            get
            {
                var data = new TheoryData<IEnumerable<KeyValuePair<string, string>>>();
                var requestHeaders = new[]
                {
                    new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                    new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                    new KeyValuePair<string, string>(HeaderNames.Authority, "127.0.0.1"),
                    new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                };

                foreach (var headerField in requestHeaders)
                {
                    var headers = requestHeaders.Concat(new[] { new KeyValuePair<string, string>(headerField.Key, headerField.Value) });
                    data.Add(headers);
                }

                return data;
            }
        }

        public static TheoryData<IEnumerable<KeyValuePair<string, string>>> MissingPseudoHeaderFieldData
        {
            get
            {
                var data = new TheoryData<IEnumerable<KeyValuePair<string, string>>>();
                var requestHeaders = new[]
                {
                    new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                    new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                    new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                };

                foreach (var headerField in requestHeaders)
                {
                    var headers = requestHeaders.Except(new[] { headerField });
                    data.Add(headers);
                }

                return data;
            }
        }

        public static TheoryData<IEnumerable<KeyValuePair<string, string>>> ConnectMissingPseudoHeaderFieldData
        {
            get
            {
                var data = new TheoryData<IEnumerable<KeyValuePair<string, string>>>();
                var methodHeader = new KeyValuePair<string, string>(HeaderNames.Method, "CONNECT");
                var headers = new[] { methodHeader };
                data.Add(headers);

                return data;
            }
        }

        public static TheoryData<IEnumerable<KeyValuePair<string, string>>> PseudoHeaderFieldAfterRegularHeadersData
        {
            get
            {
                var data = new TheoryData<IEnumerable<KeyValuePair<string, string>>>();
                var requestHeaders = new[]
                {
                    new KeyValuePair<string, string>(HeaderNames.Method, "GET"),
                    new KeyValuePair<string, string>(HeaderNames.Path, "/"),
                    new KeyValuePair<string, string>(HeaderNames.Authority, "127.0.0.1"),
                    new KeyValuePair<string, string>(HeaderNames.Scheme, "http"),
                    new KeyValuePair<string, string>("content-length", "0")
                };

                foreach (var headerField in requestHeaders.Where(h => h.Key.StartsWith(":")))
                {
                    var headers = requestHeaders.Except(new[] { headerField }).Concat(new[] { headerField });
                    data.Add(headers);
                }

                return data;
            }
        }

        public static TheoryData<byte[], string> IllegalTrailerData
        {
            get
            {
                // We can't use HPackEncoder here because it will convert header names to lowercase
                var data = new TheoryData<byte[], string>();

                // Indexed Header Field - :method: GET
                data.Add(new byte[] { 0x82 }, CoreStrings.Http2ErrorTrailersContainPseudoHeaderField);

                // Indexed Header Field - :path: /
                data.Add(new byte[] { 0x84 }, CoreStrings.Http2ErrorTrailersContainPseudoHeaderField);

                // Indexed Header Field - :scheme: http
                data.Add(new byte[] { 0x86 }, CoreStrings.Http2ErrorTrailersContainPseudoHeaderField);

                // Literal Header Field without Indexing - Indexed Name - :authority: 127.0.0.1
                data.Add(new byte[] { 0x01, 0x09 }.Concat(Encoding.ASCII.GetBytes("127.0.0.1")).ToArray(), CoreStrings.Http2ErrorTrailersContainPseudoHeaderField);

                // Literal Header Field without Indexing - New Name - contains-Uppercase: 0
                data.Add(new byte[] { 0x00, 0x12 }
                    .Concat(Encoding.ASCII.GetBytes("contains-Uppercase"))
                    .Concat(new byte[] { 0x01, (byte)'0' })
                    .ToArray(), CoreStrings.Http2ErrorTrailerNameUppercase);

                return data;
            }
        }
    }
}
