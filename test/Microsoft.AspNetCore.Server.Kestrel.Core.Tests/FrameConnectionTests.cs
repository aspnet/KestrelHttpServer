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
        private static readonly TimeSpan RequestBodyTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RequestBodyExtendedTimeout = TimeSpan.FromSeconds(10);
        private static readonly double RequestBodyMinimumDataRate = 100;

        private readonly PipeFactory _pipeFactory;
        private readonly FrameConnectionContext _frameConnectionContext;
        private readonly FrameConnection _frameConnection;

        public FrameConnectionTests()
        {
            _pipeFactory = new PipeFactory();

            _frameConnectionContext = new FrameConnectionContext
            {
                ConnectionId = "ConnectionId",
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

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyTimeout = RequestBodyTimeout;

            _frameConnection = new FrameConnection(_frameConnectionContext);
        }

        public void Dispose()
        {
            _pipeFactory.Dispose();
        }

        [Fact]
        public async Task AbortsConnectionWhenRequestBodyExceedsTimeout()
        {
            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.StartRequestProcessing(new DummyApplication(context => Task.CompletedTask));
            _frameConnection.StartTimingReads();

            // Tick beyond timeout
            var now = DateTimeOffset.UtcNow;
            _frameConnection.Tick(now + RequestBodyTimeout + TimeSpan.FromSeconds(1));

            // Timed out
            mockLogger.Verify(logger =>
                logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), RequestBodyTimeout), Times.Once);

            // Frame.Abort() cancels the output
            var result = await _frameConnection.Output.ReadAsync();
            Assert.True(result.IsCancelled);

            _frameConnection.OnConnectionClosed(null);
            await _frameConnection.StopAsync();
        }

        [Fact]
        public async Task AbortsConnectionWhenRequestBodyExceedsExtendedTimeout()
        {
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyExtendedTimeout = RequestBodyExtendedTimeout;
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyMinimumDataRate = RequestBodyMinimumDataRate;

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.StartRequestProcessing(new DummyApplication(context => Task.CompletedTask));
            _frameConnection.StartTimingReads();

            // Tick beyond maximum timeout w/ satisfactory data rate
            var now = DateTimeOffset.UtcNow;
            var future = now + RequestBodyExtendedTimeout + TimeSpan.FromSeconds(1);
            _frameConnection.BytesRead((int)(RequestBodyMinimumDataRate * 2 * (future - now).TotalSeconds));
            _frameConnection.Tick(future);

            // Timed out
            mockLogger.Verify(logger =>
                logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), RequestBodyExtendedTimeout), Times.Once);

            // Frame.Abort() cancels the output
            var result = await _frameConnection.Output.ReadAsync();
            Assert.True(result.IsCancelled);

            _frameConnection.OnConnectionClosed(null);
            await _frameConnection.StopAsync();
        }

        [Fact]
        public async Task AbortsConnectionWhenRequestBodyDoesNotSatisfyMinimumDataRate()
        {
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyExtendedTimeout = RequestBodyExtendedTimeout;
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyMinimumDataRate = RequestBodyMinimumDataRate;

            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            var now = DateTimeOffset.UtcNow;

            _frameConnection.StartRequestProcessing(new DummyApplication(context => Task.CompletedTask));
            _frameConnection.StartTimingReads();

            // Tick beyond minimum timeout period w/ low data rate
            now += RequestBodyTimeout + TimeSpan.FromSeconds(1);
            _frameConnection.BytesRead(1);
            _frameConnection.Tick(now);

            // Timed out
            mockLogger.Verify(logger =>
                logger.RequestBodyMininumRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), RequestBodyMinimumDataRate), Times.Once);

            // Frame.Abort() cancels the output
            var result = await _frameConnection.Output.ReadAsync();
            Assert.True(result.IsCancelled);

            _frameConnection.OnConnectionClosed(null);
            await _frameConnection.StopAsync();
        }

        [Fact]
        public async Task PausedTimeDoesNotCountAgainstRequestBodyTimeout()
        {
            var mockLogger = new Mock<IKestrelTrace>();
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            _frameConnection.StartRequestProcessing(new DummyApplication(context => Task.CompletedTask));
            _frameConnection.StartTimingReads();

            var now = DateTimeOffset.UtcNow;
            _frameConnection.Tick(now);

            // Pause and tick within timeout period
            _frameConnection.PauseTimingReads();
            now += TimeSpan.FromSeconds(1);
            _frameConnection.Tick(now);

            // Not timed out
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            // Tick while paused in what would be beyond the timeout period 
            now += RequestBodyTimeout;
            _frameConnection.Tick(now);

            // Not timed out
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            // Resume and tick within resumed timeout period
            _frameConnection.ResumeTimingReads();
            now += TimeSpan.FromSeconds(1);
            _frameConnection.Tick(now);

            // Not timed out
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);

            // Tick beyond timeout period after resuming
            now += RequestBodyTimeout;
            _frameConnection.Tick(now);

            // Timed out
            mockLogger.Verify(logger => logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);

            // Frame.Abort() cancels the output
            var result = await _frameConnection.Output.ReadAsync();
            Assert.True(result.IsCancelled);

            _frameConnection.OnConnectionClosed(null);
            await _frameConnection.StopAsync();
        }
    }
}
