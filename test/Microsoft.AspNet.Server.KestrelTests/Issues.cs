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
        public async Task DisconnectingClient()
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
    }
}