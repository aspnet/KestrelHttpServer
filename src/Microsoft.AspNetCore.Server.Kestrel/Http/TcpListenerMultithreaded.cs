// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Networking;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    /// <summary>
    /// A Windows-only implementation of <see cref="TcpListener"/> that disptaches connection processing
    /// to multiple threads/libuv loops.
    /// </summary>
    public class TcpListenerMultithreaded : TcpListener
    {
        private ListenerContext[] _secondaryListeners;
        private int _dispatchIndex;

        public TcpListenerMultithreaded(ServiceContext serviceContext, ServerAddress address, List<KestrelThread> threads)
            : base(serviceContext, address, threads[0])
        {
            _secondaryListeners = new ListenerContext[threads.Count - 1];

            for (int i = 1; i < threads.Count; i++)
            {
                var secondaryContext = new ListenerContext(serviceContext);
                secondaryContext.Thread = threads[i];
                secondaryContext.ServerAddress = address;
                _secondaryListeners[i - 1] = secondaryContext;
            }
        }

        /// <summary>
        /// Handles an incoming connection does round-robin dispatching to various kestrel threads
        /// </summary>
        /// <param name="listenSocket">Socket being used to listen on</param>
        /// <param name="status">Connection status</param>
        protected override void OnConnection(UvStreamHandle listenSocket, int status)
        {
            _dispatchIndex = (_dispatchIndex + 1) % _secondaryListeners.Length;
            var secondaryThread = _secondaryListeners[_dispatchIndex].Thread;

            try
            {
                // Block the primary thread until the client thread accepts the connection
                // to avoid potential race conditions.
                secondaryThread.PostAsync(state =>
                {
                    var listener = (TcpListenerMultithreaded)state;
                    listener.DispatchConnectionToContext();
                }, this).Wait();
            }
            catch (UvException ex)
            {
                Log.LogError("TcpListenerMultithreaded.OnConnection", ex);
                return;
            }
        }

        private void DispatchConnectionToContext()
        {
            var secondaryContext = _secondaryListeners[_dispatchIndex];
            var acceptSocket = new UvTcpHandle(Log);

            try
            {
                acceptSocket.Init(secondaryContext.Thread.Loop);
                acceptSocket.NoDelay(NoDelay);
                ListenSocket.Accept(acceptSocket, crossThread: true);
            }
            catch (UvException ex)
            {
                Log.LogError("DispatchConnectionToContext.DispatchConnectionToContext", ex);
            }

            var connection = new Connection(secondaryContext, acceptSocket);
            connection.Start();
        }

        public override void Dispose()
        {
            base.Dispose();

            var disposeTasks = new List<Task>();

            foreach (var listener in _secondaryListeners)
            {
                // Ensure the event loop is still running.
                // If the event loop isn't running and we try to wait on this Post
                // to complete, then KestrelEngine will never be disposed and
                // the exception that stopped the event loop will never be surfaced.
                if (listener.Thread.FatalError == null)
                {
                    var disposeTask = listener.Thread.PostAsync(state =>
                    {
                        var context = (ListenerContext)state;
                        var writeReqPool = context.WriteReqPool;
                        while (writeReqPool.Count > 0)
                        {
                            writeReqPool.Dequeue().Dispose();
                        }
                    }, listener);

                    disposeTasks.Add(disposeTask);
                }
            }

            Task.WhenAll(disposeTasks).Wait();
        }
    }
}
