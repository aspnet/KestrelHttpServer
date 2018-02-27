// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using Microsoft.AspNetCore.Protocols.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class PipeOptionsTests
    {
        [Theory]
        [InlineData(10, 10, 10)]
        [InlineData(0, 1, 1)]
        [InlineData(null, 0, 0)]
        public void OutputPipeOptionsConfiguredCorrectly(long? maxResponseBufferSize, long expectedMaximumSizeLow, long expectedMaximumSizeHigh)
        {
            var serviceContext = new TestServiceContext();
            serviceContext.ServerOptions.Limits.MaxResponseBufferSize = maxResponseBufferSize;

            var mockReaderScheduler = Mock.Of<PipeScheduler>();
            var mockAppScheduler = Mock.Of<PipeScheduler>();

            var mockConnectionTransportFeatures = new Mock<IConnectionTransportFeature>();
            mockConnectionTransportFeatures.SetupGet(c => c.OutputReaderScheduler).Returns(mockReaderScheduler);
            mockConnectionTransportFeatures.SetupGet(c => c.ApplicationScheduler).Returns(mockAppScheduler);
            mockConnectionTransportFeatures.SetupGet(c => c.MemoryPool).Returns(new MemoryPool());

            var outputPipeOptions = ConnectionHandler.GetOutputPipeOptions(serviceContext, mockConnectionTransportFeatures.Object, mockAppScheduler);

            Assert.Equal(expectedMaximumSizeLow, outputPipeOptions.ResumeWriterThreshold);
            Assert.Equal(expectedMaximumSizeHigh, outputPipeOptions.PauseWriterThreshold);
            Assert.Same(mockReaderScheduler, outputPipeOptions.ReaderScheduler);
            Assert.Same(mockAppScheduler, outputPipeOptions.WriterScheduler);
        }

        [Theory]
        [InlineData(10, 10, 10)]
        [InlineData(null, 0, 0)]
        public void InputPipeOptionsConfiguredCorrectly(long? maxRequestBufferSize, long expectedMaximumSizeLow, long expectedMaximumSizeHigh)
        {
            var serviceContext = new TestServiceContext();
            serviceContext.ServerOptions.Limits.MaxRequestBufferSize = maxRequestBufferSize;

            var mockWriterScheduler = Mock.Of<PipeScheduler>();
            var mockAppScheduler = Mock.Of<PipeScheduler>();

            var mockConnectionTransportFeatures = new Mock<IConnectionTransportFeature>();
            mockConnectionTransportFeatures.SetupGet(c => c.InputWriterScheduler).Returns(mockWriterScheduler);
            mockConnectionTransportFeatures.SetupGet(c => c.ApplicationScheduler).Returns(mockAppScheduler);
            mockConnectionTransportFeatures.SetupGet(c => c.MemoryPool).Returns(new MemoryPool());

            var inputPipeOptions = ConnectionHandler.GetInputPipeOptions(serviceContext, mockConnectionTransportFeatures.Object, mockAppScheduler);

            Assert.Equal(expectedMaximumSizeLow, inputPipeOptions.ResumeWriterThreshold);
            Assert.Equal(expectedMaximumSizeHigh, inputPipeOptions.PauseWriterThreshold);
            Assert.Same(mockAppScheduler, inputPipeOptions.ReaderScheduler);
            Assert.Same(mockWriterScheduler, inputPipeOptions.WriterScheduler);
        }

        [Theory]
        [InlineData(10, 10, 10)]
        [InlineData(null, 0, 0)]
        public void AdaptedInputPipeOptionsConfiguredCorrectly(long? maxRequestBufferSize, long expectedMaximumSizeLow, long expectedMaximumSizeHigh)
        {
            var serviceContext = new TestServiceContext();
            serviceContext.ServerOptions.Limits.MaxRequestBufferSize = maxRequestBufferSize;

            var mockAppScheduler = Mock.Of<PipeScheduler>();

            var connectionLifetime = new HttpConnection(new HttpConnectionContext
            {
                ServiceContext = serviceContext,
                ApplicationScheduler = mockAppScheduler
            });

            Assert.Equal(expectedMaximumSizeLow, connectionLifetime.AdaptedInputPipeOptions.ResumeWriterThreshold);
            Assert.Equal(expectedMaximumSizeHigh, connectionLifetime.AdaptedInputPipeOptions.PauseWriterThreshold);
            Assert.Same(mockAppScheduler, connectionLifetime.AdaptedInputPipeOptions.ReaderScheduler);
            Assert.Same(PipeScheduler.Inline, connectionLifetime.AdaptedInputPipeOptions.WriterScheduler);
        }

        [Theory]
        [InlineData(10, 10, 10)]
        [InlineData(null, 0, 0)]
        public void AdaptedOutputPipeOptionsConfiguredCorrectly(long? maxRequestBufferSize, long expectedMaximumSizeLow, long expectedMaximumSizeHigh)
        {
            var serviceContext = new TestServiceContext();
            serviceContext.ServerOptions.Limits.MaxResponseBufferSize = maxRequestBufferSize;

            var mockAppScheduler = Mock.Of<PipeScheduler>();

            var connectionLifetime = new HttpConnection(new HttpConnectionContext
            {
                ServiceContext = serviceContext,
                ApplicationScheduler = mockAppScheduler
            });

            Assert.Equal(expectedMaximumSizeLow, connectionLifetime.AdaptedOutputPipeOptions.ResumeWriterThreshold);
            Assert.Equal(expectedMaximumSizeHigh, connectionLifetime.AdaptedOutputPipeOptions.PauseWriterThreshold);
            Assert.Same(PipeScheduler.Inline, connectionLifetime.AdaptedOutputPipeOptions.ReaderScheduler);
            Assert.Same(PipeScheduler.Inline, connectionLifetime.AdaptedOutputPipeOptions.WriterScheduler);
        }
    }
}
