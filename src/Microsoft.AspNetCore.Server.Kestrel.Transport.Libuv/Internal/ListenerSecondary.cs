// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal
{
    /// <summary>
    /// A secondary listener is delegated requests from a primary listener via a named pipe or
    /// UNIX domain socket.
    /// </summary>
    public class ListenerSecondary : ListenerContext, IAsyncDisposable
    {
        private string _pipeName;
        private byte[] _pipeMessage;
        private IntPtr _ptr;
        private LibuvFunctions.uv_buf_t _buf;
        private bool _closed;

        public ListenerSecondary(LibuvTransportContext transportContext) : base(transportContext)
        {
            _ptr = Marshal.AllocHGlobal(4);
        }

        UvPipeHandle DispatchPipe { get; set; }

        public ILibuvTrace Log => TransportContext.Log;

        public Task StartAsync(
            string pipeName,
            byte[] pipeMessage,
            IEndPointInformation endPointInformation,
            LibuvThread thread)
        {
            _pipeName = pipeName;
            _pipeMessage = pipeMessage;
            _buf = thread.Loop.Libuv.buf_init(_ptr, 4);

            EndPointInformation = endPointInformation;
            Thread = thread;
            DispatchPipe = new UvPipeHandle(Log);

            var tcs = new TaskCompletionSource<int>(this, TaskCreationOptions.RunContinuationsAsynchronously);
            Thread.Post(StartCallback, tcs);
            return tcs.Task;
        }

        private static void StartCallback(TaskCompletionSource<int> tcs)
        {
            var listener = (ListenerSecondary)tcs.Task.AsyncState;
            listener.StartedCallback(tcs);
        }

        private void StartedCallback(TaskCompletionSource<int> tcs)
        {
            var connect = new UvConnectRequest(Log);
            try
            {
                DispatchPipe.Init(Thread.Loop, Thread.QueueCloseHandle, true);
                connect.Init(Thread);
                connect.Connect(
                    DispatchPipe,
                    _pipeName,
                    (connect2, status, error, state) => ConnectCallback(connect2, status, error, (TaskCompletionSource<int>)state),
                    tcs);
            }
            catch (Exception ex)
            {
                DispatchPipe.Dispose();
                connect.Dispose();
                tcs.SetException(ex);
            }
        }

        private static void ConnectCallback(UvConnectRequest connect, int status, UvException error, TaskCompletionSource<int> tcs)
        {
            var listener = (ListenerSecondary)tcs.Task.AsyncState;
            _ = listener.ConnectedCallback(connect, status, error, tcs);
        }

        private async Task ConnectedCallback(UvConnectRequest connect, int status, UvException error, TaskCompletionSource<int> tcs)
        {
            connect.Dispose();
            if (error != null)
            {
                tcs.SetException(error);
                return;
            }

            var writeReq = new UvWriteReq(Log);

            try
            {
                DispatchPipe.ReadStart(
                    (handle, status2, state) => ((ListenerSecondary)state)._buf,
                    (handle, status2, state) => ((ListenerSecondary)state).ReadStartCallback(handle, status2),
                    this);

                writeReq.Init(Thread);
                var result = await writeReq.WriteAsync(
                     DispatchPipe,
                     new ArraySegment<ArraySegment<byte>>(new[] { new ArraySegment<byte>(_pipeMessage) }));

                if (result.Error != null)
                {
                    tcs.SetException(result.Error);
                }
                else
                {
                    tcs.SetResult(0);
                }
            }
            catch (Exception ex)
            {
                DispatchPipe.Dispose();
                tcs.SetException(ex);
            }
            finally
            {
                writeReq.Dispose();
            }
        }

        private void ReadStartCallback(UvStreamHandle handle, int status)
        {
            if (status < 0)
            {
                if (status != LibuvConstants.EOF)
                {
                    Thread.Loop.Libuv.Check(status, out var ex);
                    Log.LogError(0, ex, "DispatchPipe.ReadStart");
                }

                DispatchPipe.Dispose();
                return;
            }

            if (_closed || DispatchPipe.PendingCount() == 0)
            {
                return;
            }

            var acceptSocket = CreateAcceptSocket();

            try
            {
                DispatchPipe.Accept(acceptSocket);
            }
            catch (UvException ex)
            {
                Log.LogError(0, ex, "DispatchPipe.Accept");
                acceptSocket.Dispose();
                return;
            }

            try
            {
                var connection = new LibuvConnection(this, acceptSocket);
                _ = connection.Start();
            }
            catch (UvException ex)
            {
                Log.LogError(0, ex, "ListenerSecondary.OnConnection");
                acceptSocket.Dispose();
            }
        }

        private void FreeBuffer()
        {
            var ptr = Interlocked.Exchange(ref _ptr, IntPtr.Zero);
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public async Task DisposeAsync()
        {
            // Ensure the event loop is still running.
            // If the event loop isn't running and we try to wait on this Post
            // to complete, then LibuvTransport will never be disposed and
            // the exception that stopped the event loop will never be surfaced.
            if (Thread.FatalError == null)
            {
                await Thread.PostAsync(listener =>
                {
                    listener.DispatchPipe.Dispose();
                    listener.FreeBuffer();

                    listener._closed = true;

                }, this).ConfigureAwait(false);
            }
            else
            {
                FreeBuffer();
            }
        }
    }
}
