using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class MaxInputBufferLengthTests
    {
        [Theory]
        [InlineData(-1)]
        [InlineData(10 * 1024 * 1024)]
        [InlineData(Int32.MaxValue)]
        public void LargeUploadNotPaused(int maxInputBufferLength)
        {
            const int totalBytes = 10 * 1024 * 1024;
            var startReadingRequestBody = new ManualResetEvent(false);

            using (var host = StartWebHost(totalBytes, maxInputBufferLength, startReadingRequestBody))
            {
                var bytesWritten = 0;
                using (var socket = CreateSocketForHttpPost(host))
                {
                    var buffer = new byte[4096];

                    while (bytesWritten < totalBytes)
                    {
                        var size = Math.Min(totalBytes - bytesWritten, buffer.Length);
                        bytesWritten += socket.Send(buffer, 0, size, SocketFlags.None);
                    }

                    Assert.Equal(totalBytes, bytesWritten);

                    // Tell server to start reading request body
                    startReadingRequestBody.Set();

                    buffer = new byte[4096];
                    socket.Receive(buffer);
                    Assert.Contains($"bytesRead: {totalBytes}", Encoding.ASCII.GetString(buffer));
                }
            }
        }

        [Theory]
        [InlineData(16 * 1024)]
        [InlineData(1024 * 1024)]
        [InlineData(5 * 1024 * 1024)]
        public void LargeUploadPausedWhenInputBufferFull(int maxInputBufferLength)
        {
            const int totalBytes = 10 * 1024 * 1024;
            var startReadingRequestBody = new ManualResetEvent(false);

            using (var host = StartWebHost(totalBytes, maxInputBufferLength, startReadingRequestBody))
            {
                var bytesWritten = 0;
                using (var socket = CreateSocketForHttpPost(host))
                {
                    var buffer = new byte[4096];
                    try
                    {
                        while (bytesWritten < totalBytes)
                        {
                            var size = Math.Min(totalBytes - bytesWritten, buffer.Length);
                            bytesWritten += socket.Send(buffer, 0, size, SocketFlags.None);
                        }

                        Assert.Equal("SocketException", "No Exception");
                    }
                    catch (SocketException)
                    {
                        // When the input buffer is full (plus some amount of OS buffers), socket.Send() should
                        // throw a SocketException, since the server called IConnectionControl.Pause().

                        // Verify the number of bytes written is greater than or equal to the max input buffer size,
                        // but less than the total bytes.
                        Assert.InRange(bytesWritten, maxInputBufferLength, totalBytes - 1);

                        // Tell server to start reading request body
                        startReadingRequestBody.Set();

                        while (bytesWritten < totalBytes)
                        {
                            var size = Math.Min(totalBytes - bytesWritten, buffer.Length);
                            bytesWritten += socket.Send(buffer, 0, size, SocketFlags.None);
                        }
                    }

                    Assert.Equal(totalBytes, bytesWritten);

                    buffer = new byte[4096];
                    socket.Receive(buffer);
                    Assert.Contains($"bytesRead: {totalBytes}", Encoding.ASCII.GetString(buffer));
                }
            }
        }

        private static IWebHost StartWebHost(int totalBytes, int maxInputBufferLength, ManualResetEvent startReadingRequestBody)
        {
            var host = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    options.MaxInputBufferLength = maxInputBufferLength;
                })
                .UseUrls("http://127.0.0.1:0/")
                .Configure(app => app.Run(async context =>
                {
                    var bytesRead = 0;

                    startReadingRequestBody.WaitOne();

                    var buffer = new byte[4096];
                    while (bytesRead < totalBytes)
                    {
                        bytesRead += await context.Request.Body.ReadAsync(buffer, 0, buffer.Length);
                    }

                    await context.Response.WriteAsync($"bytesRead: {bytesRead.ToString()}");
                }
                ))
                .Build();

            host.Start();

            return host;
        }

        private static Socket CreateSocketForHttpPost(IWebHost host)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            socket.SendTimeout = 100;
            socket.ReceiveTimeout = 100;

            socket.Connect(IPAddress.Loopback, host.GetPort());
            socket.Send(Encoding.ASCII.GetBytes($"POST / HTTP/1.0\r\n\r\n"));

            return socket;
        }
    }
}
