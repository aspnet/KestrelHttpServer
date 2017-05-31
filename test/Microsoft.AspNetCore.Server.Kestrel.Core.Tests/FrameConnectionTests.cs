// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
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
        private static readonly TimeSpan RequestBodyMinimumTime = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RequestBodyMaximumTime = TimeSpan.FromSeconds(10);
        private static readonly double RequestBodyMinimumRate = 100;

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

            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyTimeout.MinimumTime = RequestBodyMinimumTime;

            _frameConnection = new FrameConnection(_frameConnectionContext);
        }

        public void Dispose()
        {
            _pipeFactory.Dispose();
        }

        [Fact]
        public async Task AbortsConnectionWhenRequestBodyExceedsMinimumTimeout()
        {
            var logEvent = new ManualResetEventSlim();
            var mockLogger = new Mock<IKestrelTrace>();
            mockLogger
                .Setup(logger =>
                    logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), RequestBodyMinimumTime.TotalSeconds))
                .Callback(() => logEvent.Set());
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            var now = DateTimeOffset.UtcNow;

            _frameConnection.StartRequestProcessing(new DummyApplication(context => Task.CompletedTask));
            _frameConnection.StartTimingReads();

            // Tick beyond timeout
            _frameConnection.Tick(now + RequestBodyMinimumTime + Heartbeat.Interval);

            Assert.True(logEvent.Wait(TimeSpan.FromSeconds(10)));

            var result = await _frameConnection.Output.ReadAsync();

            // Frame.Abort() cancels the output
            Assert.True(result.IsCancelled);

            _frameConnection.OnConnectionClosed(null);
            await _frameConnection.StopAsync();
        }

        [Fact]
        public async Task AbortsConnectionWhenRequestBodyExceedsMaximumTimeout()
        {
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyTimeout.MaximumTime = RequestBodyMaximumTime;
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyTimeout.MinimumRate = RequestBodyMinimumRate;

            var logEvent = new ManualResetEventSlim();
            var mockLogger = new Mock<IKestrelTrace>();
            mockLogger
                .Setup(logger =>
                    logger.RequestBodyTimeout(It.IsAny<string>(), It.IsAny<string>(), RequestBodyMaximumTime.TotalSeconds))
                .Callback(() => logEvent.Set());
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            var now = DateTimeOffset.UtcNow;

            _frameConnection.StartRequestProcessing(new DummyApplication(context => Task.CompletedTask));
            _frameConnection.StartTimingReads();

            // Tick beyond maximum timeout w/ satisfactory data rate
            var future = now + RequestBodyMaximumTime + Heartbeat.Interval;
            _frameConnection.BytesRead((int)(RequestBodyMinimumRate * 2 * (future - now).TotalSeconds));
            _frameConnection.Tick(future);

            Assert.True(logEvent.Wait(TimeSpan.FromSeconds(10)));

            var result = await _frameConnection.Output.ReadAsync();

            // Frame.Abort() cancels the output
            Assert.True(result.IsCancelled);

            _frameConnection.OnConnectionClosed(null);
            await _frameConnection.StopAsync();
        }

        [Fact]
        public async Task AbortsConnectionWhenRequestBodyDoesNotSatisfyMinimumDataRate()
        {
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyTimeout.MaximumTime = RequestBodyMaximumTime;
            _frameConnectionContext.ServiceContext.ServerOptions.Limits.DefaultRequestBodyTimeout.MinimumRate = RequestBodyMinimumRate;

            var logEvent = new ManualResetEventSlim();
            var mockLogger = new Mock<IKestrelTrace>();
            mockLogger
                .Setup(logger =>
                    logger.RequestBodyMininumRateNotSatisfied(It.IsAny<string>(), It.IsAny<string>(), RequestBodyMinimumRate))
                .Callback(() => logEvent.Set());
            _frameConnectionContext.ServiceContext.Log = mockLogger.Object;

            var now = DateTimeOffset.UtcNow;

            _frameConnection.StartRequestProcessing(new DummyApplication(context => Task.CompletedTask));
            _frameConnection.StartTimingReads();

            // Tick beyond minimum timeout w/ low data rate
            _frameConnection.BytesRead(1);
            _frameConnection.Tick(now + RequestBodyMinimumTime + Heartbeat.Interval);

            Assert.True(logEvent.Wait(TimeSpan.FromSeconds(10)));

            var result = await _frameConnection.Output.ReadAsync();

            // Frame.Abort() cancels the output
            Assert.True(result.IsCancelled);

            _frameConnection.OnConnectionClosed(null);
            await _frameConnection.StopAsync();
        }
    }
}
