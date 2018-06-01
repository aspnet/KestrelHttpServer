﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal
{
    public class LibuvOutputConsumer
    {
        private readonly LibuvThread _thread;
        private readonly UvStreamHandle _socket;
        private readonly string _connectionId;
        private readonly ILibuvTrace _log;
        private readonly PipeReader _pipe;

        private long _totalBytesWritten;

        public LibuvOutputConsumer(
            PipeReader pipe,
            LibuvThread thread,
            UvStreamHandle socket,
            string connectionId,
            ILibuvTrace log)
        {
            _pipe = pipe;
            _thread = thread;
            _socket = socket;
            _connectionId = connectionId;
            _log = log;
        }

        public long TotalBytesWritten => Interlocked.Read(ref _totalBytesWritten);
        public Exception AbortException { get; private set; }

        public async Task WriteOutputAsync()
        {
            var pool = _thread.WriteReqPool;

            while (true)
            {
                ReadResult result;

                try
                {
                    result = await _pipe.ReadAsync();
                }
                catch (Exception ex)
                {
                    AbortException = ex;
                    return;
                }

                var buffer = result.Buffer;
                var consumed = buffer.End;

                try
                {
                    if (result.IsCanceled)
                    {
                        break;
                    }

                    if (!buffer.IsEmpty)
                    {
                        var writeReq = pool.Allocate();

                        try
                        {
                            if (_socket.IsClosed)
                            {
                                break;
                            }

                            var writeResult = await writeReq.WriteAsync(_socket, buffer);

                            // This is not interlocked because there could be a concurrent writer.
                            // Instead it's to prevent read tearing on 32-bit systems.
                            Interlocked.Add(ref _totalBytesWritten, buffer.Length);

                            LogWriteInfo(writeResult.Status, writeResult.Error);

                            if (writeResult.Error != null)
                            {
                                consumed = buffer.Start;
                                throw writeResult.Error;
                            }
                        }
                        finally
                        {
                            // Make sure we return the writeReq to the pool
                            pool.Return(writeReq);

                            // Null out writeReq so it doesn't get caught by CheckUvReqLeaks.
                            // It is rooted by a TestSink scope through Pipe continuations in
                            // ResponseTests.HttpsConnectionClosedWhenResponseDoesNotSatisfyMinimumDataRate
                            writeReq = null;
                        }
                    }
                    else if (result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    _pipe.AdvanceTo(consumed);
                }
            }
        }

        private void LogWriteInfo(int status, Exception error)
        {
            if (error == null)
            {
                _log.ConnectionWriteCallback(_connectionId, status);
            }
            else
            {
                // Log connection resets at a lower (Debug) level.
                if (status == LibuvConstants.ECANCELED)
                {
                    // Connection was aborted.
                }
                else if (LibuvConstants.IsConnectionReset(status))
                {
                    _log.ConnectionReset(_connectionId);
                }
                else
                {
                    _log.ConnectionError(_connectionId, error);
                }
            }
        }
    }
}
