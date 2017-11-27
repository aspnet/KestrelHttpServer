// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Xunit;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class ReasonPhraseTests
    {
        [Theory]
        [InlineData(999, "Unknown")]
        [InlineData(999, null)]
        public void UnknownStatusCodes(int statusCode, string reasonPhrase)
        {
            var bytes = Internal.Http.ReasonPhrases.ToStatusBytes(statusCode, reasonPhrase);
            Assert.NotNull(bytes);
            if (string.IsNullOrEmpty(reasonPhrase))
            {
                Assert.Equal(0x20, bytes[bytes.Length - 1]);
            }
            else
            {
                Assert.NotEqual(0x20, bytes[bytes.Length - 1]);
            }
        }

        [Theory]
        [InlineData(StatusCodes.Status200OK, "OK")]
        [InlineData(StatusCodes.Status200OK, null)]
        public void KnownStatusCodes(int statusCode, string reasonPhrase)
        {
            var bytes = Internal.Http.ReasonPhrases.ToStatusBytes(statusCode, reasonPhrase);
            Assert.NotNull(bytes);
            Assert.NotEqual(0x20, bytes[bytes.Length - 1]);
        }
    }
}