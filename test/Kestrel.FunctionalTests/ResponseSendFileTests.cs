// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Testing;
using Microsoft.AspNetCore.Testing.xunit;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    [OSSkipCondition(OperatingSystems.Linux | OperatingSystems.MacOSX, SkipReason = "True async file IO for IHttpSendFileFeature is only supported on Windows.")]
    public class ResponseSendFileTests : TestApplicationErrorLoggerLoggedTest
    {
        private readonly string AbsoluteFilePath;
        private readonly string RelativeFilePath;
        private readonly long FileLength;

        public ResponseSendFileTests()
        {
            AbsoluteFilePath = Directory.GetFiles(Directory.GetCurrentDirectory()).First();
            RelativeFilePath = Path.GetFileName(AbsoluteFilePath);
            FileLength = new FileInfo(AbsoluteFilePath).Length;
        }

        [ConditionalFact]
        public async Task ResponseSendFile_SupportKeys_Present()
        {
            using (CreateHttpServer(out string address, httpContext =>
             {
                 try
                 {
                     var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                     Assert.NotNull(sendFile);
                 }
                 catch (Exception ex)
                 {
                     byte[] body = Encoding.UTF8.GetBytes(ex.ToString());
                     httpContext.Response.Body.Write(body, 0, body.Length);
                 }
                 return Task.FromResult(0);
             }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(200, (int)response.StatusCode);
                Assert.True(response.Content.Headers.TryGetValues("content-length", out IEnumerable<string> ignored), "Content-Length");
                Assert.False(response.Headers.TransferEncodingChunked.HasValue, "Chunked");
                Assert.Equal(0, response.Content.Headers.ContentLength);
                Assert.Equal(0, await response.Content.GetLengthAsync());
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_MissingFile_Throws()
        {
            var waitHandle = new ManualResetEvent(false);
            bool? appThrew = null;
            using (CreateHttpServer(out string address, async httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 try
                 {
                     await sendFile.SendFileAsync(string.Empty, 0, null, CancellationToken.None);
                     appThrew = false;
                 }
                 catch (Exception)
                 {
                     appThrew = true;
                     throw;
                 }
                 finally
                 {
                     waitHandle.Set();
                 }
             }))
            {
                HttpResponseMessage response = await SendRequestAsync(address);
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.True(waitHandle.WaitOne(100));
                Assert.True(appThrew.HasValue, "appThrew.HasValue");
                Assert.True(appThrew.Value, "appThrew.Value");
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_NoHeaders_DefaultsToChunked()
        {
            using (CreateHttpServer(out string address, httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 return sendFile.SendFileAsync(AbsoluteFilePath, 0, null, CancellationToken.None);
             }))
            {
                HttpResponseMessage response = await SendRequestAsync(address);
                Assert.Equal(200, (int)response.StatusCode);
                Assert.False(response.Content.Headers.TryGetValues("content-length", out IEnumerable<string> ignored), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value, "Chunked");
                Assert.Equal(FileLength, await response.Content.GetLengthAsync());
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_RelativeFile_Success()
        {
            using (CreateHttpServer(out string address, httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 return sendFile.SendFileAsync(RelativeFilePath, 0, null, CancellationToken.None);
             }))
            {
                HttpResponseMessage response = await SendRequestAsync(address);
                Assert.Equal(200, (int)response.StatusCode);
                Assert.False(response.Content.Headers.TryGetValues("content-length", out IEnumerable<string> ignored), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value, "Chunked");
                Assert.Equal(FileLength, await response.Content.GetLengthAsync());
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_Unspecified_Chunked()
        {
            using (CreateHttpServer(out string address, httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 return sendFile.SendFileAsync(AbsoluteFilePath, 0, null, CancellationToken.None);
             }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(200, (int)response.StatusCode);
                Assert.False(response.Content.Headers.TryGetValues("content-length", out IEnumerable<string> contentLength), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value);
                Assert.Equal(FileLength, await response.Content.GetLengthAsync());
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_MultipleWrites_Chunked()
        {
            const int FileCount = 10;
            using (CreateHttpServer(out string address, async httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 for (var i = 0; i < FileCount; i++)
                 {
                     await sendFile.SendFileAsync(AbsoluteFilePath, 0, FileLength, CancellationToken.None);
                 }
             }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(200, (int)response.StatusCode);
                Assert.False(response.Content.Headers.TryGetValues("content-length", out IEnumerable<string> contentLength), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value);
                Assert.Equal(FileLength * FileCount, await response.Content.GetLengthAsync());
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_HalfOfFile_Chunked()
        {
            using (CreateHttpServer(out string address, httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 return sendFile.SendFileAsync(AbsoluteFilePath, 0, FileLength / 2, CancellationToken.None);
             }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(200, (int)response.StatusCode);
                Assert.False(response.Content.Headers.TryGetValues("content-length", out IEnumerable<string> contentLength), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value);
                Assert.Equal(FileLength / 2, await response.Content.GetLengthAsync());
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_OffsetOutOfRange_Throws()
        {
            var completed = false;
            using (CreateHttpServer(out string address, async httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                     sendFile.SendFileAsync(AbsoluteFilePath, FileLength + 1, null, CancellationToken.None));
                 completed = true;
             }))
            {
                var response = await SendRequestAsync(address);
                response.EnsureSuccessStatusCode();
                Assert.True(completed);
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_CountOutOfRange_Throws()
        {
            var completed = false;
            using (CreateHttpServer(out string address, async httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                     sendFile.SendFileAsync(AbsoluteFilePath, 0, FileLength + 1, CancellationToken.None));
                 completed = true;
             }))
            {
                var response = await SendRequestAsync(address);
                response.EnsureSuccessStatusCode();
                Assert.True(completed);
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_Count0_Chunked()
        {
            using (CreateHttpServer(out string address, httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 return sendFile.SendFileAsync(AbsoluteFilePath, 0, 0, CancellationToken.None);
             }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(200, (int)response.StatusCode);
                Assert.False(response.Content.Headers.TryGetValues("content-length", out IEnumerable<string> contentLength), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.Value);
                Assert.Equal(0, await response.Content.GetLengthAsync());
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_ContentLength_PassedThrough()
        {
            using (CreateHttpServer(out string address, httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 httpContext.Response.Headers["Content-lenGth"] = FileLength.ToString();
                 return sendFile.SendFileAsync(AbsoluteFilePath, 0, null, CancellationToken.None);
             }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(200, (int)response.StatusCode);
                Assert.True(response.Content.Headers.TryGetValues("content-length", out IEnumerable<string> contentLength), "Content-Length");
                Assert.Equal(FileLength.ToString(), contentLength.First());
                Assert.Null(response.Headers.TransferEncodingChunked);
                Assert.Equal(FileLength, await response.Content.GetLengthAsync());
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_ContentLengthSpecific_PassedThrough()
        {
            using (CreateHttpServer(out string address, httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 httpContext.Response.Headers["Content-lenGth"] = "10";
                 return sendFile.SendFileAsync(AbsoluteFilePath, 0, 10, CancellationToken.None);
             }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(200, (int)response.StatusCode);
                Assert.True(response.Content.Headers.TryGetValues("content-length", out IEnumerable<string> contentLength), "Content-Length");
                Assert.Equal("10", contentLength.First());
                Assert.Null(response.Headers.TransferEncodingChunked);
                Assert.Equal(10, await response.Content.GetLengthAsync());
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_ContentLength0_PassedThrough()
        {
            using (CreateHttpServer(out string address, httpContext =>
             {
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 httpContext.Response.Headers["Content-lenGth"] = "0";
                 return sendFile.SendFileAsync(AbsoluteFilePath, 0, 0, CancellationToken.None);
             }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(200, (int)response.StatusCode);
                Assert.True(response.Content.Headers.TryGetValues("content-length", out IEnumerable<string> contentLength), "Content-Length");
                Assert.Equal("0", contentLength.First());
                Assert.Null(response.Headers.TransferEncodingChunked);
                Assert.Equal(0, await response.Content.GetLengthAsync());
            }
        }

        [ConditionalFact]
        public async Task ResponseSendFile_TriggersOnStarting()
        {
            var onStartingCalled = false;
            using (CreateHttpServer(out string address, httpContext =>
             {
                 httpContext.Response.OnStarting(state =>
                 {
                     onStartingCalled = true;
                     Assert.Same(state, httpContext);
                     return Task.FromResult(0);
                 }, httpContext);
                 var sendFile = httpContext.Features.Get<IHttpSendFileFeature>();
                 return sendFile.SendFileAsync(AbsoluteFilePath, 0, 10, CancellationToken.None);
             }))
            {
                var response = await SendRequestAsync(address);
                Assert.Equal(200, (int)response.StatusCode);
                Assert.Equal(new Version(1, 1), response.Version);
                Assert.True(onStartingCalled);
                Assert.False(response.Content.Headers.TryGetValues("content-length", out IEnumerable<string> ignored), "Content-Length");
                Assert.True(response.Headers.TransferEncodingChunked.HasValue, "Chunked");
                Assert.Equal(10, await response.Content.GetLengthAsync());
            }
        }

        private IDisposable CreateHttpServer(out string address, Func<HttpContext, Task> application)
        {
            var hostBuilder = TransportSelector.GetWebHostBuilder()
                .UseKestrel()
                .UseUrls("http://127.0.0.1:0/")
                .ConfigureServices(AddTestLogging)
                .Configure(app =>
                {
                    app.Run((context) => application(context));
                });

            var host = hostBuilder.Build();
            host.Start();

            address = $"http://127.0.0.1:{host.GetPort()}/";

            return host;
        }

        private async Task<HttpResponseMessage> SendRequestAsync(string uri)
        {
            using (HttpClient client = new HttpClient())
            {
                return await client.GetAsync(uri);
            }
        }
    }

    static class HttpContentExtensions
    {
        public static async Task<int> GetLengthAsync(this HttpContent content)
        {
            var stream = await content.ReadAsStreamAsync();
            var buffer = new byte[1024];
            var count = await stream.ReadAsync(buffer, 0, buffer.Length);
            var total = count;
            while ((count = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                total += count;
            }

            return total;
        }
    }
}
