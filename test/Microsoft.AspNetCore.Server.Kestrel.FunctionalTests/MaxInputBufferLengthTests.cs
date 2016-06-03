using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class MaxInputBufferLengthTests
    {
        private static readonly int _packetSize = 4096;
        private static readonly byte[] _data = new byte[10 * 1024 * 1024];

        static MaxInputBufferLengthTests()
        {
            // Fixed seed for reproducability
            (new Random(0)).NextBytes(_data);
        }

        [Theory]
        [InlineData(-1, true)]
        [InlineData(-1, false)]
        [InlineData(10 * 1024 * 1024, true)]
        [InlineData(10 * 1024 * 1024, false)]
        [InlineData(Int32.MaxValue, true)]
        [InlineData(Int32.MaxValue, false)]
        public void LargeUploadNotPaused(int maxInputBufferLength, bool sendContentLengthHeader)
        {
            var startReadingRequestBody = new ManualResetEvent(false);
            var clientFinishedSendingRequestBody = new ManualResetEvent(false);

            using (var host = StartWebHost(maxInputBufferLength, startReadingRequestBody, clientFinishedSendingRequestBody))
            {
                using (var socket = CreateSocketForHttpPost(host, sendContentLengthHeader ? _data.Length : -1))
                {
                    var bytesWritten = 0;
                    while (bytesWritten < _data.Length)
                    {
                        var size = Math.Min(_data.Length - bytesWritten, _packetSize);
                        bytesWritten += socket.Send(_data, bytesWritten, size, SocketFlags.None);
                    }
                    socket.Shutdown(SocketShutdown.Send);
                    clientFinishedSendingRequestBody.Set();

                    Assert.Equal(_data.Length, bytesWritten);

                    // Tell server to start reading request body
                    startReadingRequestBody.Set();

                    var buffer = new byte[_packetSize];
                    var bytesRead = socket.Receive(buffer);
                    Assert.Contains($"bytesRead: {_data.Length}", Encoding.ASCII.GetString(buffer, 0, bytesRead));
                }
            }
        }

        [Theory]
        [InlineData(16 * 1024, true)]
        [InlineData(16 * 1024, false)]
        [InlineData(1024 * 1024, true)]
        [InlineData(1024 * 1024, false)]
        [InlineData(5 * 1024 * 1024, true)]
        [InlineData(5 * 1024 * 1024, false)]
        public void LargeUploadPausedWhenInputBufferFull(int maxInputBufferLength, bool sendContentLengthHeader)
        {
            var startReadingRequestBody = new ManualResetEvent(false);
            var clientFinishedSendingRequestBody = new ManualResetEvent(false);
        
            using (var host = StartWebHost(maxInputBufferLength, startReadingRequestBody, clientFinishedSendingRequestBody))
            {
                using (var socket = CreateSocketForHttpPost(host, sendContentLengthHeader ? _data.Length : -1))
                {
                    var bytesWritten = 0;
                    try
                    {
                        socket.SendTimeout = 100;
                        while (bytesWritten < _data.Length)
                        {
                            var size = Math.Min(_data.Length - bytesWritten, _packetSize);
                            bytesWritten += socket.Send(_data, bytesWritten, size, SocketFlags.None);
                        }

                        Assert.Equal("SocketException", "No Exception");
                    }
                    catch (SocketException)
                    {
                        // When the input buffer is full (plus some amount of OS buffers), socket.Send() should
                        // throw a SocketException, since the server called IConnectionControl.Pause().

                        bytesWritten += _packetSize;

                        // Verify the number of bytes written is greater than or equal to the max input buffer size,
                        // but less than the total bytes.
                        Assert.InRange(bytesWritten, maxInputBufferLength, _data.Length - 1);

                        // Tell server to start reading request body
                        startReadingRequestBody.Set();

                        socket.SendTimeout = 10 * 1000;
                        while (bytesWritten < _data.Length)
                        {
                            var size = Math.Min(_data.Length - bytesWritten, _packetSize);
                            bytesWritten += socket.Send(_data, bytesWritten, size, SocketFlags.None);
                        }
                        socket.Shutdown(SocketShutdown.Send);
                        clientFinishedSendingRequestBody.Set();
                    }

                    Assert.Equal(_data.Length, bytesWritten);

                    var buffer = new byte[_packetSize];
                    var bytesRead = socket.Receive(buffer);
                    Assert.Contains($"bytesRead: {_data.Length}", Encoding.ASCII.GetString(buffer, 0, bytesRead));
                }
            }
        }

        private static IWebHost StartWebHost(int maxInputBufferLength, ManualResetEvent startReadingRequestBody,
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

                    var buffer = new byte[_data.Length];
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
                        await context.Response.WriteAsync("Client sent more bytes than _data.Length");
                        return;
                    }

                    // Verify bytes received match _data
                    for (int i=0; i < _data.Length; i++)
                    {
                        if (buffer[i] != _data[i])
                        {
                            context.Response.StatusCode = 500;
                            await context.Response.WriteAsync($"Bytes received do not match _data at position {i}");
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
            socket.Send(Encoding.ASCII.GetBytes($"POST / HTTP/1.0\r\n"));
            if (contentLength > -1)
            {
                socket.Send(Encoding.ASCII.GetBytes($"Content-Length: {contentLength}\r\n"));
            }
            socket.Send(Encoding.ASCII.GetBytes("\r\n"));
            
            return socket;
        }
    }
}
