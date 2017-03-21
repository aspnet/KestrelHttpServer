// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class ResponseTests
    {
        [Fact]
        public async Task LargeDownload()
        {
            var hostBuilder = new WebHostBuilder()
                .UseKestrel()
                .UseUrls("http://127.0.0.1:0/")
                .Configure(app =>
                {
                    app.Run(async context =>
                    {
                        var bytes = new byte[1024];
                        for (int i = 0; i < bytes.Length; i++)
                        {
                            bytes[i] = (byte)i;
                        }

                        context.Response.ContentLength = bytes.Length * 1024;

                        for (int i = 0; i < 1024; i++)
                        {
                            await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
                        }
                    });
                });

            using (var host = hostBuilder.Build())
            {
                host.Start();

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync($"http://127.0.0.1:{host.GetPort()}/");
                    response.EnsureSuccessStatusCode();
                    var responseBody = await response.Content.ReadAsStreamAsync();

                    // Read the full response body
                    var total = 0;
                    var bytes = new byte[1024];
                    var count = await responseBody.ReadAsync(bytes, 0, bytes.Length);
                    while (count > 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            Assert.Equal(total % 256, bytes[i]);
                            total++;
                        }
                        count = await responseBody.ReadAsync(bytes, 0, bytes.Length);
                    }
                }
            }
        }

        [Theory, MemberData(nameof(NullHeaderData))]
        public async Task IgnoreNullHeaderValues(string headerName, StringValues headerValue, string expectedValue)
        {
            var hostBuilder = new WebHostBuilder()
                .UseKestrel()
                .UseUrls("http://127.0.0.1:0/")
                .Configure(app =>
                {
                    app.Run(async context =>
                    {
                        context.Response.Headers.Add(headerName, headerValue);

                        await context.Response.WriteAsync("");
                    });
                });

            using (var host = hostBuilder.Build())
            {
                host.Start();

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync($"http://127.0.0.1:{host.GetPort()}/");
                    response.EnsureSuccessStatusCode();

                    var headers = response.Headers;

                    if (expectedValue == null)
                    {
                        Assert.False(headers.Contains(headerName));
                    }
                    else
                    {
                        Assert.True(headers.Contains(headerName));
                        Assert.Equal(headers.GetValues(headerName).Single(), expectedValue);
                    }
                }
            }
        }

        [Fact]
        public async Task OnCompleteCalledEvenWhenOnStartingNotCalled()
        {
            var onStartingCalled = false;
            var onCompletedCalled = false;

            var hostBuilder = new WebHostBuilder()
                .UseKestrel()
                .UseUrls("http://127.0.0.1:0/")
                .Configure(app =>
                {
                    app.Run(context =>
                    {
                        context.Response.OnStarting(() => Task.Run(() => onStartingCalled = true));
                        context.Response.OnCompleted(() => Task.Run(() => onCompletedCalled = true));

                        // Prevent OnStarting call (see Frame<T>.RequestProcessingAsync()).
                        throw new Exception();
                    });
                });

            using (var host = hostBuilder.Build())
            {
                host.Start();

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync($"http://127.0.0.1:{host.GetPort()}/");

                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                    Assert.False(onStartingCalled);
                    Assert.True(onCompletedCalled);
                }
            }
        }

        [Fact]
        public async Task OnStartingThrowsWhenSetAfterResponseHasAlreadyStarted()
        {
            InvalidOperationException ex = null;

            var hostBuilder = new WebHostBuilder()
                .UseKestrel()
                .UseUrls("http://127.0.0.1:0/")
                .Configure(app =>
                {
                    app.Run(async context =>
                    {
                        await context.Response.WriteAsync("hello, world");
                        await context.Response.Body.FlushAsync();
                        ex = Assert.Throws<InvalidOperationException>(() => context.Response.OnStarting(_ => TaskCache.CompletedTask, null));
                    });
                });

            using (var host = hostBuilder.Build())
            {
                host.Start();

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync($"http://127.0.0.1:{host.GetPort()}/");

                    // Despite the error, the response had already started
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.NotNull(ex);
                }
            }
        }

        [Fact]
        public Task ResponseStatusCodeSetBeforeHttpContextDisposeAppException()
        {
            return ResponseStatusCodeSetBeforeHttpContextDispose(
                context =>
                {
                    throw new Exception();
                },
                expectedClientStatusCode: HttpStatusCode.InternalServerError,
                expectedServerStatusCode: HttpStatusCode.InternalServerError);
        }

        [Fact]
        public Task ResponseStatusCodeSetBeforeHttpContextDisposeRequestAborted()
        {
            return ResponseStatusCodeSetBeforeHttpContextDispose(
                context =>
                {
                    context.Abort();
                    return TaskCache.CompletedTask;
                },
                expectedClientStatusCode: null,
                expectedServerStatusCode: 0);
        }

        [Fact]
        public Task ResponseStatusCodeSetBeforeHttpContextDisposeRequestAbortedAppException()
        {
            return ResponseStatusCodeSetBeforeHttpContextDispose(
                context =>
                {
                    context.Abort();
                    throw new Exception();
                },
                expectedClientStatusCode: null,
                expectedServerStatusCode: 0);
        }

        [Fact]
        public Task ResponseStatusCodeSetBeforeHttpContextDisposedRequestMalformed()
        {
            return ResponseStatusCodeSetBeforeHttpContextDispose(
                context =>
                {
                    return TaskCache.CompletedTask;
                },
                expectedClientStatusCode: null,
                expectedServerStatusCode: HttpStatusCode.BadRequest,
                sendMalformedRequest: true);
        }

        [Fact]
        public Task ResponseStatusCodeSetBeforeHttpContextDisposedRequestMalformedRead()
        {
            return ResponseStatusCodeSetBeforeHttpContextDispose(
                async context =>
                {
                    await context.Request.Body.ReadAsync(new byte[1], 0, 1);
                },
                expectedClientStatusCode: null,
                expectedServerStatusCode: HttpStatusCode.BadRequest,
                sendMalformedRequest: true);
        }

        [Fact]
        public Task ResponseStatusCodeSetBeforeHttpContextDisposedRequestMalformedReadIgnored()
        {
            return ResponseStatusCodeSetBeforeHttpContextDispose(
                async context =>
                {
                    try
                    {
                        await context.Request.Body.ReadAsync(new byte[1], 0, 1);
                    }
                    catch (BadHttpRequestException)
                    {
                    }
                },
                expectedClientStatusCode: null,
                expectedServerStatusCode: HttpStatusCode.BadRequest,
                sendMalformedRequest: true);
        }

        private static async Task ResponseStatusCodeSetBeforeHttpContextDispose(
            RequestDelegate handler,
            HttpStatusCode? expectedClientStatusCode,
            HttpStatusCode expectedServerStatusCode,
            bool sendMalformedRequest = false)
        {
            var mockHttpContextFactory = new Mock<IHttpContextFactory>();
            mockHttpContextFactory.Setup(f => f.Create(It.IsAny<IFeatureCollection>()))
                .Returns<IFeatureCollection>(fc => new DefaultHttpContext(fc));

            var disposedTcs = new TaskCompletionSource<int>();
            mockHttpContextFactory.Setup(f => f.Dispose(It.IsAny<HttpContext>()))
                .Callback<HttpContext>(c =>
                {
                    disposedTcs.TrySetResult(c.Response.StatusCode);
                });

            using (var server = new TestServer(handler, new TestServiceContext(), mockHttpContextFactory.Object))
            {
                if (!sendMalformedRequest)
                {
                    using (var client = new HttpClient())
                    {
                        try
                        {
                            var response = await client.GetAsync($"http://127.0.0.1:{server.Port}/");
                            Assert.Equal(expectedClientStatusCode, response.StatusCode);
                        }
                        catch
                        {
                            if (expectedClientStatusCode != null)
                            {
                                throw;
                            }
                        }
                    }
                }
                else
                {
                    using (var connection = new TestConnection(server.Port))
                    {
                        await connection.Send(
                            "POST / HTTP/1.1",
                            "Transfer-Encoding: chunked",
                            "",
                            "gg");
                        await connection.ReceiveForcedEnd(
                            "HTTP/1.1 400 Bad Request",
                            "Connection: close",
                            $"Date: {server.Context.DateHeaderValue}",
                            "Content-Length: 0",
                            "",
                            "");
                    }
                }

                var disposedStatusCode = await disposedTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(10));
                Assert.Equal(expectedServerStatusCode, (HttpStatusCode)disposedStatusCode);
            }
        }

        // https://github.com/aspnet/KestrelHttpServer/pull/1111/files#r80584475 explains the reason for this test.
        [Fact]
        public async Task SingleErrorResponseSentWhenAppSwallowsBadRequestException()
        {
            BadHttpRequestException readException = null;

            using (var server = new TestServer(async httpContext =>
            {
                readException = await Assert.ThrowsAsync<BadHttpRequestException>(
                    async () => await httpContext.Request.Body.ReadAsync(new byte[1], 0, 1));
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "POST / HTTP/1.1",
                        "Transfer-Encoding: chunked",
                        "",
                        "gg");
                    await connection.ReceiveForcedEnd(
                        "HTTP/1.1 400 Bad Request",
                        "Connection: close",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }

            Assert.NotNull(readException);
        }

        [Fact]
        public async Task TransferEncodingChunkedSetOnUnknownLengthHttp11Response()
        {
            using (var server = new TestServer(async httpContext =>
            {
                await httpContext.Response.WriteAsync("hello, ");
                await httpContext.Response.WriteAsync("world");
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Transfer-Encoding: chunked",
                        "",
                        "7",
                        "hello, ",
                        "5",
                        "world",
                        "0",
                        "",
                        "");
                }
            }
        }

        [Theory]
        [InlineData(StatusCodes.Status204NoContent)]
        [InlineData(StatusCodes.Status205ResetContent)]
        [InlineData(StatusCodes.Status304NotModified)]
        public async Task TransferEncodingChunkedNotSetOnNonBodyResponse(int statusCode)
        {
            using (var server = new TestServer(httpContext =>
            {
                httpContext.Response.StatusCode = statusCode;
                return TaskCache.CompletedTask;
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        $"HTTP/1.1 {Encoding.ASCII.GetString(ReasonPhrases.ToStatusBytes(statusCode))}",
                        $"Date: {server.Context.DateHeaderValue}",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task TransferEncodingNotSetOnHeadResponse()
        {
            using (var server = new TestServer(httpContext =>
            {
                return TaskCache.CompletedTask;
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "HEAD / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        $"HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task ResponseBodyNotWrittenOnHeadResponseAndLoggedOnlyOnce()
        {
            const string response = "hello, world";

            var logTcs = new TaskCompletionSource<object>();
            var mockKestrelTrace = new Mock<IKestrelTrace>();
            mockKestrelTrace
                .Setup(trace => trace.ConnectionHeadResponseBodyWrite(It.IsAny<string>(), response.Length))
                .Callback<string, long>((connectionId, count) => logTcs.SetResult(null));

            using (var server = new TestServer(async httpContext =>
            {
                await httpContext.Response.WriteAsync(response);
                await httpContext.Response.Body.FlushAsync();
            }, new TestServiceContext { Log = mockKestrelTrace.Object }))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "HEAD / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        $"HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "",
                        "");

                    // Wait for message to be logged before disposing the socket.
                    // Disposing the socket will abort the connection and Frame._requestAborted
                    // might be 1 by the time ProduceEnd() gets called and the message is logged.
                    await logTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(10));
                }
            }

            mockKestrelTrace.Verify(kestrelTrace =>
                kestrelTrace.ConnectionHeadResponseBodyWrite(It.IsAny<string>(), response.Length), Times.Once);
        }

        [Fact]
        public async Task ThrowsAndClosesConnectionWhenAppWritesMoreThanContentLengthWrite()
        {
            var testLogger = new TestApplicationErrorLogger();
            var serviceContext = new TestServiceContext { Log = new TestKestrelTrace(testLogger) };

            using (var server = new TestServer(httpContext =>
            {
                httpContext.Response.ContentLength = 11;
                httpContext.Response.Body.Write(Encoding.ASCII.GetBytes("hello,"), 0, 6);
                httpContext.Response.Body.Write(Encoding.ASCII.GetBytes(" world"), 0, 6);
                return TaskCache.CompletedTask;
            }, serviceContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        $"HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 11",
                        "",
                        "hello,");

                    await connection.WaitForConnectionClose();
                }
            }


            var logMessage = Assert.Single(testLogger.Messages, message => message.LogLevel == LogLevel.Error);

            Assert.Equal(
                $"Response Content-Length mismatch: too many bytes written (12 of 11).",
                logMessage.Exception.Message);

        }

        [Fact]
        public async Task ThrowsAndClosesConnectionWhenAppWritesMoreThanContentLengthWriteAsync()
        {
            var testLogger = new TestApplicationErrorLogger();
            var serviceContext = new TestServiceContext { Log = new TestKestrelTrace(testLogger) };

            using (var server = new TestServer(async httpContext =>
            {
                httpContext.Response.ContentLength = 11;
                await httpContext.Response.WriteAsync("hello,");
                await httpContext.Response.WriteAsync(" world");
            }, serviceContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveForcedEnd(
                        $"HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 11",
                        "",
                        "hello,");
                }
            }

            var logMessage = Assert.Single(testLogger.Messages, message => message.LogLevel == LogLevel.Error);
            Assert.Equal(
                $"Response Content-Length mismatch: too many bytes written (12 of 11).",
                logMessage.Exception.Message);
        }

        [Fact]
        public async Task InternalServerErrorAndConnectionClosedOnWriteWithMoreThanContentLengthAndResponseNotStarted()
        {
            var testLogger = new TestApplicationErrorLogger();
            var serviceContext = new TestServiceContext { Log = new TestKestrelTrace(testLogger) };

            using (var server = new TestServer(httpContext =>
            {
                var response = Encoding.ASCII.GetBytes("hello, world");
                httpContext.Response.ContentLength = 5;
                httpContext.Response.Body.Write(response, 0, response.Length);
                return TaskCache.CompletedTask;
            }, serviceContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveForcedEnd(
                        $"HTTP/1.1 500 Internal Server Error",
                        "Connection: close",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }

            var logMessage = Assert.Single(testLogger.Messages, message => message.LogLevel == LogLevel.Error);
            Assert.Equal(
                $"Response Content-Length mismatch: too many bytes written (12 of 5).",
                logMessage.Exception.Message);
        }

        [Fact]
        public async Task InternalServerErrorAndConnectionClosedOnWriteAsyncWithMoreThanContentLengthAndResponseNotStarted()
        {
            var testLogger = new TestApplicationErrorLogger();
            var serviceContext = new TestServiceContext { Log = new TestKestrelTrace(testLogger) };

            using (var server = new TestServer(httpContext =>
            {
                var response = Encoding.ASCII.GetBytes("hello, world");
                httpContext.Response.ContentLength = 5;
                return httpContext.Response.Body.WriteAsync(response, 0, response.Length);
            }, serviceContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveForcedEnd(
                        $"HTTP/1.1 500 Internal Server Error",
                        "Connection: close",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }

            var logMessage = Assert.Single(testLogger.Messages, message => message.LogLevel == LogLevel.Error);
            Assert.Equal(
                $"Response Content-Length mismatch: too many bytes written (12 of 5).",
                logMessage.Exception.Message);
        }

        [Fact]
        public async Task WhenAppWritesLessThanContentLengthErrorLogged()
        {
            var logTcs = new TaskCompletionSource<object>();
            var mockTrace = new Mock<IKestrelTrace>();
            mockTrace
                .Setup(trace => trace.ApplicationError(It.IsAny<string>(), It.IsAny<InvalidOperationException>()))
                .Callback<string, Exception>((connectionId, ex) =>
                {
                    logTcs.SetResult(null);
                });

            using (var server = new TestServer(async httpContext =>
            {
                httpContext.Response.ContentLength = 13;
                await httpContext.Response.WriteAsync("hello, world");
            }, new TestServiceContext { Log = mockTrace.Object }))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");

                    // Don't use ReceiveEnd here, otherwise the FIN might
                    // abort the request before the server checks the
                    // response content length, in which case the check
                    // will be skipped.
                    await connection.Receive(
                        $"HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 13",
                        "",
                        "hello, world");

                    // Wait for error message to be logged.
                    await logTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(10));

                    // The server should close the connection in this situation.
                    await connection.WaitForConnectionClose().TimeoutAfter(TimeSpan.FromSeconds(10));
                }
            }

            mockTrace.Verify(trace =>
                trace.ApplicationError(
                    It.IsAny<string>(),
                    It.Is<InvalidOperationException>(ex =>
                        ex.Message.Equals($"Response Content-Length mismatch: too few bytes written (12 of 13).", StringComparison.Ordinal))));
        }

        [Fact]
        public async Task WhenAppWritesLessThanContentLengthButRequestIsAbortedErrorNotLogged()
        {
            var abortTcs = new TaskCompletionSource<object>();
            var mockTrace = new Mock<IKestrelTrace>();

            using (var server = new TestServer(async httpContext =>
            {
                httpContext.RequestAborted.Register(() =>
                {
                    abortTcs.SetResult(null);
                });
                httpContext.Response.ContentLength = 12;
                await httpContext.Response.WriteAsync("hello,");

                // Wait until the request is aborted so we know Frame will skip the response content length check.
                await abortTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(10));
            }, new TestServiceContext { Log = mockTrace.Object }))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        $"HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 12",
                        "",
                        "hello,");
                }
            }

            // Verify the request was really aborted. A timeout in the await inside
            // the app func would cause a server error and skip the content length
            // check altogether, making the test pass for the wrong reason.
            Assert.True(abortTcs.Task.IsCompleted);

            // With the server disposed we know all connections were drained and all messages were logged.
            mockTrace.Verify(trace => trace.ApplicationError(It.IsAny<string>(), It.IsAny<InvalidOperationException>()), Times.Never);
        }

        [Fact]
        public async Task WhenAppSetsContentLengthButDoesNotWriteBody500ResponseSentAndConnectionDoesNotClose()
        {
            var testLogger = new TestApplicationErrorLogger();
            var serviceContext = new TestServiceContext { Log = new TestKestrelTrace(testLogger) };

            using (var server = new TestServer(httpContext =>
            {
                httpContext.Response.ContentLength = 5;
                return TaskCache.CompletedTask;
            }, serviceContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 500 Internal Server Error",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "HTTP/1.1 500 Internal Server Error",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }

            var error = testLogger.Messages.Where(message => message.LogLevel == LogLevel.Error);
            Assert.Equal(2, error.Count());
            Assert.All(error, message => message.Equals("Response Content-Length mismatch: too few bytes written (0 of 5)."));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WhenAppSetsContentLengthToZeroAndDoesNotWriteNoErrorIsThrown(bool flushResponse)
        {
            var testLogger = new TestApplicationErrorLogger();
            var serviceContext = new TestServiceContext { Log = new TestKestrelTrace(testLogger) };

            using (var server = new TestServer(async httpContext =>
            {
                httpContext.Response.ContentLength = 0;

                if (flushResponse)
                {
                    await httpContext.Response.Body.FlushAsync();
                }
            }, serviceContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        $"HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 0",
                        "",
                        "");
                }
            }

            Assert.Equal(0, testLogger.ApplicationErrorsLogged);
        }

        // https://tools.ietf.org/html/rfc7230#section-3.3.3
        // If a message is received with both a Transfer-Encoding and a
        // Content-Length header field, the Transfer-Encoding overrides the
        // Content-Length.
        [Fact]
        public async Task WhenAppSetsTransferEncodingAndContentLengthWritingLessIsNotAnError()
        {
            var testLogger = new TestApplicationErrorLogger();
            var serviceContext = new TestServiceContext { Log = new TestKestrelTrace(testLogger) };

            using (var server = new TestServer(async httpContext =>
            {
                httpContext.Response.Headers["Transfer-Encoding"] = "chunked";
                httpContext.Response.ContentLength = 13;
                await httpContext.Response.WriteAsync("hello, world");
            }, serviceContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        $"HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 13",
                        "Transfer-Encoding: chunked",
                        "",
                        "hello, world");
                }
            }

            Assert.Equal(0, testLogger.ApplicationErrorsLogged);
        }

        // https://tools.ietf.org/html/rfc7230#section-3.3.3
        // If a message is received with both a Transfer-Encoding and a
        // Content-Length header field, the Transfer-Encoding overrides the
        // Content-Length.
        [Fact]
        public async Task WhenAppSetsTransferEncodingAndContentLengthWritingMoreIsNotAnError()
        {
            var testLogger = new TestApplicationErrorLogger();
            var serviceContext = new TestServiceContext { Log = new TestKestrelTrace(testLogger) };

            using (var server = new TestServer(async httpContext =>
            {
                httpContext.Response.Headers["Transfer-Encoding"] = "chunked";
                httpContext.Response.ContentLength = 11;
                await httpContext.Response.WriteAsync("hello, world");
            }, serviceContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        $"HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 11",
                        "Transfer-Encoding: chunked",
                        "",
                        "hello, world");
                }
            }

            Assert.Equal(0, testLogger.ApplicationErrorsLogged);
        }

        [Fact]
        public async Task HeadResponseCanContainContentLengthHeader()
        {
            using (var server = new TestServer(httpContext =>
            {
                httpContext.Response.ContentLength = 42;
                return TaskCache.CompletedTask;
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "HEAD / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 42",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task HeadResponseBodyNotWrittenWithAsyncWrite()
        {
            var flushed = new SemaphoreSlim(0, 1);

            using (var server = new TestServer(async httpContext =>
            {
                httpContext.Response.ContentLength = 12;
                await httpContext.Response.WriteAsync("hello, world");
                await flushed.WaitAsync();
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "HEAD / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 12",
                        "",
                        "");

                    flushed.Release();
                }
            }
        }

        [Fact]
        public async Task HeadResponseBodyNotWrittenWithSyncWrite()
        {
            var flushed = new SemaphoreSlim(0, 1);

            using (var server = new TestServer(httpContext =>
            {
                httpContext.Response.ContentLength = 12;
                httpContext.Response.Body.Write(Encoding.ASCII.GetBytes("hello, world"), 0, 12);
                flushed.Wait();
                return TaskCache.CompletedTask;
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "HEAD / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 12",
                        "",
                        "");

                    flushed.Release();
                }
            }
        }

        [Fact]
        public async Task ZeroLengthWritesFlushHeaders()
        {
            var flushed = new SemaphoreSlim(0, 1);

            using (var server = new TestServer(async httpContext =>
            {
                httpContext.Response.ContentLength = 12;
                await httpContext.Response.WriteAsync("");
                flushed.Wait();
                await httpContext.Response.WriteAsync("hello, world");
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        "Content-Length: 12",
                        "",
                        "");

                    flushed.Release();

                    await connection.ReceiveEnd("hello, world");
                }
            }
        }

        [Fact]
        public async Task WriteAfterConnectionCloseNoops()
        {
            var connectionClosed = new ManualResetEventSlim();
            var requestStarted = new ManualResetEventSlim();
            var tcs = new TaskCompletionSource<object>();

            using (var server = new TestServer(async httpContext =>
            {
                try
                {
                    requestStarted.Set();
                    connectionClosed.Wait();
                    httpContext.Response.ContentLength = 12;
                    await httpContext.Response.WriteAsync("hello, world");
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");

                    requestStarted.Wait();
                    connection.Shutdown(SocketShutdown.Send);
                    await connection.WaitForConnectionClose();
                }

                connectionClosed.Set();

                await tcs.Task;
            }
        }

        [Fact]
        public async Task AppCanWriteOwnBadRequestResponse()
        {
            var expectedResponse = string.Empty;
            var responseWritten = new SemaphoreSlim(0);

            using (var server = new TestServer(async httpContext =>
            {
                try
                {
                    await httpContext.Request.Body.ReadAsync(new byte[1], 0, 1);
                }
                catch (BadHttpRequestException ex)
                {
                    expectedResponse = ex.Message;
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    httpContext.Response.ContentLength = ex.Message.Length;
                    await httpContext.Response.WriteAsync(ex.Message);
                    responseWritten.Release();
                }
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "POST / HTTP/1.1",
                        "Transfer-Encoding: chunked",
                        "",
                        "gg");
                    await responseWritten.WaitAsync();
                    await connection.ReceiveEnd(
                        "HTTP/1.1 400 Bad Request",
                        $"Date: {server.Context.DateHeaderValue}",
                        $"Content-Length: {expectedResponse.Length}",
                        "",
                        expectedResponse);
                }
            }
        }

        [Theory]
        [InlineData("gzip")]
        [InlineData("chunked, gzip")]
        [InlineData("gzip")]
        [InlineData("chunked, gzip")]
        public async Task ConnectionClosedWhenChunkedIsNotFinalTransferCoding(string responseTransferEncoding)
        {
            using (var server = new TestServer(async httpContext =>
            {
                httpContext.Response.Headers["Transfer-Encoding"] = responseTransferEncoding;
                await httpContext.Response.WriteAsync("hello, world");
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveForcedEnd(
                        "HTTP/1.1 200 OK",
                        "Connection: close",
                        $"Date: {server.Context.DateHeaderValue}",
                        $"Transfer-Encoding: {responseTransferEncoding}",
                        "",
                        "hello, world");
                }

                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.0",
                        "Connection: keep-alive",
                        "",
                        "");
                    await connection.ReceiveForcedEnd(
                        "HTTP/1.1 200 OK",
                        "Connection: close",
                        $"Date: {server.Context.DateHeaderValue}",
                        $"Transfer-Encoding: {responseTransferEncoding}",
                        "",
                        "hello, world");
                }
            }
        }

        [Theory]
        [InlineData("gzip")]
        [InlineData("chunked, gzip")]
        [InlineData("gzip")]
        [InlineData("chunked, gzip")]
        public async Task ConnectionClosedWhenChunkedIsNotFinalTransferCodingEvenIfConnectionKeepAliveSetInResponse(string responseTransferEncoding)
        {
            using (var server = new TestServer(async httpContext =>
            {
                httpContext.Response.Headers["Connection"] = "keep-alive";
                httpContext.Response.Headers["Transfer-Encoding"] = responseTransferEncoding;
                await httpContext.Response.WriteAsync("hello, world");
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveForcedEnd(
                        "HTTP/1.1 200 OK",
                        "Connection: keep-alive",
                        $"Date: {server.Context.DateHeaderValue}",
                        $"Transfer-Encoding: {responseTransferEncoding}",
                        "",
                        "hello, world");
                }

                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.0",
                        "Connection: keep-alive",
                        "",
                        "");
                    await connection.ReceiveForcedEnd(
                        "HTTP/1.1 200 OK",
                        "Connection: keep-alive",
                        $"Date: {server.Context.DateHeaderValue}",
                        $"Transfer-Encoding: {responseTransferEncoding}",
                        "",
                        "hello, world");
                }
            }
        }

        [Theory]
        [InlineData("chunked")]
        [InlineData("gzip, chunked")]
        public async Task ConnectionKeptAliveWhenChunkedIsFinalTransferCoding(string responseTransferEncoding)
        {
            using (var server = new TestServer(async httpContext =>
            {
                httpContext.Response.Headers["Transfer-Encoding"] = responseTransferEncoding;

                // App would have to chunk manually, but here we don't care
                await httpContext.Response.WriteAsync("hello, world");
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        $"Transfer-Encoding: {responseTransferEncoding}",
                        "",
                        "hello, world");

                    // Make sure connection was kept open
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        $"Transfer-Encoding: {responseTransferEncoding}",
                        "",
                        "hello, world");
                }
            }
        }

        [Fact]
        public async Task FirstWriteVerifiedAfterOnStarting()
        {
            using (var server = new TestServer(httpContext =>
            {
                httpContext.Response.OnStarting(() =>
                {
                    // Change response to chunked
                    httpContext.Response.ContentLength = null;
                    return TaskCache.CompletedTask;
                });

                var response = Encoding.ASCII.GetBytes("hello, world");
                httpContext.Response.ContentLength = response.Length - 1;

                // If OnStarting is not run before verifying writes, an error response will be sent.
                httpContext.Response.Body.Write(response, 0, response.Length);
                return TaskCache.CompletedTask;
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        $"Transfer-Encoding: chunked",
                        "",
                        "c",
                        "hello, world",
                        "0",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task SubsequentWriteVerifiedAfterOnStarting()
        {
            using (var server = new TestServer(httpContext =>
            {
                httpContext.Response.OnStarting(() =>
                {
                    // Change response to chunked
                    httpContext.Response.ContentLength = null;
                    return TaskCache.CompletedTask;
                });

                var response = Encoding.ASCII.GetBytes("hello, world");
                httpContext.Response.ContentLength = response.Length - 1;

                // If OnStarting is not run before verifying writes, an error response will be sent.
                httpContext.Response.Body.Write(response, 0, response.Length / 2);
                httpContext.Response.Body.Write(response, response.Length / 2, response.Length - response.Length / 2);
                return TaskCache.CompletedTask;
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        $"Transfer-Encoding: chunked",
                        "",
                        "6",
                        "hello,",
                        "6",
                        " world",
                        "0",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task FirstWriteAsyncVerifiedAfterOnStarting()
        {
            using (var server = new TestServer(httpContext =>
            {
                httpContext.Response.OnStarting(() =>
                {
                    // Change response to chunked
                    httpContext.Response.ContentLength = null;
                    return TaskCache.CompletedTask;
                });

                var response = Encoding.ASCII.GetBytes("hello, world");
                httpContext.Response.ContentLength = response.Length - 1;

                // If OnStarting is not run before verifying writes, an error response will be sent.
                return httpContext.Response.Body.WriteAsync(response, 0, response.Length);
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        $"Transfer-Encoding: chunked",
                        "",
                        "c",
                        "hello, world",
                        "0",
                        "",
                        "");
                }
            }
        }

        [Fact]
        public async Task SubsequentWriteAsyncVerifiedAfterOnStarting()
        {
            using (var server = new TestServer(async httpContext =>
            {
                httpContext.Response.OnStarting(() =>
                {
                    // Change response to chunked
                    httpContext.Response.ContentLength = null;
                    return TaskCache.CompletedTask;
                });

                var response = Encoding.ASCII.GetBytes("hello, world");
                httpContext.Response.ContentLength = response.Length - 1;

                // If OnStarting is not run before verifying writes, an error response will be sent.
                await httpContext.Response.Body.WriteAsync(response, 0, response.Length / 2);
                await httpContext.Response.Body.WriteAsync(response, response.Length / 2, response.Length - response.Length / 2);
            }, new TestServiceContext()))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET / HTTP/1.1",
                        "",
                        "");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        $"Date: {server.Context.DateHeaderValue}",
                        $"Transfer-Encoding: chunked",
                        "",
                        "6",
                        "hello,",
                        "6",
                        " world",
                        "0",
                        "",
                        "");
                }
            }
        }

        public static TheoryData<string, StringValues, string> NullHeaderData
        {
            get
            {
                var dataset = new TheoryData<string, StringValues, string>();

                // Unknown headers
                dataset.Add("NullString", (string)null, null);
                dataset.Add("EmptyString", "", "");
                dataset.Add("NullStringArray", new string[] { null }, null);
                dataset.Add("EmptyStringArray", new string[] { "" }, "");
                dataset.Add("MixedStringArray", new string[] { null, "" }, "");
                // Known headers
                dataset.Add("Location", (string)null, null);
                dataset.Add("Location", "", "");
                dataset.Add("Location", new string[] { null }, null);
                dataset.Add("Location", new string[] { "" }, "");
                dataset.Add("Location", new string[] { null, "" }, "");

                return dataset;
            }
        }
    }
}
