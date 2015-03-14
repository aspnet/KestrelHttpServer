﻿using Microsoft.AspNet.Server.Kestrel;
using Microsoft.AspNet.Server.Kestrel.Http;
using Microsoft.Framework.Runtime;
using Microsoft.Framework.Runtime.Infrastructure;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.AspNet.Server.KestrelTests
{
    /// <summary>
    /// Summary description for EngineTests
    /// </summary>
    public class EngineTests
    {
        private const int port = 54321;

        private async Task App(Frame frame)
        {
            for (; ;)
            {
                var buffer = new byte[8192];
                var count = await frame.RequestBody.ReadAsync(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    break;
                }
                await frame.ResponseBody.WriteAsync(buffer, 0, count);
            }
        }

        ILibraryManager LibraryManager
        {
            get
            {
                try
                {
                    var locator = CallContextServiceLocator.Locator;
                    if (locator == null)
                    {
                        return null;
                    }
                    var services = locator.ServiceProvider;
                    if (services == null)
                    {
                        return null;
                    }
                    return (ILibraryManager)services.GetService(typeof(ILibraryManager));
                }
                catch (NullReferenceException)
                { return null; }
            }
        }

        private async Task AppChunked(Frame frame)
        {
            var data = new MemoryStream();
            for (; ;)
            {
                var buffer = new byte[8192];
                var count = await frame.RequestBody.ReadAsync(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    break;
                }
                data.Write(buffer, 0, count);
            }
            var bytes = data.ToArray();
            frame.ResponseHeaders["Content-Length"] = new[] { bytes.Length.ToString() };
            await frame.ResponseBody.WriteAsync(bytes, 0, bytes.Length);
        }

        [Fact]
        public async Task EngineCanStartAndStop()
        {
            using (var engine = new KestrelEngine(LibraryManager))
            {
                engine.Start(1);
                Assert.True(engine.IsClean());
            }
        }

        [Fact]
        public async Task ListenerCanCreateAndDispose()
        {
            using (var engine = new KestrelEngine(LibraryManager))
            {
                engine.Start(1);
                using (engine.CreateServer("http", "localhost", port, App))
                {
                    Assert.True(engine.IsClean());
                }
            }
        }

        [Fact]
        public async Task ConnectionCanReadAndWrite()
        {
            using (var engine = new KestrelEngine(LibraryManager))
            {
                engine.Start(1);
                using (engine.CreateServer("http", "localhost", port, App))
                {
                    using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        Console.WriteLine("Started");

                        socket.Connect(new IPEndPoint(IPAddress.Loopback, port));
                        socket.Send(Encoding.ASCII.GetBytes("POST / HTTP/1.0\r\n\r\nHello World"));
                        socket.Shutdown(SocketShutdown.Send);
                        var buffer = new byte[8192];
                        var response = string.Empty;
                        var length = 0;
                        do
                        {
                            length = socket.Receive(buffer);
                            response += Encoding.ASCII.GetString(buffer, 0, length);
                        }
                        while (length > 0);
                        Assert.False(string.IsNullOrEmpty(response));
                    }
                    Assert.True(engine.IsClean());
                }
            }
        }

        [Fact]
        public async Task Http10()
        {
            using (var server = new TestServer(App, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.SendEnd(
                        "POST / HTTP/1.0",
                        "",
                        "Hello World");
                    await connection.ReceiveEnd(
                        "HTTP/1.0 200 OK",
                        "",
                        "Hello World");
                    Assert.True(server.IsClean());
                }
            }
        }

        [Fact]
        public async Task Http11()
        {
            using (var server = new TestServer(AppChunked, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.SendEnd(
                        "GET / HTTP/1.1",
                        "",
                        "GET / HTTP/1.1",
                        "Connection: close",
                        "",
                        "Goodbye");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        "Content-Length: 0",
                        "",
                        "HTTP/1.1 200 OK",
                        "Content-Length: 7",
                        "Connection: close",
                        "",
                        "Goodbye");
                    Assert.True(server.IsClean());
                }
            }
        }


        [Fact]
        public async Task Http10ContentLength()
        {
            using (var server = new TestServer(App, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.Send(
                        "POST / HTTP/1.0",
                        "Content-Length: 11",
                        "",
                        "Hello World");
                    await connection.ReceiveEnd(
                        "HTTP/1.0 200 OK",
                        "",
                        "Hello World");
                    Assert.True(server.IsClean());
                }
            }
        }

        [Fact]
        public async Task Http10TransferEncoding()
        {
            using (var server = new TestServer(App, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.Send(
                        "POST / HTTP/1.0",
                        "Transfer-Encoding: chunked",
                        "",
                        "5", "Hello", "6", " World", "0\r\n");
                    await connection.ReceiveEnd(
                        "HTTP/1.0 200 OK",
                        "",
                        "Hello World");
                    Assert.True(server.IsClean());
                }
            }
        }


        [Fact]
        public async Task Http10KeepAlive()
        {
            using (var server = new TestServer(AppChunked, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.SendEnd(
                        "GET / HTTP/1.0",
                        "Connection: keep-alive",
                        "",
                        "POST / HTTP/1.0",
                        "",
                        "Goodbye");
                    await connection.Receive(
                        "HTTP/1.0 200 OK",
                        "Content-Length: 0",
                        "Connection: keep-alive",
                        "\r\n");
                    await connection.ReceiveEnd(
                        "HTTP/1.0 200 OK",
                        "Content-Length: 7",
                        "",
                        "Goodbye");
                    Assert.True(server.IsClean());
                }
            }
        }

        [Fact]
        public async Task Http10KeepAliveContentLength()
        {
            using (var server = new TestServer(AppChunked, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.SendEnd(
                        "POST / HTTP/1.0",
                        "Connection: keep-alive",
                        "Content-Length: 11",
                        "",
                        "Hello WorldPOST / HTTP/1.0",
                        "",
                        "Goodbye");
                    await connection.Receive(
                        "HTTP/1.0 200 OK",
                        "Content-Length: 11",
                        "Connection: keep-alive",
                        "",
                        "Hello World");
                    await connection.ReceiveEnd(
                        "HTTP/1.0 200 OK",
                        "Content-Length: 7",
                        "",
                        "Goodbye");
                    Assert.True(server.IsClean());
                }
            }
        }

        [Fact]
        public async Task Http10KeepAliveTransferEncoding()
        {
            using (var server = new TestServer(AppChunked, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.SendEnd(
                        "POST / HTTP/1.0",
                        "Transfer-Encoding: chunked",
                        "Connection: keep-alive",
                        "",
                        "5", "Hello", "6", " World", "0",
                        "POST / HTTP/1.0",
                        "",
                        "Goodbye");
                    await connection.Receive(
                        "HTTP/1.0 200 OK",
                        "Content-Length: 11",
                        "Connection: keep-alive",
                        "",
                        "Hello World");
                    await connection.ReceiveEnd(
                        "HTTP/1.0 200 OK",
                        "Content-Length: 7",
                        "",
                        "Goodbye");
                    Assert.True(server.IsClean());
                }
            }
        }

        [Fact]
        public async Task Expect100ContinueForBody()
        {
            using (var server = new TestServer(AppChunked, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.Send(
                        "POST / HTTP/1.1",
                        "Expect: 100-continue",
                        "Content-Length: 11",
                        "Connection: close",
                        "\r\n");
                    await connection.Receive("HTTP/1.1 100 Continue", "\r\n");
                    await connection.SendEnd("Hello World");
                    await connection.Receive(
                        "HTTP/1.1 200 OK",
                        "Content-Length: 11",
                        "Connection: close",
                        "",
                        "Hello World");
                    Assert.True(server.IsClean());
                }
            }
        }
    }
}
