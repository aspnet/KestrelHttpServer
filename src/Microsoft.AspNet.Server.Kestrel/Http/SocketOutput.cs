// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNet.Server.Kestrel.Networking;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    /// <summary>
    ///   Operations performed for buffered socket output
    /// </summary>
    public interface ISocketOutput
    {
        Task WriteAsync(ArraySegment<byte> buffer);
    }

    public class SocketOutput : ISocketOutput
    {
        private readonly KestrelThread _thread;
        private readonly UvTcpStreamHandle _socket;

        public SocketOutput(KestrelThread thread, UvTcpStreamHandle socket)
        {
            _thread = thread;
            _socket = socket;
        }

        public async Task WriteAsync(ArraySegment<byte> buffer)
        {
            //TODO: need buffering that works
            var copy = new byte[buffer.Count];
            Array.Copy(buffer.Array, buffer.Offset, copy, 0, buffer.Count);
            var arraySegment = new ArraySegment<byte>(copy);

            KestrelTrace.Log.ConnectionWrite(0, buffer.Count);
            using (var req = new UvWriteReq(
                _thread.Loop,
                _socket,
                arraySegment))
            {
                await _thread.PostAsync(req.Write);
                await req.Task;
            }
        }

        public bool Flush(Action drained)
        {
            return false;
        }
    }
}
