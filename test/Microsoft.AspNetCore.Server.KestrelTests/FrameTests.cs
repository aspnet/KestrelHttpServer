// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class FrameTests : IDisposable
    {
        private readonly IPipe _input;
        private readonly TestFrame<object> _frame;
        private readonly ServiceContext _serviceContext;
        private readonly ConnectionContext _connectionContext;
        private readonly PipeFactory _pipelineFactory;
        private ReadCursor _consumed;
        private ReadCursor _examined;

        private class TestFrame<TContext> : Frame<TContext>
        {
            public TestFrame(IHttpApplication<TContext> application, ConnectionContext context)
            : base(application, context)
            {
            }

            public Task ProduceEndAsync()
            {
                return ProduceEnd();
            }
        }

        public FrameTests()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            _pipelineFactory = new PipeFactory();
            _input = _pipelineFactory.Create();

            _serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions(),
                HttpParserFactory = frame => new KestrelHttpParser(trace),
                Log = trace
            };
            var listenerContext = new ListenerContext(_serviceContext)
            {
                ListenOptions = new ListenOptions(new IPEndPoint(IPAddress.Loopback, 5000))
            };
            _connectionContext = new ConnectionContext(listenerContext)
            {
                Input = _input,
                Output = new MockSocketOutput(),
                ConnectionControl = Mock.Of<IConnectionControl>()
            };

            _frame = new TestFrame<object>(application: null, context: _connectionContext);
            _frame.Reset();
            _frame.InitializeHeaders();
        }

        public void Dispose()
        {
            _input.Reader.Complete();
            _input.Writer.Complete();
            _pipelineFactory.Dispose();
        }

        [Fact]
        public async Task TakeMessageHeadersThrowsWhenHeadersExceedTotalSizeLimit()
        {
            const string headerLine = "Header: value\r\n";
            _serviceContext.ServerOptions.Limits.MaxRequestHeadersTotalSize = headerLine.Length - 1;
            _frame.Reset();

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"{headerLine}\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() => _frame.TakeMessageHeaders(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal("Request headers too long.", exception.Message);
            Assert.Equal(StatusCodes.Status431RequestHeaderFieldsTooLarge, exception.StatusCode);
        }

        [Fact]
        public async Task TakeMessageHeadersThrowsWhenHeadersExceedCountLimit()
        {
            const string headerLines = "Header-1: value1\r\nHeader-2: value2\r\n";
            _serviceContext.ServerOptions.Limits.MaxRequestHeaderCount = 1;

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"{headerLines}\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() => _frame.TakeMessageHeaders(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal("Request contains too many headers.", exception.Message);
            Assert.Equal(StatusCodes.Status431RequestHeaderFieldsTooLarge, exception.StatusCode);
        }

        [Fact]
        public void ResetResetsScheme()
        {
            _frame.Scheme = "https";

            // Act
            _frame.Reset();

            // Assert
            Assert.Equal("http", ((IFeatureCollection)_frame).Get<IHttpRequestFeature>().Scheme);
        }

        [Fact]
        public async Task ResetResetsHeaderLimits()
        {
            const string headerLine1 = "Header-1: value1\r\n";
            const string headerLine2 = "Header-2: value2\r\n";

            var options = new KestrelServerOptions();
            options.Limits.MaxRequestHeadersTotalSize = headerLine1.Length;
            options.Limits.MaxRequestHeaderCount = 1;
            _serviceContext.ServerOptions = options;

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"{headerLine1}\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var takeMessageHeaders = _frame.TakeMessageHeaders(readableBuffer, out _consumed, out _examined);
            _input.Reader.Advance(_consumed, _examined);

            Assert.True(takeMessageHeaders);
            Assert.Equal(1, _frame.RequestHeaders.Count);
            Assert.Equal("value1", _frame.RequestHeaders["Header-1"]);

            _frame.Reset();

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"{headerLine2}\r\n"));
            readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            takeMessageHeaders = _frame.TakeMessageHeaders(readableBuffer, out _consumed, out _examined);
            _input.Reader.Advance(_consumed, _examined);

            Assert.True(takeMessageHeaders);
            Assert.Equal(1, _frame.RequestHeaders.Count);
            Assert.Equal("value2", _frame.RequestHeaders["Header-2"]);
        }

        [Fact]
        public void ThrowsWhenStatusCodeIsSetAfterResponseStarted()
        {
            // Act
            _frame.Write(new ArraySegment<byte>(new byte[1]));

            // Assert
            Assert.True(_frame.HasResponseStarted);
            Assert.Throws<InvalidOperationException>(() => ((IHttpResponseFeature)_frame).StatusCode = StatusCodes.Status404NotFound);
        }

        [Fact]
        public void ThrowsWhenReasonPhraseIsSetAfterResponseStarted()
        {
            // Act
            _frame.Write(new ArraySegment<byte>(new byte[1]));

            // Assert
            Assert.True(_frame.HasResponseStarted);
            Assert.Throws<InvalidOperationException>(() => ((IHttpResponseFeature)_frame).ReasonPhrase = "Reason phrase");
        }

        [Fact]
        public void ThrowsWhenOnStartingIsSetAfterResponseStarted()
        {
            _frame.Write(new ArraySegment<byte>(new byte[1]));

            // Act/Assert
            Assert.True(_frame.HasResponseStarted);
            Assert.Throws<InvalidOperationException>(() => ((IHttpResponseFeature)_frame).OnStarting(_ => TaskCache.CompletedTask, null));
        }

        [Fact]
        public void InitializeHeadersResetsRequestHeaders()
        {
            // Arrange
            var originalRequestHeaders = _frame.RequestHeaders;
            _frame.RequestHeaders = new FrameRequestHeaders();

            // Act
            _frame.InitializeHeaders();

            // Assert
            Assert.Same(originalRequestHeaders, _frame.RequestHeaders);
        }

        [Fact]
        public void InitializeHeadersResetsResponseHeaders()
        {
            // Arrange
            var originalResponseHeaders = _frame.ResponseHeaders;
            _frame.ResponseHeaders = new FrameResponseHeaders();

            // Act
            _frame.InitializeHeaders();

            // Assert
            Assert.Same(originalResponseHeaders, _frame.ResponseHeaders);
        }

        [Fact]
        public void InitializeStreamsResetsStreams()
        {
            // Arrange
            var messageBody = MessageBody.For(Kestrel.Internal.Http.HttpVersion.Http11, (FrameRequestHeaders)_frame.RequestHeaders, _frame);
            _frame.InitializeStreams(messageBody);

            var originalRequestBody = _frame.RequestBody;
            var originalResponseBody = _frame.ResponseBody;
            var originalDuplexStream = _frame.DuplexStream;
            _frame.RequestBody = new MemoryStream();
            _frame.ResponseBody = new MemoryStream();
            _frame.DuplexStream = new MemoryStream();

            // Act
            _frame.InitializeStreams(messageBody);

            // Assert
            Assert.Same(originalRequestBody, _frame.RequestBody);
            Assert.Same(originalResponseBody, _frame.ResponseBody);
            Assert.Same(originalDuplexStream, _frame.DuplexStream);
        }

        [Theory]
        [MemberData(nameof(RequestLineValidData))]
        public async Task TakeStartLineSetsFrameProperties(
            string requestLine,
            string expectedMethod,
            string expectedRawTarget,
            string expectedRawPath,
            string expectedDecodedPath,
            string expectedQueryString,
            string expectedHttpVersion)
        {
            var requestLineBytes = Encoding.ASCII.GetBytes(requestLine);
            await _input.Writer.WriteAsync(requestLineBytes);
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var returnValue = _frame.TakeStartLine(readableBuffer, out _consumed, out _examined);
            _input.Reader.Advance(_consumed, _examined);

            Assert.True(returnValue);
            Assert.Equal(expectedMethod, _frame.Method);
            Assert.Equal(expectedRawTarget, _frame.RawTarget);
            Assert.Equal(expectedDecodedPath, _frame.Path);
            Assert.Equal(expectedQueryString, _frame.QueryString);
            Assert.Equal(expectedHttpVersion, _frame.HttpVersion);
        }

        [Theory]
        [MemberData(nameof(RequestLineDotSegmentData))]
        public async Task TakeStartLineRemovesDotSegmentsFromTarget(
            string requestLine,
            string expectedRawTarget,
            string expectedDecodedPath,
            string expectedQueryString)
        {
            var requestLineBytes = Encoding.ASCII.GetBytes(requestLine);
            await _input.Writer.WriteAsync(requestLineBytes);
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var returnValue = _frame.TakeStartLine(readableBuffer, out _consumed, out _examined);
            _input.Reader.Advance(_consumed, _examined);

            Assert.True(returnValue);
            Assert.Equal(expectedRawTarget, _frame.RawTarget);
            Assert.Equal(expectedDecodedPath, _frame.Path);
            Assert.Equal(expectedQueryString, _frame.QueryString);
        }

        [Fact]
        public async Task ParseRequestStartsRequestHeadersTimeoutOnFirstByteAvailable()
        {
            var connectionControl = new Mock<IConnectionControl>();
            _connectionContext.ConnectionControl = connectionControl.Object;

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes("G"));

            _frame.ParseRequest((await _input.Reader.ReadAsync()).Buffer, out _consumed, out _examined);
            _input.Reader.Advance(_consumed, _examined);

            var expectedRequestHeadersTimeout = (long)_serviceContext.ServerOptions.Limits.RequestHeadersTimeout.TotalMilliseconds;
            connectionControl.Verify(cc => cc.ResetTimeout(expectedRequestHeadersTimeout, TimeoutAction.SendTimeoutResponse));
        }

        [Fact]
        public async Task TakeStartLineThrowsWhenTooLong()
        {
            _serviceContext.ServerOptions.Limits.MaxRequestLineSize = "GET / HTTP/1.1\r\n".Length;

            var requestLineBytes = Encoding.ASCII.GetBytes("GET /a HTTP/1.1\r\n");
            await _input.Writer.WriteAsync(requestLineBytes);

            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;
            var exception = Assert.Throws<BadHttpRequestException>(() => _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal("Request line too long.", exception.Message);
            Assert.Equal(StatusCodes.Status414UriTooLong, exception.StatusCode);
        }

        [Theory]
        [MemberData(nameof(TargetWithEncodedNullCharData))]
        public async Task TakeStartLineThrowsOnEncodedNullCharInTarget(string target)
        {
            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal($"Invalid request target: '{target}'", exception.Message);
        }

        [Theory]
        [MemberData(nameof(TargetWithNullCharData))]
        public async Task TakeStartLineThrowsOnNullCharInTarget(string target)
        {
            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal($"Invalid request target: '{target.EscapeNonPrintable()}'", exception.Message);
        }

        [Theory]
        [MemberData(nameof(MethodWithNullCharData))]
        public async Task TakeStartLineThrowsOnNullCharInMethod(string method)
        {
            var requestLine = $"{method} / HTTP/1.1\r\n";

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes(requestLine));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal($"Invalid request line: '{requestLine.EscapeNonPrintable()}'", exception.Message);
        }

        [Theory]
        [MemberData(nameof(QueryStringWithNullCharData))]
        public async Task TakeStartLineThrowsOnNullCharInQueryString(string queryString)
        {
            var target = $"/{queryString}";

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\n"));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal($"Invalid request target: '{target.EscapeNonPrintable()}'", exception.Message);
        }

        [Theory]
        [MemberData(nameof(TargetInvalidData))]
        public async Task TakeStartLineThrowsWhenRequestTargetIsInvalid(string method, string target)
        {
            var requestLine = $"{method} {target} HTTP/1.1\r\n";

            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes(requestLine));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal($"Invalid request target: '{target.EscapeNonPrintable()}'", exception.Message);
        }

        [Theory]
        [MemberData(nameof(MethodNotAllowedTargetData))]
        public async Task TakeStartLineThrowsWhenMethodNotAllowed(string requestLine, HttpMethod allowedMethod)
        {
            await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes(requestLine));
            var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

            var exception = Assert.Throws<BadHttpRequestException>(() =>
                _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
            _input.Reader.Advance(_consumed, _examined);

            Assert.Equal(405, exception.StatusCode);
            Assert.Equal("Method not allowed.", exception.Message);
            Assert.Equal(HttpUtilities.MethodToString(allowedMethod), exception.AllowedHeader);
        }

        [Fact]
        public void RequestProcessingAsyncEnablesKeepAliveTimeout()
        {
            var connectionControl = new Mock<IConnectionControl>();
            _connectionContext.ConnectionControl = connectionControl.Object;

            var requestProcessingTask = _frame.RequestProcessingAsync();

            var expectedKeepAliveTimeout = (long)_serviceContext.ServerOptions.Limits.KeepAliveTimeout.TotalMilliseconds;
            connectionControl.Verify(cc => cc.SetTimeout(expectedKeepAliveTimeout, TimeoutAction.CloseConnection));

            _frame.StopAsync();
            _input.Writer.Complete();

            requestProcessingTask.Wait();
        }

        [Fact]
        public void WriteThrowsForNonBodyResponse()
        {
            // Arrange
            ((IHttpResponseFeature)_frame).StatusCode = StatusCodes.Status304NotModified;

            // Act/Assert
            Assert.Throws<InvalidOperationException>(() => _frame.Write(new ArraySegment<byte>(new byte[1])));
        }

        [Fact]
        public async Task WriteAsyncThrowsForNonBodyResponse()
        {
            // Arrange
            _frame.HttpVersion = "HTTP/1.1";
            ((IHttpResponseFeature)_frame).StatusCode = StatusCodes.Status304NotModified;

            // Act/Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => _frame.WriteAsync(new ArraySegment<byte>(new byte[1]), default(CancellationToken)));
        }

        [Fact]
        public void WriteDoesNotThrowForHeadResponse()
        {
            // Arrange
            _frame.HttpVersion = "HTTP/1.1";
            ((IHttpRequestFeature)_frame).Method = "HEAD";

            // Act/Assert
            _frame.Write(new ArraySegment<byte>(new byte[1]));
        }

        [Fact]
        public async Task WriteAsyncDoesNotThrowForHeadResponse()
        {
            // Arrange
            _frame.HttpVersion = "HTTP/1.1";
            ((IHttpRequestFeature)_frame).Method = "HEAD";

            // Act/Assert
            await _frame.WriteAsync(new ArraySegment<byte>(new byte[1]), default(CancellationToken));
        }

        [Fact]
        public void ManuallySettingTransferEncodingThrowsForHeadResponse()
        {
            // Arrange
            _frame.HttpVersion = "HTTP/1.1";
            ((IHttpRequestFeature)_frame).Method = "HEAD";

            // Act
            _frame.ResponseHeaders.Add("Transfer-Encoding", "chunked");

            // Assert
            Assert.Throws<InvalidOperationException>(() => _frame.Flush());
        }

        [Fact]
        public void ManuallySettingTransferEncodingThrowsForNoBodyResponse()
        {
            // Arrange
            _frame.HttpVersion = "HTTP/1.1";
            ((IHttpResponseFeature)_frame).StatusCode = StatusCodes.Status304NotModified;

            // Act
            _frame.ResponseHeaders.Add("Transfer-Encoding", "chunked");

            // Assert
            Assert.Throws<InvalidOperationException>(() => _frame.Flush());
        }

        [Fact]
        public async Task RequestProcessingTaskIsUnwrapped()
        {
            _frame.Start();

            var data = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\n");
            await _input.Writer.WriteAsync(data);

            var requestProcessingTask = _frame.StopAsync();
            Assert.IsNotType(typeof(Task<Task>), requestProcessingTask);

            await requestProcessingTask.TimeoutAfter(TimeSpan.FromSeconds(10));
            _input.Writer.Complete();
        }

        [Fact]
        public void RequestAbortedTokenIsResetBeforeLastWriteWithContentLength()
        {
            _frame.ResponseHeaders["Content-Length"] = "12";

            // Need to compare WaitHandle ref since CancellationToken is struct
            var original = _frame.RequestAborted.WaitHandle;

            foreach (var ch in "hello, worl")
            {
                _frame.Write(new ArraySegment<byte>(new[] { (byte)ch }));
                Assert.Same(original, _frame.RequestAborted.WaitHandle);
            }

            _frame.Write(new ArraySegment<byte>(new[] { (byte)'d' }));
            Assert.NotSame(original, _frame.RequestAborted.WaitHandle);
        }

        [Fact]
        public async Task RequestAbortedTokenIsResetBeforeLastWriteAsyncWithContentLength()
        {
            _frame.ResponseHeaders["Content-Length"] = "12";

            // Need to compare WaitHandle ref since CancellationToken is struct
            var original = _frame.RequestAborted.WaitHandle;

            foreach (var ch in "hello, worl")
            {
                await _frame.WriteAsync(new ArraySegment<byte>(new[] { (byte)ch }), default(CancellationToken));
                Assert.Same(original, _frame.RequestAborted.WaitHandle);
            }

            await _frame.WriteAsync(new ArraySegment<byte>(new[] { (byte)'d' }), default(CancellationToken));
            Assert.NotSame(original, _frame.RequestAborted.WaitHandle);
        }

        [Fact]
        public async Task RequestAbortedTokenIsResetBeforeLastWriteAsyncAwaitedWithContentLength()
        {
            _frame.ResponseHeaders["Content-Length"] = "12";

            // Need to compare WaitHandle ref since CancellationToken is struct
            var original = _frame.RequestAborted.WaitHandle;

            foreach (var ch in "hello, worl")
            {
                await _frame.WriteAsyncAwaited(new ArraySegment<byte>(new[] { (byte)ch }), default(CancellationToken));
                Assert.Same(original, _frame.RequestAborted.WaitHandle);
            }

            await _frame.WriteAsyncAwaited(new ArraySegment<byte>(new[] { (byte)'d' }), default(CancellationToken));
            Assert.NotSame(original, _frame.RequestAborted.WaitHandle);
        }

        [Fact]
        public async Task RequestAbortedTokenIsResetBeforeLastWriteWithChunkedEncoding()
        {
            // Need to compare WaitHandle ref since CancellationToken is struct
            var original = _frame.RequestAborted.WaitHandle;

            _frame.HttpVersion = "HTTP/1.1";
            await _frame.WriteAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes("hello, world")), default(CancellationToken));
            Assert.Same(original, _frame.RequestAborted.WaitHandle);

            await _frame.ProduceEndAsync();
            Assert.NotSame(original, _frame.RequestAborted.WaitHandle);
        }

        [Fact]
        public async Task ExceptionDetailNotIncludedWhenLogLevelInformationNotEnabled()
        {
            var previousLog = _serviceContext.Log;

            try
            {
                var mockTrace = new Mock<IKestrelTrace>();
                mockTrace
                    .Setup(trace => trace.IsEnabled(LogLevel.Information))
                    .Returns(false);

                _serviceContext.Log = mockTrace.Object;

                await _input.Writer.WriteAsync(Encoding.ASCII.GetBytes($"GET /%00 HTTP/1.1\r\n"));
                var readableBuffer = (await _input.Reader.ReadAsync()).Buffer;

                var exception = Assert.Throws<BadHttpRequestException>(() =>
                    _frame.TakeStartLine(readableBuffer, out _consumed, out _examined));
                _input.Reader.Advance(_consumed, _examined);

                Assert.Equal("Invalid request target: ''", exception.Message);
                Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
            }
            finally
            {
                _serviceContext.Log = previousLog;
            }
        }

        public static IEnumerable<object> RequestLineValidData => HttpParsingData.RequestLineValidData;

        public static IEnumerable<object> RequestLineDotSegmentData => HttpParsingData.RequestLineDotSegmentData;

        public static TheoryData<string> TargetWithEncodedNullCharData
        {
            get
            {
                var data = new TheoryData<string>();

                foreach (var target in HttpParsingData.TargetWithEncodedNullCharData)
                {
                    data.Add(target);
                }

                return data;
            }
        }

        public static TheoryData<string, string> TargetInvalidData 
            => HttpParsingData.TargetInvalidData;

        public static TheoryData<string, HttpMethod> MethodNotAllowedTargetData
            => HttpParsingData.MethodNotAllowedRequestLine;

        public static TheoryData<string> TargetWithNullCharData
        {
            get
            {
                var data = new TheoryData<string>();

                foreach (var target in HttpParsingData.TargetWithNullCharData)
                {
                    data.Add(target);
                }

                return data;
            }
        }

        public static TheoryData<string> MethodWithNullCharData
        {
            get
            {
                var data = new TheoryData<string>();

                foreach (var target in HttpParsingData.MethodWithNullCharData)
                {
                    data.Add(target);
                }

                return data;
            }
        }

        public static TheoryData<string> QueryStringWithNullCharData
        {
            get
            {
                var data = new TheoryData<string>();

                foreach (var target in HttpParsingData.QueryStringWithNullCharData)
                {
                    data.Add(target);
                }

                return data;
            }
        }
    }
}
