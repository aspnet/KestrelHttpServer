// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
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
using Microsoft.AspNetCore.Server.KestrelTests.TestHelpers;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Internal;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class FrameTests
    {
        [Fact]
        public void CanReadHeaderValueWithoutLeadingWhitespace()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions()
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>()
                };

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes("Header:value\r\n\r\n");
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var success = frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders);

                Assert.True(success);
                Assert.Equal(1, frame.RequestHeaders.Count);
                Assert.Equal("value", frame.RequestHeaders["Header"]);

                // Assert TakeMessageHeaders consumed all the input
                var scan = socketInput.ConsumingStart();
                Assert.True(scan.IsEnd);
            }
        }

        [Theory]
        [InlineData("Header: value\r\n\r\n")]
        [InlineData("Header:  value\r\n\r\n")]
        [InlineData("Header:\tvalue\r\n\r\n")]
        [InlineData("Header: \tvalue\r\n\r\n")]
        [InlineData("Header:\t value\r\n\r\n")]
        [InlineData("Header:\t\tvalue\r\n\r\n")]
        [InlineData("Header:\t\t value\r\n\r\n")]
        [InlineData("Header: \t\tvalue\r\n\r\n")]
        [InlineData("Header: \t\t value\r\n\r\n")]
        [InlineData("Header: \t \t value\r\n\r\n")]
        public void LeadingWhitespaceIsNotIncludedInHeaderValue(string rawHeaders)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions()
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>()
                };

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes(rawHeaders);
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var success = frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders);

                Assert.True(success);
                Assert.Equal(1, frame.RequestHeaders.Count);
                Assert.Equal("value", frame.RequestHeaders["Header"]);

                // Assert TakeMessageHeaders consumed all the input
                var scan = socketInput.ConsumingStart();
                Assert.True(scan.IsEnd);
            }
        }

        [Theory]
        [InlineData("Header: value \r\n\r\n")]
        [InlineData("Header: value\t\r\n\r\n")]
        [InlineData("Header: value \t\r\n\r\n")]
        [InlineData("Header: value\t \r\n\r\n")]
        [InlineData("Header: value\t\t\r\n\r\n")]
        [InlineData("Header: value\t\t \r\n\r\n")]
        [InlineData("Header: value \t\t\r\n\r\n")]
        [InlineData("Header: value \t\t \r\n\r\n")]
        [InlineData("Header: value \t \t \r\n\r\n")]
        public void TrailingWhitespaceIsNotIncludedInHeaderValue(string rawHeaders)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions()
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>()
                };

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes(rawHeaders);
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var success = frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders);

                Assert.True(success);
                Assert.Equal(1, frame.RequestHeaders.Count);
                Assert.Equal("value", frame.RequestHeaders["Header"]);

                // Assert TakeMessageHeaders consumed all the input
                var scan = socketInput.ConsumingStart();
                Assert.True(scan.IsEnd);
            }
        }

        [Theory]
        [InlineData("Header: one two three\r\n\r\n", "one two three")]
        [InlineData("Header: one  two  three\r\n\r\n", "one  two  three")]
        [InlineData("Header: one\ttwo\tthree\r\n\r\n", "one\ttwo\tthree")]
        [InlineData("Header: one two\tthree\r\n\r\n", "one two\tthree")]
        [InlineData("Header: one\ttwo three\r\n\r\n", "one\ttwo three")]
        [InlineData("Header: one \ttwo \tthree\r\n\r\n", "one \ttwo \tthree")]
        [InlineData("Header: one\t two\t three\r\n\r\n", "one\t two\t three")]
        [InlineData("Header: one \ttwo\t three\r\n\r\n", "one \ttwo\t three")]
        public void WhitespaceWithinHeaderValueIsPreserved(string rawHeaders, string expectedValue)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>()
                };

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes(rawHeaders);
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var success = frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders);

                Assert.True(success);
                Assert.Equal(1, frame.RequestHeaders.Count);
                Assert.Equal(expectedValue, frame.RequestHeaders["Header"]);

                // Assert TakeMessageHeaders consumed all the input
                var scan = socketInput.ConsumingStart();
                Assert.True(scan.IsEnd);
            }
        }

        [Theory]
        [InlineData("Header: line1\r\n line2\r\n\r\n")]
        [InlineData("Header: line1\r\n\tline2\r\n\r\n")]
        [InlineData("Header: line1\r\n  line2\r\n\r\n")]
        [InlineData("Header: line1\r\n \tline2\r\n\r\n")]
        [InlineData("Header: line1\r\n\t line2\r\n\r\n")]
        [InlineData("Header: line1\r\n\t\tline2\r\n\r\n")]
        [InlineData("Header: line1\r\n \t\t line2\r\n\r\n")]
        [InlineData("Header: line1\r\n \t \t line2\r\n\r\n")]
        public void TakeMessageHeadersThrowsOnHeaderValueWithLineFolding(string rawHeaders)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext);

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes(rawHeaders);
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
                Assert.Equal("Header value line folding not supported.", exception.Message);
                Assert.Equal(400, exception.StatusCode);
            }
        }

        [Fact]
        public void TakeMessageHeadersThrowsOnHeaderValueWithLineFolding_CharacterNotAvailableOnFirstAttempt()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext);

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes("Header-1: value1\r\n");
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                Assert.False(frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));

                socketInput.IncomingData(Encoding.ASCII.GetBytes(" "), 0, 1);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
                Assert.Equal("Header value line folding not supported.", exception.Message);
                Assert.Equal(400, exception.StatusCode);
            }
        }

        [Theory]
        [InlineData("Header-1: value1\r\r\n")]
        [InlineData("Header-1: val\rue1\r\n")]
        [InlineData("Header-1: value1\rHeader-2: value2\r\n\r\n")]
        [InlineData("Header-1: value1\r\nHeader-2: value2\r\r\n")]
        [InlineData("Header-1: value1\r\nHeader-2: v\ralue2\r\n")]
        public void TakeMessageHeadersThrowsOnHeaderValueContainingCR(string rawHeaders)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext);

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes(rawHeaders);
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
                Assert.Equal("Header value must not contain CR characters.", exception.Message);
                Assert.Equal(400, exception.StatusCode);
            }
        }

        [Theory]
        [InlineData("Header-1 value1\r\n\r\n")]
        [InlineData("Header-1 value1\r\nHeader-2: value2\r\n\r\n")]
        [InlineData("Header-1: value1\r\nHeader-2 value2\r\n\r\n")]
        public void TakeMessageHeadersThrowsOnHeaderLineMissingColon(string rawHeaders)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext);

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes(rawHeaders);
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
              //  Assert.Equal("No ':' character found in header line.", exception.Message);
                Assert.Equal(400, exception.StatusCode);
            }
        }

        [Theory]
        [InlineData(" Header: value\r\n\r\n")]
        [InlineData("\tHeader: value\r\n\r\n")]
        [InlineData(" Header-1: value1\r\nHeader-2: value2\r\n\r\n")]
        [InlineData("\tHeader-1: value1\r\nHeader-2: value2\r\n\r\n")]
        public void TakeMessageHeadersThrowsOnHeaderLineStartingWithWhitespace(string rawHeaders)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext);

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes(rawHeaders);
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
                Assert.Equal("Header line must not start with whitespace.", exception.Message);
                Assert.Equal(400, exception.StatusCode);
            }
        }

        [Theory]
        [InlineData("Header : value\r\n\r\n")]
        [InlineData("Header\t: value\r\n\r\n")]
        [InlineData("Header 1: value1\r\nHeader-2: value2\r\n\r\n")]
        [InlineData("Header 1 : value1\r\nHeader-2: value2\r\n\r\n")]
        [InlineData("Header 1\t: value1\r\nHeader-2: value2\r\n\r\n")]
        [InlineData("Header-1: value1\r\nHeader 2: value2\r\n\r\n")]
        [InlineData("Header-1: value1\r\nHeader-2 : value2\r\n\r\n")]
        [InlineData("Header-1: value1\r\nHeader-2\t: value2\r\n\r\n")]
        public void TakeMessageHeadersThrowsOnWhitespaceInHeaderName(string rawHeaders)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext);

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes(rawHeaders);
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
                Assert.Equal("Whitespace is not allowed in header name.", exception.Message);
                Assert.Equal(400, exception.StatusCode);
            }
        }

        [Theory]
        [InlineData("Header-1: value1\r\nHeader-2: value2\r\n\r\r")]
        [InlineData("Header-1: value1\r\nHeader-2: value2\r\n\r ")]
        [InlineData("Header-1: value1\r\nHeader-2: value2\r\n\r \n")]
        public void TakeMessageHeadersThrowsOnHeadersNotEndingInCRLFLine(string rawHeaders)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext);

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes(rawHeaders);
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
                Assert.Equal("Headers corrupted, invalid header sequence.", exception.Message);
                Assert.Equal(400, exception.StatusCode);
            }
        }

        [Fact]
        public void TakeMessageHeadersThrowsWhenHeadersExceedTotalSizeLimit()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                const string headerLine = "Header: value\r\n";

                var options = new KestrelServerOptions();
                options.Limits.MaxRequestHeadersTotalSize = headerLine.Length - 1;

                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = options,
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext);

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes($"{headerLine}\r\n");
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
                Assert.Equal("Request headers too long.", exception.Message);
                Assert.Equal(431, exception.StatusCode);
            }
        }

        [Fact]
        public void TakeMessageHeadersThrowsWhenHeadersExceedCountLimit()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                const string headerLines = "Header-1: value1\r\nHeader-2: value2\r\n";

                var options = new KestrelServerOptions();
                options.Limits.MaxRequestHeaderCount = 1;

                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = options,
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext);

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes($"{headerLines}\r\n");
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
                Assert.Equal("Request contains too many headers.", exception.Message);
                Assert.Equal(431, exception.StatusCode);
            }
        }

        [Theory]
        [InlineData("Cookie: \r\n\r\n", 1)]
        [InlineData("Cookie:\r\n\r\n", 1)]
        [InlineData("Cookie: \r\nConnection: close\r\n\r\n", 2)]
        [InlineData("Cookie:\r\nConnection: close\r\n\r\n", 2)]
        [InlineData("Connection: close\r\nCookie: \r\n\r\n", 2)]
        [InlineData("Connection: close\r\nCookie:\r\n\r\n", 2)]
        public void EmptyHeaderValuesCanBeParsed(string rawHeaders, int numHeaders)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions()
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>()
                };

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray = Encoding.ASCII.GetBytes(rawHeaders);
                socketInput.IncomingData(headerArray, 0, headerArray.Length);

                var success = frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders);

                Assert.True(success);
                Assert.Equal(numHeaders, frame.RequestHeaders.Count);

                // Assert TakeMessageHeaders consumed all the input
                var scan = socketInput.ConsumingStart();
                Assert.True(scan.IsEnd);
            }
        }

        [Fact]
        public void ResetResetsScheme()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext);

            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.Scheme = "https";

            // Act
            frame.Reset();

            // Assert
            Assert.Equal("http", ((IFeatureCollection)frame).Get<IHttpRequestFeature>().Scheme);
        }

        [Fact]
        public void ResetResetsHeaderLimits()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                const string headerLine1 = "Header-1: value1\r\n";
                const string headerLine2 = "Header-2: value2\r\n";

                var options = new KestrelServerOptions();
                options.Limits.MaxRequestHeadersTotalSize = headerLine1.Length;
                options.Limits.MaxRequestHeaderCount = 1;

                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = options
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>()
                };

                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerArray1 = Encoding.ASCII.GetBytes($"{headerLine1}\r\n");
                socketInput.IncomingData(headerArray1, 0, headerArray1.Length);

                Assert.True(frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
                Assert.Equal(1, frame.RequestHeaders.Count);
                Assert.Equal("value1", frame.RequestHeaders["Header-1"]);

                frame.Reset();

                var headerArray2 = Encoding.ASCII.GetBytes($"{headerLine2}\r\n");
                socketInput.IncomingData(headerArray2, 0, headerArray1.Length);

                Assert.True(frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
                Assert.Equal(1, frame.RequestHeaders.Count);
                Assert.Equal("value2", frame.RequestHeaders["Header-2"]);
            }
        }

        [Fact]
        public void ThrowsWhenStatusCodeIsSetAfterResponseStarted()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut()
            };

            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();

            // Act
            frame.Write(new ArraySegment<byte>(new byte[1]));

            // Assert
            Assert.True(frame.HasResponseStarted);
            Assert.Throws<InvalidOperationException>(() => ((IHttpResponseFeature)frame).StatusCode = 404);
        }

        [Fact]
        public void ThrowsWhenReasonPhraseIsSetAfterResponseStarted()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut()
            };

            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();

            // Act
            frame.Write(new ArraySegment<byte>(new byte[1]));

            // Assert
            Assert.True(frame.HasResponseStarted);
            Assert.Throws<InvalidOperationException>(() => ((IHttpResponseFeature)frame).ReasonPhrase = "Reason phrase");
        }

        [Fact]
        public void ThrowsWhenOnStartingIsSetAfterResponseStarted()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut()
            };

            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();
            frame.Write(new ArraySegment<byte>(new byte[1]));

            // Act/Assert
            Assert.True(frame.HasResponseStarted);
            Assert.Throws<InvalidOperationException>(() => ((IHttpResponseFeature)frame).OnStarting(_ => TaskCache.CompletedTask, null));
        }

        [Fact]
        public void InitializeHeadersResetsRequestHeaders()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut()
            };

            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();

            var originalRequestHeaders = frame.RequestHeaders;
            frame.RequestHeaders = new FrameRequestHeaders();

            // Act
            frame.InitializeHeaders();

            // Assert
            Assert.Same(originalRequestHeaders, frame.RequestHeaders);
        }

        [Fact]
        public void InitializeHeadersResetsResponseHeaders()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut()
            };

            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();

            var originalResponseHeaders = frame.ResponseHeaders;
            frame.ResponseHeaders = new FrameResponseHeaders();

            // Act
            frame.InitializeHeaders();

            // Assert
            Assert.Same(originalResponseHeaders, frame.ResponseHeaders);
        }

        [Fact]
        public void InitializeStreamsResetsStreams()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut()
            };

            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();

            var messageBody = MessageBody.For(HttpVersion.Http11, (FrameRequestHeaders)frame.RequestHeaders, frame);
            frame.InitializeStreams(messageBody);

            var originalRequestBody = frame.RequestBody;
            var originalResponseBody = frame.ResponseBody;
            var originalDuplexStream = frame.DuplexStream;
            frame.RequestBody = new MemoryStream();
            frame.ResponseBody = new MemoryStream();
            frame.DuplexStream = new MemoryStream();

            // Act
            frame.InitializeStreams(messageBody);

            // Assert
            Assert.Same(originalRequestBody, frame.RequestBody);
            Assert.Same(originalResponseBody, frame.ResponseBody);
            Assert.Same(originalDuplexStream, frame.DuplexStream);
        }

        [Fact]
        public void TakeStartLineCallsConsumingCompleteWithFurthestExamined()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000"),
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = new Mock<IConnectionControl>().Object
                };
                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();

                var requestLineBytes = Encoding.ASCII.GetBytes("GET / ");
                socketInput.IncomingData(requestLineBytes, 0, requestLineBytes.Length);
                frame.TakeStartLine(socketInput);
                Assert.False(socketInput.IsCompleted);

                requestLineBytes = Encoding.ASCII.GetBytes("HTTP/1.1\r\n");
                socketInput.IncomingData(requestLineBytes, 0, requestLineBytes.Length);
                frame.TakeStartLine(socketInput);
                Assert.False(socketInput.IsCompleted);
            }
        }

        [Theory]
        [InlineData("", Frame.RequestLineStatus.Empty)]
        [InlineData("G", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GE", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET ", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET /", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET / ", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET / H", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET / HT", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET / HTT", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET / HTTP", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET / HTTP/", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET / HTTP/1", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET / HTTP/1.", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET / HTTP/1.1", Frame.RequestLineStatus.Incomplete)]
        [InlineData("GET / HTTP/1.1\r", Frame.RequestLineStatus.Incomplete)]
        public void TakeStartLineReturnsWhenGivenIncompleteRequestLines(string requestLine, Frame.RequestLineStatus expectedReturnValue)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000"),
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = new Mock<IConnectionControl>().Object
                };
                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();

                var requestLineBytes = Encoding.ASCII.GetBytes(requestLine);
                socketInput.IncomingData(requestLineBytes, 0, requestLineBytes.Length);

                var returnValue = frame.TakeStartLine(socketInput);
                Assert.Equal(expectedReturnValue, returnValue);
            }
        }

        [Fact]
        public void TakeStartLineStartsRequestHeadersTimeoutOnFirstByteAvailable()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000"),
                };
                var connectionControl = new Mock<IConnectionControl>();
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = connectionControl.Object
                };
                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();

                var requestLineBytes = Encoding.ASCII.GetBytes("G");
                socketInput.IncomingData(requestLineBytes, 0, requestLineBytes.Length);

                frame.TakeStartLine(socketInput);
                var expectedRequestHeadersTimeout = (long)serviceContext.ServerOptions.Limits.RequestHeadersTimeout.TotalMilliseconds;
                connectionControl.Verify(cc => cc.ResetTimeout(expectedRequestHeadersTimeout, TimeoutAction.SendTimeoutResponse));
            }
        }

        [Fact]
        public void TakeStartLineDoesNotStartRequestHeadersTimeoutIfNoDataAvailable()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionControl = new Mock<IConnectionControl>();
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = connectionControl.Object
                };
                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();

                frame.TakeStartLine(socketInput);
                connectionControl.Verify(cc => cc.ResetTimeout(It.IsAny<long>(), It.IsAny<TimeoutAction>()), Times.Never);
            }
        }

        [Fact]
        public void TakeStartLineThrowsWhenTooLong()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions()
                    {
                        Limits =
                        {
                            MaxRequestLineSize = "GET / HTTP/1.1\r\n".Length
                        }
                    },
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>()
                };
                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();

                var requestLineBytes = Encoding.ASCII.GetBytes("GET /a HTTP/1.1\r\n");
                socketInput.IncomingData(requestLineBytes, 0, requestLineBytes.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeStartLine(socketInput));
                Assert.Equal("Request line too long.", exception.Message);
                Assert.Equal(414, exception.StatusCode);
            }
        }

        [Theory]
        [InlineData("GET/HTTP/1.1\r\n", "Invalid request line: GET/HTTP/1.1<0x0D><0x0A>")]
        [InlineData(" / HTTP/1.1\r\n", "Invalid request line:  / HTTP/1.1<0x0D><0x0A>")]
        [InlineData("GET? / HTTP/1.1\r\n", "Invalid request line: GET? / HTTP/1.1<0x0D><0x0A>")]
        [InlineData("GET /HTTP/1.1\r\n", "Invalid request line: GET /HTTP/1.1<0x0D><0x0A>")]
        [InlineData("GET /a?b=cHTTP/1.1\r\n", "Invalid request line: GET /a?b=cHTTP/1.1<0x0D><0x0A>")]
        [InlineData("GET /a%20bHTTP/1.1\r\n", "Invalid request line: GET /a%20bHTTP/1.1<0x0D><0x0A>")]
        [InlineData("GET /a%20b?c=dHTTP/1.1\r\n", "Invalid request line: GET /a%20b?c=dHTTP/1.1<0x0D><0x0A>")]
        [InlineData("GET  HTTP/1.1\r\n", "Invalid request line: GET  HTTP/1.1<0x0D><0x0A>")]
        [InlineData("GET / HTTP/1.1\n", "Invalid request line: GET / HTTP/1.1<0x0A>")]
        [InlineData("GET / \r\n", "Invalid request line: GET / <0x0D><0x0A>")]
        [InlineData("GET / HTTP/1.1\ra\n", "Invalid request line: GET / HTTP/1.1<0x0D>a<0x0A>")]
        public void TakeStartLineThrowsWhenInvalid(string requestLine, string expectedExceptionMessage)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>()
                };
                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();

                var requestLineBytes = Encoding.ASCII.GetBytes(requestLine);
                socketInput.IncomingData(requestLineBytes, 0, requestLineBytes.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeStartLine(socketInput));
                Assert.Equal(expectedExceptionMessage, exception.Message);
                Assert.Equal(400, exception.StatusCode);
            }
        }

        [Fact]
        public void TakeStartLineThrowsOnUnsupportedHttpVersion()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>(),
                };
                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();

                var requestLineBytes = Encoding.ASCII.GetBytes("GET / HTTP/1.2\r\n");
                socketInput.IncomingData(requestLineBytes, 0, requestLineBytes.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeStartLine(socketInput));
                Assert.Equal("Unrecognized HTTP version: HTTP/1.2", exception.Message);
                Assert.Equal(505, exception.StatusCode);
            }
        }

        [Fact]
        public void TakeStartLineThrowsOnUnsupportedHttpVersionLongerThanEigthCharacters()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>(),
                };
                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();

                var requestLineBytes = Encoding.ASCII.GetBytes("GET / HTTP/1.1ab\r\n");
                socketInput.IncomingData(requestLineBytes, 0, requestLineBytes.Length);

                var exception = Assert.Throws<BadHttpRequestException>(() => frame.TakeStartLine(socketInput));
                Assert.Equal("Unrecognized HTTP version: HTTP/1.1a...", exception.Message);
                Assert.Equal(505, exception.StatusCode);
            }
        }

        [Fact]
        public void TakeMessageHeadersCallsConsumingCompleteWithFurthestExamined()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>()
                };
                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headersBytes = Encoding.ASCII.GetBytes("Header: ");
                socketInput.IncomingData(headersBytes, 0, headersBytes.Length);
                frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders);
                Assert.False(socketInput.IsCompleted);

                headersBytes = Encoding.ASCII.GetBytes("value\r\n");
                socketInput.IncomingData(headersBytes, 0, headersBytes.Length);
                frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders);
                Assert.False(socketInput.IsCompleted);

                headersBytes = Encoding.ASCII.GetBytes("\r\n");
                socketInput.IncomingData(headersBytes, 0, headersBytes.Length);
                frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders);
                Assert.False(socketInput.IsCompleted);
            }
        }

        [Theory]
        [InlineData("\r")]
        [InlineData("H")]
        [InlineData("He")]
        [InlineData("Hea")]
        [InlineData("Head")]
        [InlineData("Heade")]
        [InlineData("Header")]
        [InlineData("Header:")]
        [InlineData("Header: ")]
        [InlineData("Header: v")]
        [InlineData("Header: va")]
        [InlineData("Header: val")]
        [InlineData("Header: valu")]
        [InlineData("Header: value")]
        [InlineData("Header: value\r")]
        [InlineData("Header: value\r\n")]
        [InlineData("Header: value\r\n\r")]
        public void TakeMessageHeadersReturnsWhenGivenIncompleteHeaders(string headers)
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000"),
                };
                var connectionContext = new ConnectionContext(listenerContext);
                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();
                frame.InitializeHeaders();

                var headerBytes = Encoding.ASCII.GetBytes(headers);
                socketInput.IncomingData(headerBytes, 0, headerBytes.Length);

                Assert.Equal(false, frame.TakeMessageHeaders(socketInput, (FrameRequestHeaders)frame.RequestHeaders));
            }
        }

        [Fact]
        public void RequestProcessingAsyncEnablesKeepAliveTimeout()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionControl = new Mock<IConnectionControl>();
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = connectionControl.Object
                };
                var frame = new Frame<object>(application: null, context: connectionContext);
                frame.Reset();

                var requestProcessingTask = frame.RequestProcessingAsync();

                var expectedKeepAliveTimeout = (long)serviceContext.ServerOptions.Limits.KeepAliveTimeout.TotalMilliseconds;
                connectionControl.Verify(cc => cc.SetTimeout(expectedKeepAliveTimeout, TimeoutAction.CloseConnection));

                frame.StopAsync();
                socketInput.IncomingFin();

                requestProcessingTask.Wait();
            }
        }

        [Fact]
        public void WriteThrowsForNonBodyResponse()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions(),
                Log = new TestKestrelTrace()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut(),
            };
            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();
            frame.HttpVersion = "HTTP/1.1";
            ((IHttpResponseFeature)frame).StatusCode = 304;

            // Act/Assert
            Assert.Throws<InvalidOperationException>(() => frame.Write(new ArraySegment<byte>(new byte[1])));
        }

        [Fact]
        public async Task WriteAsyncThrowsForNonBodyResponse()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions(),
                Log = new TestKestrelTrace()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut(),
            };
            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();
            frame.HttpVersion = "HTTP/1.1";
            ((IHttpResponseFeature)frame).StatusCode = 304;

            // Act/Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => frame.WriteAsync(new ArraySegment<byte>(new byte[1]), default(CancellationToken)));
        }

        [Fact]
        public void WriteDoesNotThrowForHeadResponse()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions(),
                Log = new TestKestrelTrace()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut(),
            };
            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();
            frame.HttpVersion = "HTTP/1.1";
            ((IHttpRequestFeature)frame).Method = "HEAD";

            // Act/Assert
            frame.Write(new ArraySegment<byte>(new byte[1]));
        }

        [Fact]
        public async Task WriteAsyncDoesNotThrowForHeadResponse()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions(),
                Log = new TestKestrelTrace()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut(),
            };
            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();
            frame.HttpVersion = "HTTP/1.1";
            ((IHttpRequestFeature)frame).Method = "HEAD";

            // Act/Assert
            await frame.WriteAsync(new ArraySegment<byte>(new byte[1]), default(CancellationToken));
        }

        [Fact]
        public void ManuallySettingTransferEncodingThrowsForHeadResponse()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions(),
                Log = new TestKestrelTrace()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut(),
            };
            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();
            frame.HttpVersion = "HTTP/1.1";
            ((IHttpRequestFeature)frame).Method = "HEAD";

            // Act
            frame.ResponseHeaders.Add("Transfer-Encoding", "chunked");

            // Assert
            Assert.Throws<InvalidOperationException>(() => frame.Flush());
        }

        [Fact]
        public void ManuallySettingTransferEncodingThrowsForNoBodyResponse()
        {
            // Arrange
            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions(),
                Log = new TestKestrelTrace()
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
            };
            var connectionContext = new ConnectionContext(listenerContext)
            {
                SocketOutput = new MockSocketOuptut(),
            };
            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();
            frame.HttpVersion = "HTTP/1.1";
            ((IHttpResponseFeature)frame).StatusCode = 304;

            // Act
            frame.ResponseHeaders.Add("Transfer-Encoding", "chunked");

            // Assert
            Assert.Throws<InvalidOperationException>(() => frame.Flush());
        }

        [Fact]
        public async Task RequestProcessingTaskIsUnwrapped()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var ltp = new LoggingThreadPool(trace);
            using (var pool = new MemoryPool())
            using (var socketInput = new SocketInput(pool, ltp))
            {
                var serviceContext = new ServiceContext
                {
                    DateHeaderValueManager = new DateHeaderValueManager(),
                    ServerOptions = new KestrelServerOptions(),
                    Log = trace
                };
                var listenerContext = new ListenerContext(serviceContext)
                {
                    ServerAddress = ServerAddress.FromUrl("http://localhost:5000")
                };
                var connectionContext = new ConnectionContext(listenerContext)
                {
                    ConnectionControl = Mock.Of<IConnectionControl>(),
                    SocketInput = socketInput
                };

                var frame = new Frame<HttpContext>(application: null, context: connectionContext);
                frame.Start();

                var data = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\n");
                socketInput.IncomingData(data, 0, data.Length);

                var requestProcessingTask = frame.StopAsync();
                Assert.IsNotType(typeof(Task<Task>), requestProcessingTask);

                await requestProcessingTask.TimeoutAfter(TimeSpan.FromSeconds(10));
                socketInput.IncomingFin();
            }
        }
    }
}
