// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;
using Microsoft.AspNetCore.Server.Kestrel.Transport;
using Microsoft.AspNetCore.Server.KestrelTests.TestHelpers;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class ConnectionTests
    {
        [Fact]
        public async Task DoesNotEndConnectionOnZeroRead()
        {

            using (var testConnectionHandler = new TestConnectionHandler())
            {
                var mockLibuv = new MockLibuv();
                var serviceContext = new TestServiceContext();
                serviceContext.TransportContext.ConnectionHandler = testConnectionHandler;

                var engine = new KestrelEngine(mockLibuv, serviceContext.TransportContext, null);
                var thread = new KestrelThread(engine);

                try
                {
                    await thread.StartAsync();
                    await thread.PostAsync(_ =>
                    {
                        var listenerContext = new ListenerContext(serviceContext.TransportContext)
                        {
                            Thread = thread
                        };
                        var socket = new MockSocket(mockLibuv, Thread.CurrentThread.ManagedThreadId, serviceContext.Log);
                        var connection = new Connection(listenerContext, socket);
                        connection.Start();

                        LibuvFunctions.uv_buf_t ignored;
                        mockLibuv.AllocCallback(socket.InternalGetHandle(), 2048, out ignored);
                        mockLibuv.ReadCallback(socket.InternalGetHandle(), 0, ref ignored);
                    }, (object)null);

                    var readAwaitable = await testConnectionHandler.Input.Reader.ReadAsync();
                    Assert.False(readAwaitable.IsCompleted);
                }
                finally
                {
                    await thread.StopAsync(TimeSpan.FromSeconds(1));
                }
            }
        }

        private class TestConnectionHandler : IConnectionHandler, IDisposable
        {
            private readonly PipeFactory _pipeFactory;

            public IPipe Input;

            public TestConnectionHandler()
            {
                _pipeFactory = new PipeFactory();
            }

            public IConnectionContext OnConnection(IConnectionInformation connectionInfo, IScheduler inputWriterScheduler, IScheduler outputReaderScheduler)
            {
                Assert.Null(Input);

                Input = _pipeFactory.Create();

                return new TestConnectionContext
                {
                    Input = Input.Writer,
                };
            }

            public void Dispose()
            {
                Input?.Writer.Complete();
                _pipeFactory.Dispose();
            }

            private class TestConnectionContext : IConnectionContext
            {
                public string ConnectionId { get; }
                public IPipeWriter Input { get; set; }
                public IPipeReader Output { get; set; }

                public Task StopAsync()
                {
                    throw new NotImplementedException();
                }

                public void Abort(Exception ex)
                {
                    throw new NotImplementedException();
                }

                public void SetBadRequestState(RequestRejectionReason reason)
                {
                    throw new NotImplementedException();
                }
            }
        }
    }
}