// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Http.Features;
using Microsoft.AspNet.Server.Kestrel.Filter;
using Microsoft.AspNet.Testing.xunit;
using Xunit;

namespace Microsoft.AspNet.Server.KestrelTests
{
    public class ConnectionFilterTests
    {
        private async Task App(HttpContext httpContext)
        {
            var request = httpContext.Request;
            var response = httpContext.Response;
            response.Headers.Clear();
            while (true)
            {
                var buffer = new byte[8192];
                var count = await request.Body.ReadAsync(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    break;
                }
                await response.Body.WriteAsync(buffer, 0, count);
            }
        }

        [ConditionalFact]
        [FrameworkSkipCondition(RuntimeFrameworks.Mono, SkipReason = "Test hangs after execution on Mono.")]
        public async Task CanReadAndWriteWithRewritingConnectionFilter()
        {
            var filter = new RewritingConnectionFilter();
            var serviceContext = new TestServiceContext()
            {
                ConnectionFilter = filter
            };
            var sendString = "POST / HTTP/1.0\r\n\r\nHello World?";

            using (var server = new TestServer(App, serviceContext))
            {
                using (var connection = new TestConnection())
                {
                    // "?" changes to "!"
                    await connection.SendEnd(sendString);
                    await connection.ReceiveEnd(
                        "HTTP/1.0 200 OK",
                        "",
                        "Hello World!");
                }
            }

            Assert.Equal(sendString.Length, filter.BytesRead);
        }

        [ConditionalFact]
        [FrameworkSkipCondition(RuntimeFrameworks.Mono, SkipReason = "Test hangs after execution on Mono.")]
        public async Task CanReadAndWriteWithAsyncConnectionFilter()
        {
            var serviceContext = new TestServiceContext()
            {
                ConnectionFilter = new AsyncConnectionFilter()
            };

            using (var server = new TestServer(App, serviceContext))
            {
                using (var connection = new TestConnection())
                {
                    await connection.SendEnd(
                        "POST / HTTP/1.0",
                        "",
                        "Hello World?");
                    await connection.ReceiveEnd(
                        "HTTP/1.0 200 OK",
                        "",
                        "Hello World!");
                }
            }
        }

        [ConditionalFact]
        [FrameworkSkipCondition(RuntimeFrameworks.Mono, SkipReason = "Test hangs after execution on Mono.")]
        public async Task ThrowingSynchronousConnectionFilterDoesNotCrashServer()
        {
            var serviceContext = new TestServiceContext()
            {
                ConnectionFilter = new ThrowingConnectionFilter()
            };

            using (var server = new TestServer(App, serviceContext))
            {
                using (var connection = new TestConnection())
                {
                    try
                    {
                        await connection.SendEnd(
                            "POST / HTTP/1.0",
                            "",
                            "Hello World?");
                    }
                    catch (IOException)
                    {
                        // Will throw because the exception in the connection filter will close the connection.
                        Assert.True(true);
                    }
                }
            }
        }

        private class RewritingConnectionFilter : IConnectionFilter
        {
            private static Task _empty = Task.FromResult<object>(null);

            private RewritingStream _rewritingStream;

            public Task OnConnection(ConnectionFilterContext context)
            {
                _rewritingStream = new RewritingStream(context.Connection);
                context.Connection = _rewritingStream;
                return _empty;
            }

            public int BytesRead => _rewritingStream.BytesRead;
        }

        private class AsyncConnectionFilter : IConnectionFilter
        {
            public async Task OnConnection(ConnectionFilterContext context)
            {
                var oldConnection = context.Connection;

                // Set Connection to null to ensure it isn't used until the returned task completes.
                context.Connection = null;
                await Task.Delay(100);

                context.Connection = new RewritingStream(oldConnection);
            }
        }

        private class ThrowingConnectionFilter : IConnectionFilter
        {
            public Task OnConnection(ConnectionFilterContext context)
            {
                throw new Exception();
            }
        }

        private class RewritingStream : IDuplexStreamAsync<byte>
        {
            private readonly IDuplexStreamAsync<byte> _inner;

            public RewritingStream(IDuplexStreamAsync<byte> inner)
            {
                _inner = inner;
            }

            public int BytesRead { get; private set; }


            public Task FlushAsync()
                => FlushAsync(CancellationToken.None);

            public Task FlushAsync(CancellationToken cancellationToken)
                => _inner.FlushAsync(cancellationToken);

            public ValueTask<int> ReadAsync(byte[] buffer, int offset, int count)
                => ReadAsync(buffer, offset, count, CancellationToken.None);

            public ValueTask<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                var actual = _innerRead(buffer, offset, count);

                BytesRead += actual;
                
                return _inner.ReadAsync(buffer, offset, count, cancellationToken);
            }

            public Task WriteAsync(byte[] buffer, int offset, int count)
                => WriteAsync(buffer, offset, count);

            public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i] == '?')
                    {
                        buffer[i] = (byte)'!';
                    }
                }
                return _inner.WriteAsync(buffer, offset, count, cancellationToken);
            }

            public void Dispose()
                => _inner.Dispose();
        }
    }
}
