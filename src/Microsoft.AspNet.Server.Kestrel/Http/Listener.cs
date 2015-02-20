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
        private readonly Action<int, Exception> _connectionCallback;

        private UvTcpListenHandle _listenSocket;

        private void ConnectionCallback(int status, Exception error)
        {
            if (error != null)
            {
                Trace.WriteLine("Listener.ConnectionCallback " + error.ToString());
            }
            else
            {
                OnConnection(status);
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
                    _listenSocket = new UvTcpListenHandle(Thread.Loop);
                    _listenSocket.Bind(new IPEndPoint(IPAddress.Any, port));
                    _listenSocket.Listen(10, _connectionCallback);
                    tcs.SetResult(0);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);
            return tcs.Task;
        }

        private void OnConnection(int status)
        {
            var acceptSocket = new UvTcpStreamHandle(Thread.Loop);
            _listenSocket.Accept(acceptSocket);

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
                        _listenSocket.Dispose();
                        tcs.SetResult(0);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                },
                null);
            tcs.Task.Wait();
            _listenSocket = null;
        }
    }
}
