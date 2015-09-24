// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNet.Server.Kestrel.Http;
using Microsoft.AspNet.Server.KestrelTests.TestHelpers;
using Xunit;

namespace Microsoft.AspNet.Server.KestrelTests
{
    public class DateHeaderValueManagerTests
    {
        [Fact]
        public void GetDateHeaderValue_ReturnsDateValueInCorrectFormat()
        {
            var now = DateTimeOffset.UtcNow;
            var systemClock = new MockSystemClock
            {
                UtcNow = now
            };

            var dateHeaderValueManager = new DateHeaderValueManager(systemClock);
            var value = dateHeaderValueManager.GetDateHeaderValue();

            Assert.Equal(now.ToString("r"), value);
        }
    }
}
