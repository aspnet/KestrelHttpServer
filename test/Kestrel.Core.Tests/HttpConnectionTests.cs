// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Testing;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class HttpConnectionTests
    {
        [Fact]
        public async Task WriteDataRateTimeoutAbortsConnection()
        {
            var mockConnectionContext = new Mock<ConnectionContext>();

            var httpConnectionContext = new HttpConnectionContext
            {
                ConnectionContext = mockConnectionContext.Object,
                Transport = new DuplexPipe(Mock.Of<PipeReader>(), Mock.Of<PipeWriter>()),
                ServiceContext = new TestServiceContext()
            };

            var httpConnection = new HttpConnection(httpConnectionContext);

            var aborted = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            httpConnection.Initialize(httpConnectionContext.Transport);
            httpConnection.Http1Connection.Reset();
            httpConnection.Http1Connection.RequestAborted.Register(() =>
            {
                aborted.SetResult(null);
            });

            httpConnection.OnTimeout(TimeoutReason.WriteDataRate);

            mockConnectionContext
                .Verify(c => c.Abort(It.Is<ConnectionAbortedException>(ex => ex.Message == CoreStrings.ConnectionTimedBecauseResponseMininumDataRateNotSatisfied)),
                    Times.Once);

            await aborted.Task.DefaultTimeout();
        }
    }
}
