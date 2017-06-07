// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Testing;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class FrameConnectionTests : IDisposable
    {
        private readonly PipeFactory _pipeFactory;
        private readonly FrameConnectionContext _frameConnectionContext;
        private readonly FrameConnection _frameConnection;

        public FrameConnectionTests()
        {
            _pipeFactory = new PipeFactory();

            _frameConnectionContext = new FrameConnectionContext
            {
                ConnectionId = "0123456789",
                ConnectionAdapters = new List<IConnectionAdapter>(),
                ConnectionInformation = new MockConnectionInformation
                {
                    PipeFactory = _pipeFactory
                },
                FrameConnectionId = long.MinValue,
                Input = _pipeFactory.Create(),
                Output = _pipeFactory.Create(),
                ServiceContext = new TestServiceContext
                {
                    SystemClock = new SystemClock()
                }
            };

            _frameConnection = new FrameConnection(_frameConnectionContext);
        }

        public void Dispose()
        {
            _pipeFactory.Dispose();
        }

        [Fact]
        public void TimesOutWhenRequestBodyExceedsTimeout()
        {
            var requestBodyTimeout = TimeSpan.FromSeconds(5);

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.RequestBodyTimeout = requestBodyTimeout;

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.CreateFrame(new DummyApplication(context => Task.CompletedTask), _frameConnectionContext.Input.Reader, _frameConnectionContext.Output);
            _frameConnection.Frame.Reset();

            // Initialize timestamp
            var now = DateTimeOffset.UtcNow;
            _frameConnection.Tick(now);

            _frameConnection.StartTimingReads();

            // Tick after timeout period
            _frameConnection.Tick(now + requestBodyTimeout + TimeSpan.FromSeconds(1));

            // Timed out
            Assert.True(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), requestBodyTimeout), Times.Once);
        }

        [Fact]
        public void TimesOutWhenRequestBodyDoesNotSatisfyMinimumDataRate()
        {
            var requestBodyMinimumDataRate = 100;
            var requestBodyGracePeriod = TimeSpan.FromSeconds(5);
            var requestBodyTimeout = TimeSpan.FromSeconds(10);

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.RequestBodyTimeout = requestBodyTimeout;
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.RequestBodyMinimumDataRate = new MinimumDataRate(requestBodyMinimumDataRate, requestBodyGracePeriod);

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.CreateFrame(new DummyApplication(context => Task.CompletedTask), _frameConnectionContext.Input.Reader, _frameConnectionContext.Output);
            _frameConnection.Frame.Reset();

            // Initialize timestamp
            var now = DateTimeOffset.UtcNow;
            _frameConnection.Tick(now);

            _frameConnection.StartTimingReads();

            // Tick after grace period w/ low data rate
            now += requestBodyGracePeriod + TimeSpan.FromSeconds(1);
            _frameConnection.BytesRead(1);
            _frameConnection.Tick(now);

            // Timed out
            Assert.True(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumDataRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Once);
        }

        [Fact]
        public void MinimumDataRateNotEnforcedDuringGracePeriod()
        {
            var requestBodyMinimumDataRate = 100;
            var requestBodyGracePeriod = TimeSpan.FromSeconds(2);
            var requestBodyTimeout = TimeSpan.MaxValue;

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.RequestBodyTimeout = requestBodyTimeout;
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.RequestBodyMinimumDataRate = new MinimumDataRate(requestBodyMinimumDataRate, requestBodyGracePeriod);

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.CreateFrame(new DummyApplication(context => Task.CompletedTask), _frameConnectionContext.Input.Reader, _frameConnectionContext.Output);
            _frameConnection.Frame.Reset();

            // Initialize timestamp
            var now = DateTimeOffset.UtcNow;
            _frameConnection.Tick(now);

            _frameConnection.StartTimingReads();

            // Tick during grace period w/ low data rate
            now += TimeSpan.FromSeconds(1);
            _frameConnection.BytesRead(10);
            _frameConnection.Tick(now);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumDataRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Never);

            // Tick after grace period w/ low data rate
            now += TimeSpan.FromSeconds(2);
            _frameConnection.BytesRead(10);
            _frameConnection.Tick(now);

            // Timed out
            Assert.True(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumDataRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Once);
        }

        [Fact]
        public void DataRateIsAveragedOverTimeSpentReadingRequestBody()
        {
            var requestBodyMinimumDataRate = 100;
            var requestBodyGracePeriod = TimeSpan.FromSeconds(1);
            var requestBodyTimeout = TimeSpan.MaxValue;

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.RequestBodyTimeout = requestBodyTimeout;
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.RequestBodyMinimumDataRate = new MinimumDataRate(requestBodyMinimumDataRate, requestBodyGracePeriod);

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.CreateFrame(new DummyApplication(context => Task.CompletedTask), _frameConnectionContext.Input.Reader, _frameConnectionContext.Output);
            _frameConnection.Frame.Reset();

            // Initialize timestamp
            var now = DateTimeOffset.UtcNow;
            _frameConnection.Tick(now);

            _frameConnection.StartTimingReads();

            // Tick after grace period to start enforcing minimum data rate
            now += requestBodyGracePeriod;
            _frameConnection.BytesRead(100);
            _frameConnection.Tick(now);

            // Data rate: 200 bytes/second
            now += TimeSpan.FromSeconds(1);
            _frameConnection.BytesRead(300);
            _frameConnection.Tick(now);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumDataRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Never);

            // Data rate: 150 bytes/second
            now += TimeSpan.FromSeconds(1);
            _frameConnection.BytesRead(50);
            _frameConnection.Tick(now);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumDataRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Never);

            // Data rate: 115 bytes/second
            now += TimeSpan.FromSeconds(1);
            _frameConnection.BytesRead(10);
            _frameConnection.Tick(now);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumDataRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Never);

            // Data rate: 50 bytes/second
            now += TimeSpan.FromSeconds(6);
            _frameConnection.BytesRead(40);
            _frameConnection.Tick(now);

            // Timed out
            Assert.True(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumDataRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Once);
        }

        [Fact]
        public void PausedTimeDoesNotCountAgainstRequestBodyTimeout()
        {
            var requestBodyTimeout = TimeSpan.FromSeconds(5);
            var systemClock = new MockSystemClock();

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.RequestBodyTimeout = requestBodyTimeout;
            _frameConnectionContext.ServiceContext.SystemClock = systemClock;

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.CreateFrame(new DummyApplication(context => Task.CompletedTask), _frameConnectionContext.Input.Reader, _frameConnectionContext.Output);
            _frameConnection.Frame.Reset();

            // Initialize timestamp
            _frameConnection.Tick(systemClock.UtcNow);

            _frameConnection.StartTimingReads();

            // Tick at 1s, expected counted time is 1s
            systemClock.UtcNow += TimeSpan.FromSeconds(1);
            _frameConnection.Tick(systemClock.UtcNow);

            // Pause at 1.5s
            systemClock.UtcNow += TimeSpan.FromSeconds(0.5);
            _frameConnection.PauseTimingReads();

            // Tick at 2s, expected counted time is 2s
            systemClock.UtcNow += TimeSpan.FromSeconds(0.5);
            _frameConnection.Tick(systemClock.UtcNow);

            // Tick at 6s, expected counted time is 2s
            systemClock.UtcNow += TimeSpan.FromSeconds(4);
            _frameConnection.Tick(systemClock.UtcNow);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            // Resume at 6.5s
            systemClock.UtcNow += TimeSpan.FromSeconds(0.5);
            _frameConnection.ResumeTimingReads();

            // Tick at 7s, expected counted time is 3s
            systemClock.UtcNow += TimeSpan.FromSeconds(0.5);
            _frameConnection.Tick(systemClock.UtcNow);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            // Tick just past 9s, expected counted time is just over 5s
            systemClock.UtcNow += TimeSpan.FromSeconds(2) + TimeSpan.FromTicks(1);
            _frameConnection.Tick(systemClock.UtcNow);

            // Timed out
            Assert.True(_frameConnection.TimedOut);
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);
        }

        [Fact]
        public void NotPausedWhenResumeCalledBeforeNextTick()
        {
            var requestBodyTimeout = TimeSpan.FromSeconds(5);
            var systemClock = new MockSystemClock();

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.RequestBodyTimeout = requestBodyTimeout;
            _frameConnectionContext.ServiceContext.SystemClock = systemClock;

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.CreateFrame(new DummyApplication(context => Task.CompletedTask), _frameConnectionContext.Input.Reader, _frameConnectionContext.Output);
            _frameConnection.Frame.Reset();

            // Initialize timestamp
            _frameConnection.Tick(systemClock.UtcNow);

            _frameConnection.StartTimingReads();

            // Tick at 1s, expected counted time is 1s
            systemClock.UtcNow += TimeSpan.FromSeconds(1);
            _frameConnection.Tick(systemClock.UtcNow);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            // Pause at 1.25s
            systemClock.UtcNow += TimeSpan.FromSeconds(0.25);
            _frameConnection.PauseTimingReads();

            // Resume at 1.5s
            systemClock.UtcNow += TimeSpan.FromSeconds(0.25);
            _frameConnection.ResumeTimingReads();

            // Tick at 2s, expected counted time is 2s
            systemClock.UtcNow += TimeSpan.FromSeconds(0.5);
            _frameConnection.Tick(systemClock.UtcNow);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            // Tick just past 5s, expected counted time is just over 5s
            systemClock.UtcNow += TimeSpan.FromSeconds(3) + TimeSpan.FromTicks(1);
            _frameConnection.Tick(systemClock.UtcNow);

            // Timed out
            Assert.True(_frameConnection.TimedOut);
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);
        }
    }
}
