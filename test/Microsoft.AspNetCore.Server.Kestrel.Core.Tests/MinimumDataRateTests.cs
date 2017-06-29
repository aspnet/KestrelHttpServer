﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
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
            Assert.Equal(value, new MinimumDataRate(bytesPerSecond: value, gracePeriod: TimeSpan.MaxValue).BytesPerSecond);
        }

        [Theory]
        [InlineData(double.MinValue)]
        [InlineData(-double.Epsilon)]
        public void BytesPerSecondInvalid(double value)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new MinimumDataRate(bytesPerSecond: value, gracePeriod: TimeSpan.MaxValue));

            Assert.Equal("bytesPerSecond", exception.ParamName);
            Assert.StartsWith(CoreStrings.NonNegativeNumberRequired, exception.Message);
        }

        [Theory]
        [MemberData(nameof(GracePeriodValidData))]
        public void GracePeriodValid(TimeSpan value)
        {
            Assert.Equal(value, new MinimumDataRate(bytesPerSecond: 1, gracePeriod: value).GracePeriod);
        }

        [Theory]
        [MemberData(nameof(GracePeriodInvalidData))]
        public void GracePeriodInvalid(TimeSpan value)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new MinimumDataRate(bytesPerSecond: 1, gracePeriod: value));

            Assert.Equal("gracePeriod", exception.ParamName);
            Assert.StartsWith(CoreStrings.FormatMinimumGracePeriodRequired(Heartbeat.Interval.TotalSeconds), exception.Message);
        }

        public static TheoryData<TimeSpan> GracePeriodValidData => new TheoryData<TimeSpan>
        {
            Heartbeat.Interval + TimeSpan.FromTicks(1),
            TimeSpan.MaxValue
        };

        public static TheoryData<TimeSpan> GracePeriodInvalidData => new TheoryData<TimeSpan>
        {
            TimeSpan.MinValue,
            TimeSpan.FromTicks(-1),
            TimeSpan.Zero,
            Heartbeat.Interval
        };
    }
}
