using System;
using System.Net;
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
        [InlineData(16 * 1024, true, true)]
        [InlineData(16 * 1024, false, true)]
        [InlineData(1024 * 1024, true, true)]
        [InlineData(1024 * 1024, false, true)]
        [InlineData(5 * 1024 * 1024, true, true)]
        [InlineData(5 * 1024 * 1024, false, true)]
        [InlineData(10 * 1024 * 1024, true, false)]
        [InlineData(10 * 1024 * 1024, false, false)]
        [InlineData(Int32.MaxValue, true, false)]
        [InlineData(Int32.MaxValue, false, false)]
        [InlineData(-1, true, false)]
        [InlineData(-1, false, false)]
        public async Task LargeUpload(int maxInputBufferLength, bool sendContentLengthHeader, bool expectPause)
        {
            // Parameters
            var data = new byte[10 * 1024 * 1024];
            var bytesWrittenTimeout = TimeSpan.FromMilliseconds(100);
            var maxSendSize = 4096;

            // Initialize data with random bytes
            (new Random()).NextBytes(data);

            var startReadingRequestBody = new ManualResetEvent(false);
            var clientFinishedSendingRequestBody = new ManualResetEvent(false);
            var bytesWrittenEvent = new AutoResetEvent(false);

            using (var host = StartWebHost(maxInputBufferLength, data, startReadingRequestBody, clientFinishedSendingRequestBody))
            {
                using (var socket = CreateSocketForHttpPost(host, sendContentLengthHeader ? data.Length : -1))
                {
                    var bytesWritten = 0;

                    var sendTask = Task.Run(() =>
                    {
                        while (bytesWritten < data.Length)
                        {
                            var size = Math.Min(data.Length - bytesWritten, maxSendSize);
                            bytesWritten += socket.Send(data, bytesWritten, size, SocketFlags.None);
                            bytesWrittenEvent.Set();
                        }

                        Assert.Equal(data.Length, bytesWritten);
                        socket.Shutdown(SocketShutdown.Send);
                        clientFinishedSendingRequestBody.Set();
                    });

                    if (expectPause)
                    {
                        // Block until the send task has gone a while without writing bytes, which likely means
                        // the server input buffer is full.
                        while (bytesWrittenEvent.WaitOne(bytesWrittenTimeout)) { }

                        // Verify the number of bytes written is greater than or equal to the max input buffer size,
                        // but less than the total bytes.
                        Assert.InRange(bytesWritten, maxInputBufferLength, data.Length - 1);

                        // Tell server to start reading request body
                        startReadingRequestBody.Set();

                        // Wait for sendTask to finish sending the remaining bytes
                        await sendTask;
                    }
                    else
                    {
                        // Ensure all bytes can be sent before the server starts reading
                        await sendTask;

                        // Tell server to start reading request body
                        startReadingRequestBody.Set();
                    }

                    var buffer = new byte[maxSendSize];
                    var bytesRead = socket.Receive(buffer);
                    Assert.Contains($"bytesRead: {data.Length}", Encoding.ASCII.GetString(buffer, 0, bytesRead));
                }
            }
        }

        private static IWebHost StartWebHost(int maxInputBufferLength, byte[] expectedBody, ManualResetEvent startReadingRequestBody,
            ManualResetEvent clientFinishedSendingRequestBody)
        {
            var host = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    options.MaxInputBufferLength = maxInputBufferLength;
                })
                .UseUrls("http://127.0.0.1:0/")
                .Configure(app => app.Run(async context =>
                {
                    startReadingRequestBody.WaitOne();

                    var buffer = new byte[expectedBody.Length];
                    var bytesRead = 0;
                    while (bytesRead < buffer.Length)
                    {
                        bytesRead += await context.Request.Body.ReadAsync(buffer, bytesRead, buffer.Length - bytesRead);
                    }

                    clientFinishedSendingRequestBody.WaitOne();

                    // Verify client didn't send extra bytes
                    if (context.Request.Body.ReadByte() != -1)
                    {
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsync("Client sent more bytes than expectedBody.Length");
                        return;
                    }

                    // Verify bytes received match expectedBody
                    for (int i = 0; i < expectedBody.Length; i++)
                    {
                        if (buffer[i] != expectedBody[i])
                        {
                            context.Response.StatusCode = 500;
                            await context.Response.WriteAsync($"Bytes received do not match expectedBody at position {i}");
                            return;
                        }
                    }

                    await context.Response.WriteAsync($"bytesRead: {bytesRead.ToString()}");
                }
                ))
                .Build();

            host.Start();

            return host;
        }

        private static Socket CreateSocketForHttpPost(IWebHost host, int contentLength)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Timeouts large enough to prevent false positives, but small enough to fail quickly.
            socket.SendTimeout = 10 * 1000;
            socket.ReceiveTimeout = 10 * 1000;

            socket.Connect(IPAddress.Loopback, host.GetPort());
            socket.Send(Encoding.ASCII.GetBytes("POST / HTTP/1.0\r\n"));
            if (contentLength > -1)
            {
                socket.Send(Encoding.ASCII.GetBytes($"Content-Length: {contentLength}\r\n"));
            }
            socket.Send(Encoding.ASCII.GetBytes("\r\n"));

            return socket;
        }
    }
}
