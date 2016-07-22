﻿using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;
using Microsoft.AspNetCore.Server.KestrelTests.TestHelpers;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class ConnectionTests
    {
        [Fact]
        public void DoesNotEndConnectionOnZeroRead()
        {
            var mockLibuv = new MockLibuv();

            using (var engine = new KestrelEngine(mockLibuv, new TestServiceContext()))
            {
                engine.Start(count: 1);

                var trace = new TestKestrelTrace();
                var context = new ListenerContext(new TestServiceContext())
                {
                    FrameFactory = connectionContext => new Frame<HttpContext>(
                        new DummyApplication(httpContext => TaskUtilities.CompletedTask), connectionContext),
                    ServerAddress = ServerAddress.FromUrl("http://127.0.0.1:0"),
                    Thread = engine.Threads[0]
                };
                var socket = new MockSocket(mockLibuv, Thread.CurrentThread.ManagedThreadId, trace);
                var connection = new Connection(context, socket);
                connection.Start();

                Libuv.uv_buf_t ignored;
                mockLibuv.AllocCallback(socket.InternalGetHandle(), 2048, out ignored);
                mockLibuv.ReadCallback(socket.InternalGetHandle(), 0, ref ignored);
                Assert.False(connection.SocketInput.CheckFinOrThrow());

                connection.ConnectionControl.End(ProduceEndType.SocketDisconnect);
            }
        }
    }
}
