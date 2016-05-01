// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Networking.Uv.Interop;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Networking.Uv
{
    /// <summary>
    /// A primary listener waits for incoming connections on a specified socket. Incoming 
    /// connections may be passed to a secondary listener to handle.
    /// </summary>
    public abstract class UvListenerPrimary : UvListener
    {
        private readonly List<UvPipeHandle> _dispatchPipes = new List<UvPipeHandle>();
        private int _dispatchIndex;
        private string _pipeName;

        // this message is passed to write2 because it must be non-zero-length, 
        // but it has no other functional significance
        private readonly ArraySegment<ArraySegment<byte>> _dummyMessage = new ArraySegment<ArraySegment<byte>>(new[] { new ArraySegment<byte>(new byte[] { 1, 2, 3, 4 }) });

        protected UvListenerPrimary(ServiceContext serviceContext) : base(serviceContext)
        {
        }

        private UvPipeHandle ListenPipe { get; set; }

        public async Task StartAsync(
            string pipeName,
            ServerAddress address,
            UvThread thread)
        {
            _pipeName = pipeName;

            await StartAsync(address, thread).ConfigureAwait(false);

            await Thread.PostAsync(state => ((UvListenerPrimary)state).PostCallback(),
                                   this).ConfigureAwait(false);
        }

        private void PostCallback()
        {
            ListenPipe = new UvPipeHandle(Log);
            ListenPipe.Init(Thread.Loop, Thread.QueueCloseHandle, false);
            ListenPipe.Bind(_pipeName);
            ListenPipe.Listen(Constants.ListenBacklog,
                (pipe, status, error, state) => ((UvListenerPrimary)state).OnListenPipe(pipe, status, error), this);
        }

        private void OnListenPipe(UvStreamHandle pipe, int status, Exception error)
        {
            if (status < 0)
            {
                return;
            }

            var dispatchPipe = new UvPipeHandle(Log);
            dispatchPipe.Init(Thread.Loop, Thread.QueueCloseHandle, true);

            try
            {
                pipe.Accept(dispatchPipe);
            }
            catch (UvException ex)
            {
                dispatchPipe.Dispose();
                Log.LogError(0, ex, "ListenerPrimary.OnListenPipe");
                return;
            }

            _dispatchPipes.Add(dispatchPipe);
        }

        protected override void DispatchConnection(UvStreamHandle socket)
        {
            var index = _dispatchIndex++ % (_dispatchPipes.Count + 1);
            if (index == _dispatchPipes.Count)
            {
                base.DispatchConnection(socket);
            }
            else
            {
                var dispatchPipe = _dispatchPipes[index];
                var write = new UvWriteReq(Log);
                write.Init(Thread.Loop);
                write.Write2(
                    dispatchPipe,
                    _dummyMessage,
                    socket,
                    (write2, status, error, state) => 
                    {
                        write2.Dispose();
                        ((UvStreamHandle)state).Dispose();
                    },
                    socket);
            }
        }

        public override async Task DisposeAsync()
        {
            // Call base first so the ListenSocket gets closed and doesn't
            // try to dispatch connections to closed pipes.
            await base.DisposeAsync().ConfigureAwait(false);

            if (Thread.FatalError == null && ListenPipe != null)
            {
                await Thread.PostAsync(state =>
                {
                    var listener = (UvListenerPrimary)state;
                    listener.ListenPipe.Dispose();

                    foreach (var dispatchPipe in listener._dispatchPipes)
                    {
                        dispatchPipe.Dispose();
                    }
                }, this).ConfigureAwait(false);
            }
        }
    }
}
