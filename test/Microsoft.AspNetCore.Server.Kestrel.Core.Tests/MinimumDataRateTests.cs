// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class MinimumDataRateTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(double.Epsilon)]
        [InlineData(double.MaxValue)]
        public void BytesPerSecondValid(double value)
        {
            Assert.Equal(value, new KestrelServerLimits.MinimumDataRate { BytesPerSecond = value, GracePeriod = TimeSpan.Zero }.BytesPerSecond);
        }

        [Theory]
        [InlineData(double.MinValue)]
        [InlineData(-double.Epsilon)]
        public void BytesPerSecondInvalid(double value)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new KestrelServerLimits.MinimumDataRate { BytesPerSecond = value, GracePeriod = TimeSpan.Zero });

            Assert.Equal("value", exception.ParamName);
            Assert.StartsWith(CoreStrings.NonNegativeNumberRequired, exception.Message);
        }

        [Theory]
        [MemberData(nameof(GracePeriodValidData))]
        public void GracePeriodValid(TimeSpan value)
        {
            Assert.Equal(value, new KestrelServerLimits.MinimumDataRate { BytesPerSecond = 1, GracePeriod = value }.GracePeriod);
        }

        [Theory]
        [MemberData(nameof(GracePeriodInvalidData))]
        public void GracePeriodInvalid(TimeSpan value)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new KestrelServerLimits.MinimumDataRate { BytesPerSecond = 1, GracePeriod = value });

            Assert.Equal("value", exception.ParamName);
            Assert.StartsWith(CoreStrings.NonNegativeTimeSpanRequired, exception.Message);
        }

        public static TheoryData<TimeSpan> GracePeriodValidData => new TheoryData<TimeSpan>
        {
            TimeSpan.Zero,
            TimeSpan.FromTicks(1),
            TimeSpan.MaxValue
        };

        public static TheoryData<TimeSpan> GracePeriodInvalidData => new TheoryData<TimeSpan>
        {
            TimeSpan.MinValue,
            TimeSpan.FromTicks(-1)
        };
    }
}
