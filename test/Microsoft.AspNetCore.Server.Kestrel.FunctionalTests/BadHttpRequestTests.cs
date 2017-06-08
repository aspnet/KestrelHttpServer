// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class BadHttpRequestTests
    {
        [Theory]
        [MemberData(nameof(InvalidRequestLineData))]
        public Task TestInvalidRequestLines(string request, string expectedExceptionMessage)
        {
            return TestBadRequest(
                request,
                "400 Bad Request",
                expectedExceptionMessage);
        }

        [Theory]
        [MemberData(nameof(UnrecognizedHttpVersionData))]
        public Task TestInvalidRequestLinesWithUnrecognizedVersion(string httpVersion)
        {
            return TestBadRequest(
                $"GET / {httpVersion}\r\n",
                "505 HTTP Version Not Supported",
                CoreStrings.FormatBadRequest_UnrecognizedHTTPVersion(httpVersion));
        }

        [Theory]
        [MemberData(nameof(InvalidRequestHeaderData))]
        public Task TestInvalidHeaders(string rawHeaders, string expectedExceptionMessage)
        {
            return TestBadRequest(
                $"GET / HTTP/1.1\r\n{rawHeaders}",
                "400 Bad Request",
                expectedExceptionMessage);
        }

        [Theory]
        [InlineData("Hea\0der: value", "Invalid characters in header name.")]
        [InlineData("Header: va\0lue", "Malformed request: invalid headers.")]
        [InlineData("Head\x80r: value", "Invalid characters in header name.")]
        [InlineData("Header: valu\x80", "Malformed request: invalid headers.")]
        public Task BadRequestWhenHeaderNameContainsNonASCIIOrNullCharacters(string header, string expectedExceptionMessage)
        {
            return TestBadRequest(
                $"GET / HTTP/1.1\r\n{header}\r\n\r\n",
                "400 Bad Request",
                expectedExceptionMessage);
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        public Task BadRequestIfMethodRequiresLengthButNoContentLengthOrTransferEncodingInRequest(string method)
        {
            return TestBadRequest(
                $"{method} / HTTP/1.1\r\nHost:\r\n\r\n",
                "411 Length Required",
                CoreStrings.FormatBadRequest_LengthRequired(method));
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        public Task BadRequestIfMethodRequiresLengthButNoContentLengthInHttp10Request(string method)
        {
            return TestBadRequest(
                $"{method} / HTTP/1.0\r\n\r\n",
                "400 Bad Request",
                CoreStrings.FormatBadRequest_LengthRequiredHttp10(method));
        }

        [Theory]
        [InlineData("NaN")]
        [InlineData("-1")]
        public Task BadRequestIfContentLengthInvalid(string contentLength)
        {
            return TestBadRequest(
                $"POST / HTTP/1.1\r\nHost:\r\nContent-Length: {contentLength}\r\n\r\n",
                "400 Bad Request",
                CoreStrings.FormatBadRequest_InvalidContentLength_Detail(contentLength));
        }

        [Theory]
        [InlineData("GET *", "OPTIONS")]
        [InlineData("GET www.host.com", "CONNECT")]
        public Task RejectsIncorrectMethods(string request, string allowedMethod)
        {
            return TestBadRequest(
                $"{request} HTTP/1.1\r\n",
                "405 Method Not Allowed",
                CoreStrings.BadRequest_MethodNotAllowed,
                $"Allow: {allowedMethod}");
        }

        [Fact]
        public Task BadRequestIfHostHeaderMissing()
        {
            return TestBadRequest(
                "GET / HTTP/1.1\r\n\r\n",
                "400 Bad Request",
                CoreStrings.BadRequest_MissingHostHeader);
        }

        [Fact]
        public Task BadRequestIfMultipleHostHeaders()
        {
            return TestBadRequest("GET / HTTP/1.1\r\nHost: localhost\r\nHost: localhost\r\n\r\n",
                "400 Bad Request",
                CoreStrings.BadRequest_MultipleHostHeaders);
        }

        [Theory]
        [MemberData(nameof(InvalidHostHeaderData))]
        public Task BadRequestIfHostHeaderDoesNotMatchRequestTarget(string requestTarget, string host)
        {
            return TestBadRequest(
                $"{requestTarget} HTTP/1.1\r\nHost: {host}\r\n\r\n",
                "400 Bad Request",
                CoreStrings.FormatBadRequest_InvalidHostHeader_Detail(host.Trim()));
        }

        [Fact]
        public async Task BadRequestLogsAreNotHigherThanInformation()
        {
            var maxLogLevel = LogLevel.Trace;

            var mockLogger = new Mock<ILogger>();
            mockLogger
                .Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>()))
                .Returns(true);
            mockLogger
                .Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<object>(), It.IsAny<Exception>(), It.IsAny<Func<object, Exception, string>>()))
                .Callback<LogLevel, EventId, object, Exception, Func<object, Exception, string>>((logLevel, eventId, state, ex, formatter) =>
                {
                    maxLogLevel = logLevel > maxLogLevel ? logLevel : maxLogLevel;
                });

            using (var server = new TestServer(async context =>
            {
                await context.Request.Body.ReadAsync(new byte[1], 0, 1);
            }, new TestServiceContext { Log = new KestrelTrace(mockLogger.Object) }))
            {
                using (var connection = new TestConnection(server.Port))
                {
                    await connection.SendAll(
                        "GET ? HTTP/1.1",
                        "",
                        "");
                    await ReceiveBadRequestResponse(connection, "400 Bad Request", server.Context.DateHeaderValue);
                }
            }

            const int badRequestEventId = 17;
            mockLogger.Verify(logger => logger.Log(LogLevel.Information, badRequestEventId, It.IsAny<object>(), It.IsAny<BadHttpRequestException>(), It.IsAny<Func<object, Exception, string>>()));

            Assert.Equal(LogLevel.Information, maxLogLevel);
        }

        private async Task TestBadRequest(string request, string expectedResponseStatusCode, string expectedExceptionMessage, string expectedAllowHeader = null)
        {
            BadHttpRequestException loggedException = null;
            var mockKestrelTrace = new Mock<IKestrelTrace>();
            mockKestrelTrace
                .Setup(trace => trace.IsEnabled(LogLevel.Information))
                .Returns(true);
            mockKestrelTrace
                .Setup(trace => trace.ConnectionBadRequest(It.IsAny<string>(), It.IsAny<BadHttpRequestException>()))
                .Callback<string, BadHttpRequestException>((connectionId, exception) => loggedException = exception);

            using (var server = new TestServer(context => Task.CompletedTask, new TestServiceContext { Log = mockKestrelTrace.Object }))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.SendAll(request);
                    await ReceiveBadRequestResponse(connection, expectedResponseStatusCode, server.Context.DateHeaderValue, expectedAllowHeader);
                }
            }

            mockKestrelTrace.Verify(trace => trace.ConnectionBadRequest(It.IsAny<string>(), It.IsAny<BadHttpRequestException>()));
            Assert.Equal(expectedExceptionMessage, loggedException.Message);
        }

        private async Task ReceiveBadRequestResponse(TestConnection connection, string expectedResponseStatusCode, string expectedDateHeaderValue, string expectedAllowHeader = null)
        {
            var lines = new[]
            {
                $"HTTP/1.1 {expectedResponseStatusCode}",
                "Connection: close",
                $"Date: {expectedDateHeaderValue}",
                "Content-Length: 0",
                expectedAllowHeader,
                "",
                ""
            };

            await connection.ReceiveForcedEnd(lines.Where(f => f != null).ToArray());
        }

        public static TheoryData<string, string> InvalidRequestLineData
        {
            get
            {
                var data = new TheoryData<string, string>();

                foreach (var requestLine in HttpParsingData.RequestLineInvalidData)
                {
                    data.Add(requestLine, CoreStrings.FormatBadRequest_InvalidRequestLine_Detail(requestLine.EscapeNonPrintable()));
                }

                foreach (var target in HttpParsingData.TargetWithEncodedNullCharData)
                {
                    data.Add($"GET {target} HTTP/1.1\r\n", CoreStrings.FormatBadRequest_InvalidRequestTarget_Detail(target.EscapeNonPrintable()));
                }

                foreach (var target in HttpParsingData.TargetWithNullCharData)
                {
                    data.Add($"GET {target} HTTP/1.1\r\n", CoreStrings.FormatBadRequest_InvalidRequestTarget_Detail(target.EscapeNonPrintable()));
                }

                return data;
            }
        }

        public static TheoryData<string> UnrecognizedHttpVersionData => HttpParsingData.UnrecognizedHttpVersionData;

        public static IEnumerable<object[]> InvalidRequestHeaderData => HttpParsingData.RequestHeaderInvalidData;

        public static TheoryData<string, string> InvalidHostHeaderData => HttpParsingData.HostHeaderInvalidData;
    }
}
