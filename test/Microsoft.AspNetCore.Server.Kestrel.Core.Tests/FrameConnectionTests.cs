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

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyTimeout = requestBodyTimeout;

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.CreateFrame(new DummyApplication(context => Task.CompletedTask), _frameConnectionContext.Input.Reader, _frameConnectionContext.Output);

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

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyTimeout = requestBodyTimeout;
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.SetDefaultRequestBodyMinimumDataRate(requestBodyMinimumDataRate, requestBodyGracePeriod);

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.CreateFrame(new DummyApplication(context => Task.CompletedTask), _frameConnectionContext.Input.Reader, _frameConnectionContext.Output);

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
                logger.RequestBodyMininumRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Once);
        }

        [Fact]
        public void MinimumDataRateNotEnforcedDuringGracePeriod()
        {
            var requestBodyMinimumDataRate = 100;
            var requestBodyGracePeriod = TimeSpan.FromSeconds(2);
            var requestBodyTimeout = TimeSpan.MaxValue;

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyTimeout = requestBodyTimeout;
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.SetDefaultRequestBodyMinimumDataRate(requestBodyMinimumDataRate, requestBodyGracePeriod);

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.CreateFrame(new DummyApplication(context => Task.CompletedTask), _frameConnectionContext.Input.Reader, _frameConnectionContext.Output);

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
                logger.RequestBodyMininumRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Never);

            // Tick after grace period w/ low data rate
            now += TimeSpan.FromSeconds(2);
            _frameConnection.BytesRead(10);
            _frameConnection.Tick(now);

            // Timed out
            Assert.True(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Once);
        }

        [Fact]
        public void DataRateIsAveragedOverTimeSpentReadingRequestBody()
        {
            var requestBodyMinimumDataRate = 100;
            var requestBodyGracePeriod = TimeSpan.FromSeconds(1);
            var requestBodyTimeout = TimeSpan.MaxValue;

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyTimeout = requestBodyTimeout;
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.SetDefaultRequestBodyMinimumDataRate(requestBodyMinimumDataRate, requestBodyGracePeriod);

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.CreateFrame(new DummyApplication(context => Task.CompletedTask), _frameConnectionContext.Input.Reader, _frameConnectionContext.Output);

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
                logger.RequestBodyMininumRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Never);

            // Data rate: 150 bytes/second
            now += TimeSpan.FromSeconds(1);
            _frameConnection.BytesRead(50);
            _frameConnection.Tick(now);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Never);

            // Data rate: 115 bytes/second
            now += TimeSpan.FromSeconds(1);
            _frameConnection.BytesRead(10);
            _frameConnection.Tick(now);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Never);

            // Data rate: 50 bytes/second
            now += TimeSpan.FromSeconds(6);
            _frameConnection.BytesRead(40);
            _frameConnection.Tick(now);

            // Timed out
            Assert.True(_frameConnection.TimedOut);
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), requestBodyMinimumDataRate), Times.Once);
        }

        [Fact]
        public void PausedTimeDoesNotCountAgainstRequestBodyTimeout()
        {
            var requestBodyTimeout = TimeSpan.FromSeconds(5);

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyTimeout = requestBodyTimeout;

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.CreateFrame(new DummyApplication(context => Task.CompletedTask), _frameConnectionContext.Input.Reader, _frameConnectionContext.Output);

            // Initialize timestamp
            var now = DateTimeOffset.UtcNow;
            _frameConnection.Tick(now);

            _frameConnection.StartTimingReads();

            // Pause and tick within timeout period
            _frameConnection.PauseTimingReads();
            now += TimeSpan.FromSeconds(1);
            _frameConnection.Tick(now);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            // Tick after the timeout period while paused
            now += requestBodyTimeout;
            _frameConnection.Tick(now);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            // Resume and tick within resumed timeout period
            _frameConnection.ResumeTimingReads();
            now += TimeSpan.FromSeconds(1);
            _frameConnection.Tick(now);

            // Not timed out
            Assert.False(_frameConnection.TimedOut);
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            // Tick after timeout period after resuming
            now += requestBodyTimeout;
            _frameConnection.Tick(now);

            // Timed out
            Assert.True(_frameConnection.TimedOut);
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);
        }
    }
}
