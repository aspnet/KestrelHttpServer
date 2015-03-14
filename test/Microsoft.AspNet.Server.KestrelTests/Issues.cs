using Microsoft.AspNet.Server.Kestrel.Http;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.AspNet.Server.KestrelTests
{
    public class Issues
    {
        private const int port = 54323;

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

        [Fact]
        public async Task BogusHttpVersionWorks()
        {
            using (var server = new TestServer(App, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.SendEnd(
                        "POST / bogus",
                        // The connection is handled as HTTP/1.1 so keep-alive is active
                        "Connection: close",
                        "",
                        "Hello World");
                    await connection.ReceiveEnd(
                        "bogus 200 OK",
                        "",
                        "Hello World");
                    Assert.True(false, "This should not be reached");
                }
            }
        }

        [Fact]
        public async Task EndingBeforeStartingTheRequestDoesNotEndConnection()
        {
            using (var server = new TestServer(App, port))
            {
                using (var socket = new Socket(SocketType.Stream, ProtocolType.IP))
                {
                    socket.Connect(IPAddress.Loopback, port);
                    socket.Disconnect(false);
                }
                await Task.Delay(200);
                Assert.True(server.IsClean(), "Is not clean");
            }
        }

        [Fact]
        public async Task EndingInRequestLineDoesNotEndConnection()
        {
            using (var server = new TestServer(App, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.SendEnd("POST / Ht");
                    await Task.Delay(200);
                    Assert.True(server.IsClean(), "Is not clean");
                }
            }
        }

        [Fact]
        public async Task EndingInHeadersDoesNotEndConnection()
        {
            using (var server = new TestServer(App, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.SendEnd(
                        "POST / Http/1.0",
                        "Conn"
                    );
                    await Task.Delay(200);
                    Assert.True(server.IsClean(), "Is not clean");
                }
            }
        }

        [Fact]
        public async Task EndingInContentDoesNotEndConnection()
        {
            using (var server = new TestServer(App, port))
            {
                using (var connection = new TestConnection(port))
                {
                    await connection.SendEnd(
                        "POST / HTTP/1.0",
                        "Content-Length: 100",
                        "",
                        "a");
                    await Task.Delay(200);
                    Assert.True(server.IsClean(), "Is not clean");
                }
            }
        }
    }
}