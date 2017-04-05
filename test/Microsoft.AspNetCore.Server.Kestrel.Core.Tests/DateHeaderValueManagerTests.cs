// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class DateHeaderValueManagerTests
    {
        /// <summary>
        /// DateTime format string for RFC1123.
        /// </summary>
        /// <remarks>
        /// See https://msdn.microsoft.com/en-us/library/az4se3k1(v=vs.110).aspx#RFC1123 for info on the format.
        /// </remarks>
        private const string Rfc1123DateFormat = "r";

        [Fact]
        public void GetDateHeaderValue_ReturnsDateValueInRFC1123Format()
        {
            var now = DateTimeOffset.UtcNow;
            var systemClock = new MockSystemClock
            {
                UtcNow = now
            };
            var timeWithoutRequestsUntilIdle = TimeSpan.FromSeconds(1);
            var timerInterval = TimeSpan.FromSeconds(10);

            using (var dateHeaderValueManager = new DateHeaderValueManager(systemClock, timeWithoutRequestsUntilIdle))
            using (new Heartbeat(new ITick[] {dateHeaderValueManager}, systemClock, timerInterval, null))
            {
                Assert.Equal(now.ToString(Rfc1123DateFormat), dateHeaderValueManager.GetDateHeaderValues().String);
            }
        }

        [Fact]
        public void GetDateHeaderValue_ReturnsCachedValueBetweenTimerTicks()
        {
            var now = DateTimeOffset.UtcNow;
            var future = now.AddSeconds(10);
            var systemClock = new MockSystemClock
            {
                UtcNow = now
            };
            var timeWithoutRequestsUntilIdle = TimeSpan.FromSeconds(1);
            var timerInterval = TimeSpan.FromSeconds(10);

            using (var dateHeaderValueManager = new DateHeaderValueManager(systemClock, timeWithoutRequestsUntilIdle))
            using (new Heartbeat(new ITick[] {dateHeaderValueManager}, systemClock, timerInterval, null))
            {
                Assert.Equal(now.ToString(Rfc1123DateFormat), dateHeaderValueManager.GetDateHeaderValues().String);
                Assert.Equal(now.ToString(Rfc1123DateFormat), dateHeaderValueManager.GetDateHeaderValues().String);
                Assert.Equal(1, systemClock.UtcNowCalled);
            }
        }

        [Fact]
        public async Task GetDateHeaderValue_ReturnsUpdatedValueAfterIdle()
        {
            var now = DateTimeOffset.UtcNow;
            var future = now.AddSeconds(10);
            var systemClock = new MockSystemClock
            {
                UtcNow = now
            };
            var timeWithoutRequestsUntilIdle = TimeSpan.FromMilliseconds(250);
            var timerInterval = TimeSpan.FromMilliseconds(100);

            using (var dateHeaderValueManager = new DateHeaderValueManager(systemClock, timeWithoutRequestsUntilIdle))
            using (new Heartbeat(new ITick[] {dateHeaderValueManager}, systemClock, timerInterval, null))
            {
                Assert.Equal(now.ToString(Rfc1123DateFormat), dateHeaderValueManager.GetDateHeaderValues().String);

                // Wait for longer than the idle timeout to ensure the timer is stopped
                systemClock.UtcNow = future;
                await Task.Delay(TimeSpan.FromSeconds(1));

                Assert.Equal(future.ToString(Rfc1123DateFormat), dateHeaderValueManager.GetDateHeaderValues().String);
                Assert.True(systemClock.UtcNowCalled >= 2);
            }
        }

        [Fact]
        public void GetDateHeaderValue_ReturnsDateValueAfterDisposed()
        {
            var now = DateTimeOffset.UtcNow;
            var future = now.AddSeconds(10);
            var systemClock = new MockSystemClock
            {
                UtcNow = now
            };
            var timeWithoutRequestsUntilIdle = TimeSpan.FromSeconds(1);
            var timerInterval = TimeSpan.FromSeconds(10);

            DateHeaderValueManager dateHeaderValueManager;

            using (dateHeaderValueManager = new DateHeaderValueManager(systemClock, timeWithoutRequestsUntilIdle))
            using (new Heartbeat(new ITick[] {dateHeaderValueManager}, systemClock, timerInterval, null))
            {

                Assert.Equal(now.ToString(Rfc1123DateFormat), dateHeaderValueManager.GetDateHeaderValues().String);
            }

            systemClock.UtcNow = future;
            Assert.Equal(future.ToString(Rfc1123DateFormat), dateHeaderValueManager.GetDateHeaderValues().String);
        }
    }
}
