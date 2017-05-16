﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public class RequestBodyReader : IRequestBodyReader
    {
        private readonly IPipe _pipe;
        public RequestBodyReader(IPipe pipe)
        {
            _pipe = pipe;
        }

        public async Task StartAsync(MessageBody messageBody, CancellationToken cancellationToken = default(CancellationToken))
        {
            Exception error = null;

            try
            {
                while (true)
                {
                    var writableBuffer = _pipe.Writer.Alloc(1);
                    int bytesRead;

                    try
                    {
                        bytesRead = await messageBody.ReadAsync(writableBuffer.Buffer.GetArray(), cancellationToken);

                        if (bytesRead == 0)
                        {
                            break;
                        }

                        writableBuffer.Advance(bytesRead);
                    }
                    finally
                    {
                        writableBuffer.Commit();
                    }

                    var result = await writableBuffer.FlushAsync();
                    if (result.IsCompleted)
                    {
                        // Pipe reader is done
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                _pipe.Writer.Complete(error);
            }
        }

        public void Reset()
        {
            _pipe.Reader.Complete();
            _pipe.Writer.Complete();
            _pipe.Reset();
        }

        public virtual async Task<int> ReadAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
        {
            while (true)
            {
                var result = await _pipe.Reader.ReadAsync();
                var readableBuffer = result.Buffer;

                try
                {
                    if (!readableBuffer.IsEmpty)
                    {
                        var count = Math.Min(result.Buffer.Length, buffer.Count);
                        readableBuffer = result.Buffer.Slice(0, count);
                        readableBuffer.CopyTo(buffer);
                        return count;
                    }
                    else if (result.IsCompleted)
                    {
                        return 0;
                    }
                }
                finally
                {
                    _pipe.Reader.Advance(readableBuffer.End);
                }
            }
        }

        public virtual async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default(CancellationToken))
        {
            while (true)
            {
                var result = await _pipe.Reader.ReadAsync();
                var readableBuffer = result.Buffer;

                try
                {
                    if (!readableBuffer.IsEmpty)
                    {
                        foreach (var memory in readableBuffer)
                        {
                            var array = memory.GetArray();
                            await destination.WriteAsync(array.Array, array.Offset, array.Count);
                        }
                    }
                    else if (result.IsCompleted)
                    {
                        return;
                    }
                }
                finally
                {
                    _pipe.Reader.Advance(readableBuffer.End);
                }
            }
        }

        public Task ConsumeAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            while (true)
            {
                var awaitable = _pipe.Reader.ReadAsync();

                if (awaitable.IsCompleted)
                {
                    var result = awaitable.GetResult();
                    var readableBuffer = result.Buffer;
                    _pipe.Reader.Advance(readableBuffer.End);

                    if (result.IsCompleted)
                    {
                        return TaskCache.CompletedTask;
                    }
                }
                else
                {
                    return ConsumeAsyncAwaited(awaitable, cancellationToken);
                }
            }
        }

        private async Task ConsumeAsyncAwaited(ReadableBufferAwaitable awaitable, CancellationToken cancellationToken)
        {
            while (true)
            {
                var result = await awaitable;
                var readableBuffer = result.Buffer;
                _pipe.Reader.Advance(readableBuffer.End);

                if (result.IsCompleted)
                {
                    return;
                }

                awaitable = _pipe.Reader.ReadAsync();
            }
        }
    }
}
