using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
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
        private const int _dataLength = 10 * 1024 * 1024;

        public static IEnumerable<object[]> LargeUploadData
        {
            get
            {
                var maxInputBufferLengthValues = new int?[] {
                    16 * 1024,
                    1024 * 1024,
                    5 * 1024 * 1024,
                    10 * 1024 * 1024,
                    Int32.MaxValue,
                    null
                };
                var sendContentLengthHeaderValues = new[] { true, false };
                var sslValues = new[] { true, false };

                return from maxInputBufferLength in maxInputBufferLengthValues
                       from sendContentLengthHeader in sendContentLengthHeaderValues
                       from ssl in sslValues
                       select new object[] {
                           maxInputBufferLength,
                           sendContentLengthHeader,
                           ssl,
                           maxInputBufferLength.HasValue && maxInputBufferLength.Value < _dataLength
                       };
            }
        }

        [Theory]
        [MemberData("LargeUploadData")]
        public async Task LargeUpload(int? maxInputBufferLength, bool sendContentLengthHeader, bool ssl, bool expectPause)
        {
            // Parameters
            var data = new byte[_dataLength];
            var bytesWrittenTimeout = TimeSpan.FromMilliseconds(100);
            var maxSendSize = 4096;

            // Initialize data with random bytes
            (new Random()).NextBytes(data);

            var startReadingRequestBody = new ManualResetEvent(false);
            var clientFinishedSendingRequestBody = new ManualResetEvent(false);
            var bytesWrittenEvent = new AutoResetEvent(false);

            using (var host = StartWebHost(maxInputBufferLength, data, startReadingRequestBody, clientFinishedSendingRequestBody))
            {
                var port = host.GetPort(ssl ? "https" : "http");
                using (var socket = CreateSocket(port))
                using (var stream = await CreateStreamAsync(socket, ssl, host.GetHost()))
                {
                    WritePostRequestHeaders(stream, sendContentLengthHeader ? (int?)data.Length : null);

                    var bytesWritten = 0;

                    var sendTask = Task.Run(() =>
                    {
                        while (bytesWritten < data.Length)
                        {
                            var size = Math.Min(data.Length - bytesWritten, maxSendSize);
                            stream.Write(data, bytesWritten, size);
                            bytesWritten += size;
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

                        // Verify the number of bytes written before the client was paused.
                        // 
                        // The minimum is (maxInputBufferLength - maxSendSize + 1), since if bytesWritten is
                        // (maxInputBufferLength - maxSendSize) or smaller, the client should be able to
                        // complete another send.
                        // 
                        // The maximum is harder to determine, since there can be OS-level buffers in both the client
                        // and server, which allow the client to send more than maxInputBufferLength before getting
                        // paused.  We assume the combined buffers are smaller than the difference between
                        // data.Length and maxInputBufferLength.                          
                        Assert.InRange(bytesWritten, maxInputBufferLength.Value - maxSendSize + 1, data.Length - 1);

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

                    using (var reader = new StreamReader(stream, Encoding.ASCII))
                    {
                        var response = reader.ReadToEnd();
                        Assert.Contains($"bytesRead: {data.Length}", response);
                    }
                }
            }
        }

        private static IWebHost StartWebHost(int? maxInputBufferLength, byte[] expectedBody, ManualResetEvent startReadingRequestBody,
            ManualResetEvent clientFinishedSendingRequestBody)
        {
            var host = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    options.MaxInputBufferLength = maxInputBufferLength;
                    options.UseHttps(@"TestResources/testCert.pfx", "testPassword");
                })
                .UseUrls("http://127.0.0.1:0/", "https://127.0.0.1:0/")
                .UseContentRoot(Directory.GetCurrentDirectory())
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
                }))
                .Build();

            host.Start();

            return host;
        }

        private static Socket CreateSocket(int port)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Timeouts large enough to prevent false positives, but small enough to fail quickly.
            socket.SendTimeout = 10 * 1000;
            socket.ReceiveTimeout = 10 * 1000;

            socket.Connect(IPAddress.Loopback, port);

            return socket;
        }

        private static void WritePostRequestHeaders(Stream stream, int? contentLength)
        {
            using (var writer = new StreamWriter(stream, Encoding.ASCII, bufferSize: 1024, leaveOpen: true))
            {
                writer.WriteLine("POST / HTTP/1.0");
                if (contentLength.HasValue)
                {
                    writer.WriteLine($"Content-Length: {contentLength.Value}");
                }
                writer.WriteLine();
            }
        }

        private static async Task<Stream> CreateStreamAsync(Socket socket, bool ssl, string targetHost)
        {
            var networkStream = new NetworkStream(socket);
            if (ssl)
            {
                var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false,
                    userCertificateValidationCallback: (a, b, c, d) => true);
                await sslStream.AuthenticateAsClientAsync(targetHost, clientCertificates: null,
                    enabledSslProtocols: SslProtocols.Tls11 | SslProtocols.Tls12, checkCertificateRevocation: false);
                return sslStream;
            }
            else
            {
                return networkStream;
            }
        }
    }
}
