// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class KestrelServerInformationTests
    {
        [Fact]
        public void MaxInputBufferLengthDefault()
        {
            Assert.Equal(1024 * 1024, (new KestrelServerOptions()).MaxInputBufferLength);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        public void MaxInputBufferInvalid(int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                (new KestrelServerOptions()).MaxInputBufferLength = value;
            });
        }

        [Theory]
        [InlineData(null)]
        [InlineData(1)]
        public void MaxInputBufferValid(int? value)
        {
            var o = new KestrelServerOptions();
            o.MaxInputBufferLength = value;
            Assert.Equal(value, o.MaxInputBufferLength);
        }

        [Fact]
        public void SetThreadCountUsingProcessorCount()
        {
            // Ideally we'd mock Environment.ProcessorCount to test edge cases.
            var expected = Clamp(Environment.ProcessorCount >> 1, 1, 16);

            var information = new KestrelServerOptions();

            Assert.Equal(expected, information.ThreadCount);
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}