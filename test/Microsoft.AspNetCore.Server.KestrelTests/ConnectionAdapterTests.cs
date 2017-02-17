// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Adapter;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class ConnectionAdapterTests
    {
        [Fact]
        public async Task CanReadAndWriteWithRewritingConnectionAdapter()
        {
            var adapter = new RewritingConnectionAdapter();
            var listenOptions = new ListenOptions(new IPEndPoint(IPAddress.Loopback, 0))
            {
                ConnectionAdapters = { adapter }
            };

            var serviceContext = new TestServiceContext();

            var sendString = "POST / HTTP/1.0\r\nContent-Length: 12\r\n\r\nHello World?";

            using (var server = new TestServer(TestApp.EchoApp, serviceContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    // "?" changes to "!"
                    await connection.Send(sendString);
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        "Connection: close",
                        $"Date: {serviceContext.DateHeaderValue}",
                        "",
                        "Hello World!");
                }
            }

            Assert.Equal(sendString.Length, adapter.BytesRead);
        }

        [Fact]
        public async Task CanReadAndWriteWithAsyncConnectionAdapter()
        {
            var listenOptions = new ListenOptions(new IPEndPoint(IPAddress.Loopback, 0))
            {
                ConnectionAdapters = { new AsyncConnectionAdapter() }
            };

            var serviceContext = new TestServiceContext();

            using (var server = new TestServer(TestApp.EchoApp, serviceContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "POST / HTTP/1.0",
                        "Content-Length: 12",
                        "",
                        "Hello World?");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        "Connection: close",
                        $"Date: {serviceContext.DateHeaderValue}",
                        "",
                        "Hello World!");
                }
            }
        }

        [Fact]
        public async Task ThrowingSynchronousConnectionAdapterDoesNotCrashServer()
        {
            var listenOptions = new ListenOptions(new IPEndPoint(IPAddress.Loopback, 0))
            {
                ConnectionAdapters = { new ThrowingConnectionAdapter() }
            };

            var serviceContext = new TestServiceContext();

            using (var server = new TestServer(TestApp.EchoApp, serviceContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    // Will throw because the exception in the connection adapter will close the connection.
                    await Assert.ThrowsAsync<IOException>(async () =>
                    {
                        await connection.Send(
                           "POST / HTTP/1.0",
                           "Content-Length: 1000",
                           "\r\n");

                        for (var i = 0; i < 1000; i++)
                        {
                            await connection.Send("a");
                            await Task.Delay(5);
                        }
                    });
                }
            }
        }

        [Fact]
        public async Task ThrowingOnReadDoesNotCauseUnobservedException()
        {
            var unobservedExceptionThrown = false;

            EventHandler<UnobservedTaskExceptionEventArgs> unobservedExceptionHandler = (sender, args) =>
            {
                unobservedExceptionThrown = true;
            };

            try
            {
                TaskScheduler.UnobservedTaskException += unobservedExceptionHandler;

                var adapter = new ThrowOnReadConnectionAdapter();
                var listenOptions = new ListenOptions(new IPEndPoint(IPAddress.Loopback, 0))
                {
                    ConnectionAdapters = { adapter }
                };

                var serviceContext = new TestServiceContext();
                using (var server = new TestServer(TestApp.EchoApp, serviceContext, listenOptions))
                {
                    using (var connection = server.CreateConnection())
                    {
                        try
                        {
                            await connection.Send("GET / HTTP/1.1\r\n\r\n");
                            await connection.ReceiveEnd();
                        }
                        catch (IOException)
                        {
                            // Since Stream.ReadAsync throws, the send may fail to complete
                            // if the server aborts the connection quickly enough.
                        }
                    }
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                Assert.False(unobservedExceptionThrown);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= unobservedExceptionHandler;
            }
        }

        private class RewritingConnectionAdapter : IConnectionAdapter
        {
            private RewritingStream _rewritingStream;

            public Task<IAdaptedConnection> OnConnectionAsync(ConnectionAdapterContext context)
            {
                _rewritingStream = new RewritingStream(context.ConnectionStream);
                return Task.FromResult<IAdaptedConnection>(new AdaptedConnection(_rewritingStream));
            }

            public int BytesRead => _rewritingStream.BytesRead;
       }

        private class AsyncConnectionAdapter : IConnectionAdapter
        {
            public async Task<IAdaptedConnection> OnConnectionAsync(ConnectionAdapterContext context)
            {
                await Task.Delay(100);
                return new AdaptedConnection(new RewritingStream(context.ConnectionStream));
            }
        }

        private class ThrowingConnectionAdapter : IConnectionAdapter
        {
            public Task<IAdaptedConnection> OnConnectionAsync(ConnectionAdapterContext context)
            {
                throw new Exception();
            }
        }

        private class ThrowOnReadConnectionAdapter : IConnectionAdapter
        {
            public Task<IAdaptedConnection> OnConnectionAsync(ConnectionAdapterContext context)
            {
                return Task.FromResult<IAdaptedConnection>(new AdaptedConnection(new ThrowOnReadStream()));
            }
        }

        private class AdaptedConnection : IAdaptedConnection
        {
            public AdaptedConnection(Stream adaptedStream)
            {
                ConnectionStream = adaptedStream;
            }

            public Stream ConnectionStream { get; }

            public void PrepareRequest(IFeatureCollection requestFeatures)
            {
            }
        }

        private class RewritingStream : Stream
        {
            private readonly Stream _innerStream;

            public RewritingStream(Stream innerStream)
            {
                _innerStream = innerStream;
            }

            public int BytesRead { get; private set; }

            public override bool CanRead => _innerStream.CanRead;

            public override bool CanSeek => _innerStream.CanSeek;

            public override bool CanWrite => _innerStream.CanWrite;

            public override long Length => _innerStream.Length;

            public override long Position
            {
                get
                {
                    return _innerStream.Position;
                }
                set
                {
                    _innerStream.Position = value;
                }
            }

            public override void Flush()
            {
                _innerStream.Flush();
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                return _innerStream.FlushAsync(cancellationToken);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var actual = _innerStream.Read(buffer, offset, count);

                BytesRead += actual;

                return actual;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                var actual = await _innerStream.ReadAsync(buffer, offset, count);

                BytesRead += actual;

                return actual;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return _innerStream.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                _innerStream.SetLength(value);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i] == '?')
                    {
                        buffer[i] = (byte)'!';
                    }
                }

                _innerStream.Write(buffer, offset, count);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i] == '?')
                    {
                        buffer[i] = (byte)'!';
                    }
                }

                return _innerStream.WriteAsync(buffer, offset, count, cancellationToken);
            }
        }

        private class ThrowOnReadStream : Stream
        {
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await Task.Delay(1);
                throw new Exception();
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override bool CanRead { get; }
            public override bool CanSeek { get; }
            public override bool CanWrite { get; }
            public override long Length { get; }
            public override long Position { get; set; }
        }
    }
}
