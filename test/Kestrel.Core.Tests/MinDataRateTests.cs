// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class MinDataRateTests
    {
        [Theory]
        [InlineData(double.Epsilon)]
        [InlineData(double.MaxValue)]
        public void BytesPerSecondValid(double value)
        {
            Assert.Equal(value, new MinDataRate(bytesPerSecond: value, gracePeriod: TimeSpan.MaxValue).BytesPerSecond);
        }

        [Theory]
        [InlineData(double.MinValue)]
        [InlineData(-double.Epsilon)]
        [InlineData(0)]
        public void BytesPerSecondInvalid(double value)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new MinDataRate(bytesPerSecond: value, gracePeriod: TimeSpan.MaxValue));

            Assert.Equal("bytesPerSecond", exception.ParamName);
            Assert.StartsWith(CoreStrings.PositiveNumberOrNullMinDataRateRequired, exception.Message);
        }

        [Theory]
        [MemberData(nameof(GracePeriodValidDataNames))]
        public void GracePeriodValid(string gracePeriodName)
        {
            var value = GracePeriodValidData[gracePeriodName];

            Assert.Equal(value, new MinDataRate(bytesPerSecond: 1, gracePeriod: value).GracePeriod);
        }

        [Theory]
        [MemberData(nameof(GracePeriodInvalidDataNames))]
        public void GracePeriodInvalid(string gracePeriodName)
        {
            var value = GracePeriodInvalidData[gracePeriodName];

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new MinDataRate(bytesPerSecond: 1, gracePeriod: value));

            Assert.Equal("gracePeriod", exception.ParamName);
            Assert.StartsWith(CoreStrings.FormatMinimumGracePeriodRequired(Heartbeat.Interval.TotalSeconds), exception.Message);
        }

        public static IEnumerable<object[]> GracePeriodValidDataNames => GracePeriodValidData.Keys.Select(key => new object[] { key });

        public static IDictionary<string, TimeSpan> GracePeriodValidData => new Dictionary<string, TimeSpan>
        {
            { "HeartbeatIntervalPlusOneTick", Heartbeat.Interval + TimeSpan.FromTicks(1) },
            { "MaxValue", TimeSpan.MaxValue },
        };

        public static IEnumerable<object[]> GracePeriodInvalidDataNames => GracePeriodInvalidData.Keys.Select(key => new object[] { key });

        public static IDictionary<string, TimeSpan> GracePeriodInvalidData => new Dictionary<string, TimeSpan>
        {
            { "MinValue", TimeSpan.MinValue },
            { "NegativeOneTicks", TimeSpan.FromTicks(-1) },
            { "Zero", TimeSpan.Zero },
            { "HeartbeatInterval", Heartbeat.Interval },
        };
    }
}
