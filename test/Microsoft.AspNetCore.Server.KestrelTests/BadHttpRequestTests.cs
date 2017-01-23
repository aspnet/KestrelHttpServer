// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class BadHttpRequestTests
    {
        // All test cases for this theory must end in '\n', otherwise the server will spin forever
        [Theory]
        // Incomplete request lines
        [InlineData("G\r\n")]
        [InlineData("GE\r\n")]
        [InlineData("GET\r\n")]
        [InlineData("GET \r\n")]
        [InlineData("GET /\r\n")]
        [InlineData("GET / \r\n")]
        // Missing method
        [InlineData(" \r\n")]
        // Missing second space
        [InlineData("/ \r\n")] // This fails trying to read the '/' because that's invalid for an HTTP method
        [InlineData("GET /\r\n")]
        // Missing target
        [InlineData("GET  \r\n")]
        // Missing version
        [InlineData("GET / \r\n")]
        // Missing CR
        [InlineData("GET / \n")]
        // Missing LF after CR
        [InlineData("GET / HTTP/1.0\rA\n")]
        // Bad HTTP Methods (invalid according to RFC)
        [InlineData("( / HTTP/1.0\r\n")]
        [InlineData(") / HTTP/1.0\r\n")]
        [InlineData("< / HTTP/1.0\r\n")]
        [InlineData("> / HTTP/1.0\r\n")]
        [InlineData("@ / HTTP/1.0\r\n")]
        [InlineData(", / HTTP/1.0\r\n")]
        [InlineData("; / HTTP/1.0\r\n")]
        [InlineData(": / HTTP/1.0\r\n")]
        [InlineData("\\ / HTTP/1.0\r\n")]
        [InlineData("\" / HTTP/1.0\r\n")]
        [InlineData("/ / HTTP/1.0\r\n")]
        [InlineData("[ / HTTP/1.0\r\n")]
        [InlineData("] / HTTP/1.0\r\n")]
        [InlineData("? / HTTP/1.0\r\n")]
        [InlineData("= / HTTP/1.0\r\n")]
        [InlineData("{ / HTTP/1.0\r\n")]
        [InlineData("} / HTTP/1.0\r\n")]
        [InlineData("get@ / HTTP/1.0\r\n")]
        [InlineData("post= / HTTP/1.0\r\n")]
        public async Task TestInvalidRequestLines(string request)
        {
            await TestBadRequest(
                request,
                "400 Bad Request",
                $"Invalid request line: {request.Replace("\r", "<0x0D>").Replace("\n", "<0x0A>")}");
        }

        [Theory]
        [InlineData("H")]
        [InlineData("HT")]
        [InlineData("HTT")]
        [InlineData("HTTP")]
        [InlineData("HTTP/")]
        [InlineData("HTTP/1")]
        [InlineData("HTTP/1.")]
        [InlineData("http/1.0")]
        [InlineData("http/1.1")]
        [InlineData("HTTP/1.1 ")]
        [InlineData("HTTP/1.1a")]
        [InlineData("HTTP/1.2")]
        [InlineData("HTTP/3.0")]
        [InlineData("H")]
        [InlineData("HTTP/1.")]
        [InlineData("hello")]
        [InlineData("8charact")]
        public async Task TestInvalidRequestLinesWithUnsupportedVersion(string badVersion)
        {
            await TestBadRequest(
                $"GET / {badVersion}\r\n",
                "505 HTTP Version Not Supported",
                $"Unrecognized HTTP version: {badVersion}");
        }

        [Theory]
        // Leading whitespace
        [InlineData(" Header-1: value1\r\nHeader-2: value2\r\n\r\n", "Header line must not start with whitespace.")]
        [InlineData("\tHeader-1: value1\r\nHeader-2: value2\r\n\r\n", "Header line must not start with whitespace.")]
        // Missing LF
        [InlineData("Header-1: value1\rHeader-2: value2\r\n\r\n", "Header value must not contain CR characters.")]
        [InlineData("Header-1: value1\r\nHeader-2: value2\r\r\n", "Header value must not contain CR characters.")]
        // Line folding
        [InlineData("Header-1: value1\r\n Header-2: value2\r\n\r\n", "Header value line folding not supported.")]
        [InlineData("Header-1: value1\r\n\tHeader-2: value2\r\n\r\n", "Header value line folding not supported.")]
        [InlineData("Header-1: multi\r\n line\r\nHeader-2: value2\r\n\r\n", "Header value line folding not supported.")]
        [InlineData("Header-1: value1\r\nHeader-2: multi\r\n line\r\n\r\n", "Header value line folding not supported.")]
        // Missing ':'
        [InlineData("Header-1 value1\r\nHeader-2: value2\r\n\r\n", "No ':' character found in header line.")]
        [InlineData("Header-1: value1\r\nHeader-2 value2\r\n\r\n", "No ':' character found in header line.")]
        // Whitespace in header name
        [InlineData("Header 1: value1\r\nHeader-2: value2\r\n\r\n", "Whitespace is not allowed in header name.")]
        [InlineData("Header-1: value1\r\nHeader 2: value2\r\n\r\n", "Whitespace is not allowed in header name.")]
        [InlineData("Header-1 : value1\r\nHeader-2: value2\r\n\r\n", "Whitespace is not allowed in header name.")]
        [InlineData("Header-1\t: value1\r\nHeader-2: value2\r\n\r\n", "Whitespace is not allowed in header name.")]
        [InlineData("Header-1: value1\r\nHeader-2 : value2\r\n\r\n", "Whitespace is not allowed in header name.")]
        [InlineData("Header-1: value1\r\nHeader-2\t: value2\r\n\r\n", "Whitespace is not allowed in header name.")]
        public async Task TestInvalidHeaders(string rawHeaders, string expectedExceptionMessage)
        {
            await TestBadRequest(
                $"GET / HTTP/1.1\r\n{rawHeaders}\r\n\r\n",
                "400 Bad Request",
                expectedExceptionMessage);
        }

        [Theory]
        [InlineData("Hea\0der: value", "Hea<0x00>der: value")]
        [InlineData("Header: va\0lue", "Header: va<0x00>lue")]
        [InlineData("Head\x80r: value", "Head<0x80>r: value")]
        [InlineData("Header: valu\x80", "Header: valu<0x80>")]
        public async Task BadRequestIfHeaderContainsNonASCIIOrNullCharacters(string header, string expectedLoggedHeader)
        {
            await TestBadRequest(
                $"GET / HTTP/1.1\r\n{header}\r\n\r\n",
                "400 Bad Request",
                $"Request header contains non-ASCII or null characters: {expectedLoggedHeader}");
        }

        [Theory]
        [InlineData("\0", "<0x00>")]
        [InlineData("%00", "%00")]
        [InlineData("/\0", "/<0x00>")]
        [InlineData("/%00", "/%00")]
        [InlineData("/\0\0", "/<0x00><0x00>")]
        [InlineData("/%00%00", "/%00%00")]
        [InlineData("/%C8\0", "/%C8<0x00>")]
        [InlineData("/%E8%00%84", "/%E8%00%84")]
        [InlineData("/%E8%85%00", "/%E8%85%00")]
        [InlineData("/%F3%00%82%86", "/%F3%00%82%86")]
        [InlineData("/%F3%85%00%82", "/%F3%85%00%82")]
        [InlineData("/%F3%85%82%00", "/%F3%85%82%00")]
        [InlineData("/%E8%85%00", "/%E8%85%00")]
        // Request line below will only be partially decoded, hence look a bit strange
        [InlineData("/%E8%01%00", "/%E8<0x01>01%00")]
        public async Task BadRequestIfPathContainsNullCharacters(string path, string expectedLoggedPath)
        {
            await TestBadRequest(
                $"GET {path} HTTP/1.1\r\n",
                "400 Bad Request",
                $"Request line contains non-ASCII or null characters: GET {expectedLoggedPath} HTTP/1.1<0x0D><0x0A>");
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        public async Task BadRequestIfMethodRequiresLengthButNoContentLengthOrTransferEncodingInRequest(string method)
        {
            await TestBadRequest(
                $"{method} / HTTP/1.1\r\n\r\n",
                "411 Length Required",
                $"{method} request contains no Content-Length or Transfer-Encoding header");
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        public async Task BadRequestIfMethodRequiresLengthButNoContentLengthInHttp10Request(string method)
        {
            await TestBadRequest(
                $"{method} / HTTP/1.0\r\n\r\n",
                "400 Bad Request",
                $"{method} request contains no Content-Length header");
        }

        private async Task TestBadRequest(string request, string expectedResponseStatusCode, string expectedExceptionMessage)
        {
            BadHttpRequestException loggedException = null;
            var mockKestrelTrace = new Mock<IKestrelTrace>();
            mockKestrelTrace
                .Setup(trace => trace.IsEnabled(LogLevel.Information))
                .Returns(true);
            mockKestrelTrace
                .Setup(trace => trace.ConnectionBadRequest(It.IsAny<string>(), It.IsAny<BadHttpRequestException>()))
                .Callback<string, BadHttpRequestException>((connectionId, exception) => loggedException = exception);

            using (var server = new TestServer(context => TaskCache.CompletedTask, new TestServiceContext { Log = mockKestrelTrace.Object }))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.SendAll(request);
                    await ReceiveBadRequestResponse(connection, expectedResponseStatusCode, server.Context.DateHeaderValue);
                }
            }

            mockKestrelTrace.Verify(trace => trace.ConnectionBadRequest(It.IsAny<string>(), It.IsAny<BadHttpRequestException>()));
            Assert.Equal(expectedExceptionMessage, loggedException.Message);
        }

        private async Task ReceiveBadRequestResponse(TestConnection connection, string expectedResponseStatusCode, string expectedDateHeaderValue)
        {
            await connection.ReceiveForcedEnd(
                $"HTTP/1.1 {expectedResponseStatusCode}",
                "Connection: close",
                $"Date: {expectedDateHeaderValue}",
                "Content-Length: 0",
                "",
                "");
        }
    }
}
