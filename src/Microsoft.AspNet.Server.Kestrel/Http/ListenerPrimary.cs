// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.AspNet.Server.Kestrel.Networking;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    /// <summary>
    /// A primary listener waits for incoming connections on a specified socket. Incoming 
    /// connections may be passed to a secondary listener to handle.
    /// </summary>
    abstract public class ListenerPrimary : Listener
    {
        private List<UvPipeHandle> _dispatchPipes = new List<UvPipeHandle>();
        private int _dispatchIndex;

        // this message is passed to write2 because it must be non-zero-length, 
        // but it has no other functional significance
        private readonly byte[] _dummyBuffer = { 1, 2, 3, 4 };

        protected ListenerPrimary(ServiceContext serviceContext) : base(serviceContext)
        {
        }

        UvPipeHandle ListenPipe { get; set; }

        public async Task StartAsync(
            string pipeName,
            ServerAddress address,
            KestrelThread thread,
            RequestDelegate application)
        {
            await StartAsync(address, thread, application).ConfigureAwait(false);

            await Thread.PostAsync(_this =>
            {
                _this.ListenPipe = new UvPipeHandle(_this.Log);
                _this.ListenPipe.Init(_this.Thread.Loop, false);
                _this.ListenPipe.Bind(pipeName);
                _this.ListenPipe.Listen(Constants.ListenBacklog, (pipe, status, error, state) => ((ListenerPrimary)state).OnListenPipe(pipe, status, error), _this);
            }, this).ConfigureAwait(false);
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
                Log.LogError("ListenerPrimary.OnListenPipe", ex);
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
                var msg = MemoryPoolBlock.Create(new ArraySegment<byte>(_dummyBuffer), IntPtr.Zero, null, null);
                msg.End = msg.Start + _dummyBuffer.Length;

                var dispatchPipe = _dispatchPipes[index];
                var write = new UvWriteReq(Log);
                write.Init(Thread.Loop);
                write.Write2(
                    dispatchPipe,
                    new ArraySegment<MemoryPoolBlock>(new[] { msg }),
                    socket,
                    (write2, status, error, bytesWritten, state) => 
                    {
                        write2.Dispose();
                        ((UvStreamHandle)state).Dispose();
                    },
                    socket);
            }
        }
    }
}
