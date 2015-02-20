// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNet.Server.Kestrel.Networking;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class ListenerContext
    {
        public ListenerContext() { }

        public ListenerContext(ListenerContext context)
        {
            Thread = context.Thread;
            Application = context.Application;
            Memory = context.Memory;
        }

        public KestrelThread Thread { get; set; }

        public Func<Frame, Task> Application { get; set; }

        public IMemoryPool Memory { get; set; }
    }

    /// <summary>
    /// Summary description for Accept
    /// </summary>
    public class Listener : ListenerContext, IDisposable
    {
        private readonly Action<UvTcpListenHandle, int, Exception> _connectionCallback;

        UvTcpListenHandle ListenSocket { get; set; }

        private void ConnectionCallback(UvTcpListenHandle stream, int status, Exception error)
        {
            if (error != null)
            {
                Trace.WriteLine("Listener.ConnectionCallback " + error.ToString());
            }
            else
            {
                OnConnection(stream, status);
            }
        }

        public Listener(IMemoryPool memory)
        {
            _connectionCallback = ConnectionCallback;
            Memory = memory;
        }

        public Task StartAsync(
            string scheme,
            string host,
            int port,
            KestrelThread thread,
            Func<Frame, Task> application)
        {
            Thread = thread;
            Application = application;

            var tcs = new TaskCompletionSource<int>();
            Thread.Post(_ =>
            {
                try
                {
                    ListenSocket = new UvTcpListenHandle(Thread.Loop);
                    ListenSocket.Bind(new IPEndPoint(IPAddress.Any, port));
                    ListenSocket.Listen(10, _connectionCallback);
                    tcs.SetResult(0);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);
            return tcs.Task;
        }

        private void OnConnection(UvTcpListenHandle listenSocket, int status)
        {
            var acceptSocket = new UvTcpStreamHandle(Thread.Loop);
            listenSocket.Accept(acceptSocket);

            var connection = new Connection(this, acceptSocket);
            connection.Start();
        }

        public void Dispose()
        {
            var tcs = new TaskCompletionSource<int>();
            Thread.Post(
                _ =>
                {
                    try
                    {
                        ListenSocket.Dispose();
                        tcs.SetResult(0);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                },
                null);
            tcs.Task.Wait();
            ListenSocket = null;
        }
    }
}
