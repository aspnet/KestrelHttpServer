// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNet.Server.Kestrel.Networking;
using System;
using System.Runtime.InteropServices;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    /// <summary>
    ///   Operations performed for buffered socket output
    /// </summary>
    public interface ISocketOutput
    {
        void Write(ArraySegment<byte> buffer, Action<Exception, object> callback, object state);
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

        public void Write(ArraySegment<byte> buffer, Action<Exception, object> callback, object state)
        {
            //TODO: need buffering that works
            var copy = new byte[buffer.Count];
            Array.Copy(buffer.Array, buffer.Offset, copy, 0, buffer.Count);
            var arraySegment = new ArraySegment<byte>(copy);

            KestrelTrace.Log.ConnectionWrite(0, buffer.Count);
            var req = new UvWriteReq(
                _thread.Loop,
                _socket,
                arraySegment,
                callback,
                state);
            _thread.Post(x =>
            {
                ((UvWriteReq)x).Write();
            }, req);
        }

        public bool Flush(Action drained)
        {
            return false;
        }
    }
}
