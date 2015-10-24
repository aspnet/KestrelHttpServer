// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.AspNet.Server.Kestrel.Networking;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    /// <summary>
    /// A secondary listener is delegated requests from a primary listener via a named pipe or 
    /// UNIX domain socket.
    /// </summary>
    public abstract class ListenerSecondary : ListenerContext, IDisposable
    {
        protected ListenerSecondary(ServiceContext serviceContext) : base(serviceContext)
        {
        }

        UvPipeHandle DispatchPipe { get; set; }

        public async Task StartAsync(
            string pipeName,
            ServerAddress address,
            KestrelThread thread,
            Func<Frame, Task> application)
        {
            ServerAddress = address;
            Thread = thread;
            Application = application;

            DispatchPipe = new UvPipeHandle(Log);

            var qscs = new QuadStateCompletionSource<ListenerSecondary, string, IntPtr, Libuv.uv_buf_t, int>(this, pipeName);
            Thread.Post(qscs2 =>
            {
                try
                {
                    var listener = qscs2.State1;
                    listener.DispatchPipe.Init(listener.Thread.Loop, true);
                    var connect = new UvConnectRequest(listener.Log);
                    connect.Init(listener.Thread.Loop);
                    connect.Connect(
                        listener.DispatchPipe,
                        qscs2.State2,
                        (connect2, status, error, state) =>
                        {
                            var qscs3 = (QuadStateCompletionSource<ListenerSecondary, string, IntPtr, Libuv.uv_buf_t, int>)state;
                            var listener2 = qscs3.State1;
                            connect2.Dispose();
                            if (error != null)
                            {
                                qscs3.SetException(error);
                                return;
                            }

                            try
                            {
                                var ptr = Marshal.AllocHGlobal(4);
                                var buf = listener2.Thread.Loop.Libuv.buf_init(ptr, 4);

                                qscs3.State3 = ptr;
                                qscs3.State4 = buf;

                                listener2.DispatchPipe.ReadStart(
                                    (handle, status2, state2) => ((QuadStateCompletionSource<ListenerSecondary, string, IntPtr, Libuv.uv_buf_t, int>)state2).State4,
                                    (handle, status2, state2) =>
                                    {
                                        var qscs4 = (QuadStateCompletionSource<ListenerSecondary, string, IntPtr, Libuv.uv_buf_t, int>)state2;
                                        var listener3 = qscs4.State1;
                                        if (status2 < 0)
                                        {
                                            if (status2 != Constants.EOF)
                                            {
                                                Exception ex;
                                                listener3.Thread.Loop.Libuv.Check(status2, out ex);
                                                listener3.Log.LogError("DispatchPipe.ReadStart", ex);
                                            }

                                            listener3.DispatchPipe.Dispose();
                                            Marshal.FreeHGlobal(qscs4.State3);
                                            return;
                                        }

                                        if (listener3.DispatchPipe.PendingCount() == 0)
                                        {
                                            return;
                                        }

                                        var acceptSocket = listener3.CreateAcceptSocket();

                                        try
                                        {
                                            listener3.DispatchPipe.Accept(acceptSocket);
                                        }
                                        catch (UvException ex)
                                        {
                                            listener3.Log.LogError("DispatchPipe.Accept", ex);
                                            acceptSocket.Dispose();
                                            return;
                                        }

                                        var connection = new Connection(listener3, acceptSocket);
                                        connection.Start();
                                    },
                                    qscs3);

                                qscs3.SetResult(0);
                            }
                            catch (Exception ex)
                            {
                                listener2.DispatchPipe.Dispose();
                                qscs3.SetException(ex);
                            }
                        },
                        qscs2);
                }
                catch (Exception ex)
                {
                    qscs2.State1.DispatchPipe.Dispose();
                    qscs2.SetException(ex);
                }
            }, qscs);
            await qscs;
            return;
        }

        /// <summary>
        /// Creates a socket which can be used to accept an incoming connection
        /// </summary>
        protected abstract UvStreamHandle CreateAcceptSocket();

        public void Dispose()
        {
            // Ensure the event loop is still running.
            // If the event loop isn't running and we try to wait on this Post
            // to complete, then KestrelEngine will never be disposed and
            // the exception that stopped the event loop will never be surfaced.
            if (Thread.FatalError == null)
            {
                Thread.Send(listener => ((ListenerSecondary)listener).DispatchPipe.Dispose(), this);
            }
        }
    }
}
