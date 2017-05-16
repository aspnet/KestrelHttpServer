// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class MessageBodyTests
    {
        [Fact]
        public void ForThrowsWhenFinalTransferCodingIsNotChunked()
        {
            using (var input = new TestInput())
            {
                var ex = Assert.Throws<BadHttpRequestException>(() =>
                    MessageBody.For(HttpVersion.Http11, new FrameRequestHeaders { HeaderTransferEncoding = "chunked, not-chunked" }, input.FrameContext));

                Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
                Assert.Equal(CoreStrings.FormatBadRequest_FinalTransferCodingNotChunked("chunked, not-chunked"), ex.Message);
            }
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        public void ForThrowsWhenMethodRequiresLengthButNoContentLengthOrTransferEncodingIsSet(string method)
        {
            using (var input = new TestInput())
            {
                input.FrameContext.Method = method;
                var ex = Assert.Throws<BadHttpRequestException>(() =>
                    MessageBody.For(HttpVersion.Http11, new FrameRequestHeaders(), input.FrameContext));

                Assert.Equal(StatusCodes.Status411LengthRequired, ex.StatusCode);
                Assert.Equal(CoreStrings.FormatBadRequest_LengthRequired(method), ex.Message);
            }
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        public void ForThrowsWhenMethodRequiresLengthButNoContentLengthSetHttp10(string method)
        {
            using (var input = new TestInput())
            {
                input.FrameContext.Method = method;
                var ex = Assert.Throws<BadHttpRequestException>(() =>
                    MessageBody.For(HttpVersion.Http10, new FrameRequestHeaders(), input.FrameContext));

                Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
                Assert.Equal(CoreStrings.FormatBadRequest_LengthRequiredHttp10(method), ex.Message);
            }
        }
    }
}
