// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNet.Server.Kestrel;
using Microsoft.AspNet.Server.Kestrel.Networking;
using Microsoft.Framework.Runtime;
using Microsoft.Framework.Runtime.Infrastructure;
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.AspNet.Server.KestrelTests
{
    /// <summary>
    /// Summary description for NetworkingTests
    /// </summary>
    public class NetworkingTests
    {
        public NetworkingTests()
        {
            new KestrelEngine(LibraryManager);
        }

        ILibraryManager LibraryManager
        {
            get
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
        }

        [Fact]
        public async Task LoopCanBeInitAndClose()
        {
            var loop = new UvLoopHandle();
            loop.Init();
            loop.Run();
            loop.Dispose();
        }

        [Fact]
        public async Task AsyncCanBeSent()
        {
            var loop = new UvLoopHandle();
            loop.Init();
            var trigger = new UvAsyncHandle();
            var called = false;
            trigger.Init(loop, () =>
            {
                called = true;
                trigger.Dispose();
            });
            trigger.Send();
            loop.Run();
            loop.Dispose();
            Assert.True(called);
        }

        [Fact]
        public async Task SocketCanBeInitAndClose()
        {
            var loop = new UvLoopHandle();
            loop.Init();
            var tcp = new UvTcpHandle();
            tcp.Init(loop);
            tcp.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            tcp.Dispose();
            loop.Run();
            loop.Dispose();
        }


        [Fact]
        public async Task SocketCanListenAndAccept()
        {
            var loop = new UvLoopHandle();
            loop.Init();
            var tcp = new UvTcpHandle();
            tcp.Init(loop);
            tcp.Bind(new IPEndPoint(IPAddress.Loopback, 54321));
            tcp.Listen(10, (stream, status, error, state) =>
            {
                var tcp2 = new UvTcpHandle();
                tcp2.Init(loop);
                stream.Accept(tcp2);
                tcp2.Dispose();
                stream.Dispose();
            }, null);
            var t = Task.Run(async () =>
            {
                var socket = new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);
                await Task.Factory.FromAsync(
                    socket.BeginConnect,
                    socket.EndConnect,
                    new IPEndPoint(IPAddress.Loopback, 54321),
                    null,
                    TaskCreationOptions.None);
                socket.Dispose();
            });
            loop.Run();
            loop.Dispose();
            await t;
        }


        [Fact]
        public async Task SocketCanRead()
        {
            int bytesRead = 0;
            var loop = new UvLoopHandle();
            loop.Init();
            var tcp = new UvTcpHandle();
            tcp.Init(loop);
            tcp.Bind(new IPEndPoint(IPAddress.Loopback, 54321));
            tcp.Listen(10, (_, status, error, state) =>
            {
                Console.WriteLine("Connected");
                var tcp2 = new UvTcpHandle();
                tcp2.Init(loop);
                tcp.Accept(tcp2);
                var data = Marshal.AllocCoTaskMem(500);
                tcp2.ReadStart(
                    (a, b, c) => new UvBuffer(data, 500),
                    (__, nread, error2, state2) =>
                    {
                        bytesRead += nread;
                        if (nread == 0)
                        {
                            tcp2.Dispose();
                        }
                    },
                    null);
                tcp.Dispose();
            }, null);
            Console.WriteLine("Task.Run");
            var t = Task.Run(async () =>
            {
                var socket = new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);
                await Task.Factory.FromAsync(
                    socket.BeginConnect,
                    socket.EndConnect,
                    new IPEndPoint(IPAddress.Loopback, 54321),
                    null,
                    TaskCreationOptions.None);
                await Task.Factory.FromAsync(
                    socket.BeginSend,
                    socket.EndSend,
                    new[] { new ArraySegment<byte>(new byte[] { 1, 2, 3, 4, 5 }) },
                    SocketFlags.None,
                    null,
                    TaskCreationOptions.None);
                socket.Dispose();
            });
            loop.Run();
            loop.Dispose();
            await t;
        }

        [Fact]
        public async Task SocketCanReadAndWrite()
        {
            int bytesRead = 0;
            var loop = new UvLoopHandle();
            loop.Init();
            var tcp = new UvTcpHandle();
            tcp.Init(loop);
            tcp.Bind(new IPEndPoint(IPAddress.Loopback, 54321));
            tcp.Listen(10, (_, status, error, state) =>
            {
                Console.WriteLine("Connected");
                var tcp2 = new UvTcpHandle();
                tcp2.Init(loop);
                tcp.Accept(tcp2);
                var data = Marshal.AllocCoTaskMem(500);
                tcp2.ReadStart(
                    (a, b, c) => new UvBuffer(data, 500),
                    (__, nread, error2, state2) =>
                    {
                        bytesRead += nread;
                        if (nread == 0)
                        {
                            tcp2.Dispose();
                        }
                        else
                        {
                            for (var x = 0; x != 2; ++x)
                            {
                                var req = new UvWriteReq();
                                req.Init(loop);
                                req.Write(
                                    tcp2,
                                    new ArraySegment<ArraySegment<byte>>(
                                        new[] { new ArraySegment<byte>(new byte[] { 65, 66, 67, 68, 69 }) }
                                        ),
                                    (_1, _2, _3, _4) => { },
                                    null);
                            }
                        }
                    },
                    null);
                tcp.Dispose();
            }, null);
            Console.WriteLine("Task.Run");
            var t = Task.Run(async () =>
            {
                var socket = new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);
                await Task.Factory.FromAsync(
                    socket.BeginConnect,
                    socket.EndConnect,
                    new IPEndPoint(IPAddress.Loopback, 54321),
                    null,
                    TaskCreationOptions.None);
                await Task.Factory.FromAsync(
                    socket.BeginSend,
                    socket.EndSend,
                    new[] { new ArraySegment<byte>(new byte[] { 1, 2, 3, 4, 5 }) },
                    SocketFlags.None,
                    null,
                    TaskCreationOptions.None);
                socket.Shutdown(SocketShutdown.Send);
                var buffer = new ArraySegment<byte>(new byte[2048]);
                for (; ;)
                {
                    var count = await Task.Factory.FromAsync(
                        socket.BeginReceive,
                        socket.EndReceive,
                        new[] { buffer },
                        SocketFlags.None,
                        null,
                        TaskCreationOptions.None);
                    Console.WriteLine("count {0} {1}",
                        count,
                        System.Text.Encoding.ASCII.GetString(buffer.Array, 0, count));
                    if (count <= 0) break;
                }
                socket.Dispose();
            });
            loop.Run();
            loop.Dispose();
            await t;
        }
    }
}