// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNet.Server.Kestrel.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly List<IConnectionControl> _activeConnections = new List<IConnectionControl>();

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

            return Thread.PostAsync(() =>
            {
                _listenSocket = new UvTcpListenHandle(
                    Thread.Loop,
                    new IPEndPoint(IPAddress.Any, port),
                    10,
                    _connectionCallback);
            });
        }

        private void OnConnection(int status)
        {
            var acceptSocket = new UvTcpStreamHandle(Thread.Loop, _listenSocket);

            new Connection(this, acceptSocket);
        }

        public void AddConnection(Connection c)
        {
            _activeConnections.Add(c);
        }

        public void RemoveConnection(Connection c)
        {
            _activeConnections.Remove(c);
        }

        public void Dispose()
        {
            var task = Thread.PostAsync(_listenSocket.Dispose);
            task.Wait();

            var endTasks = new List<Task>();
            var copiedConnections = _activeConnections.ToList();
            foreach (var connection in copiedConnections)
            {
                if (!connection.IsInKeepAlive)
                    Console.WriteLine("TODO: Warning! Closing an active connection");
                endTasks.Add(connection.EndAsync(ProduceEndType.SocketShutdownSend));
                endTasks.Add(connection.EndAsync(ProduceEndType.SocketDisconnect));
            }
            Task.WaitAll(endTasks.ToArray());
            _listenSocket = null;
        }

        internal bool IsClean()
        {
            return _activeConnections.All(x => x.IsInKeepAlive);
        }
    }
}
