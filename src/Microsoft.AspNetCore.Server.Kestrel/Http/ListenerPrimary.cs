// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Networking;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    /// <summary>
    /// A primary listener waits for incoming connections on a specified socket. Incoming 
    /// connections may be passed to a secondary listener to handle.
    /// </summary>
    public abstract class ListenerPrimary : Listener
    {
        private readonly List<UvPipeHandle> _dispatchPipes = new List<UvPipeHandle>();
        private int _dispatchIndex;
        private string _pipeName;

        // this message is passed to write2 because it must be non-zero-length, 
        // but it has no other functional significance
        private readonly ArraySegment<ArraySegment<byte>> _dummyMessage = new ArraySegment<ArraySegment<byte>>(new[] { new ArraySegment<byte>(new byte[] { 1, 2, 3, 4 }) });

        protected ListenerPrimary(ServiceContext serviceContext, ServerAddress address, KestrelThread thread, string pipeName)
            : base(serviceContext, address, thread)
        {
            _pipeName = pipeName;
        }

        private UvPipeHandle ListenPipe { get; set; }

        public async Task StartPrimaryAsync()
        {
            await StartAsync().ConfigureAwait(false);

            await Thread.PostAsync(state => ((ListenerPrimary)state).PostCallback(), 
                                   this).ConfigureAwait(false);
        }

        private void PostCallback()
        {
            ListenPipe = new UvPipeHandle(Log);
            ListenPipe.Init(Thread.Loop, false);
            ListenPipe.Bind(_pipeName);
            ListenPipe.Listen(Constants.ListenBacklog,
                (pipe, status, error, state) => ((ListenerPrimary)state).OnListenPipe(pipe, status, error), this);
        }

        private void OnListenPipe(UvStreamHandle pipe, int status, Exception error)
        {
            if (status < 0)
            {
                return;
            }

            var dispatchPipe = new UvPipeHandle(Log);
            dispatchPipe.Init(Thread.Loop, true);

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
    }
}
