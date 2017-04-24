// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public class RequestBodyReader
    {
        public static readonly RequestBodyReader ZeroContentLengthClose = new EmptyRequestBodyReader();

        private readonly MessageBody _messageBody;
        private readonly IPipe _pipe;

        public RequestBodyReader(MessageBody messageBody, IPipe pipe)
        {
            _messageBody = messageBody;
            _pipe = pipe;
        }

        public bool RequestUpgrade => _messageBody.RequestUpgrade;

        public async Task StartAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            while (true)
            {
                var writableBuffer = _pipe.Writer.Alloc(1);

                try
                {
                    var bytesRead = await _messageBody.ReadAsync(writableBuffer.Buffer.GetArray(), cancellationToken);
                    writableBuffer.Advance(bytesRead);
                    writableBuffer.Commit();

                    if (bytesRead == 0)
                    {
                        _pipe.Writer.Complete();
                        return;
                    }

                    await writableBuffer.FlushAsync();
                }
                catch (Exception ex)
                {
                    writableBuffer.Commit();
                    _pipe.Writer.Complete(ex);
                    return;
                }
            }
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
                    _pipe.Reader.Advance(readableBuffer.End, readableBuffer.End);
                }
            }
        }

        public virtual async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default(CancellationToken))
        {
            while (true)
            {
                var readTask = _pipe.Reader.ReadAsync();

                if (!readTask.IsCompleted)
                {
                    _messageBody.TryProduceContinue();
                }

                var result = await readTask;
                var readableBuffer = result.Buffer;
                var segment = readableBuffer.First.GetArray();

                try
                {
                    if (!readableBuffer.IsEmpty)
                    {
                        await destination.WriteAsync(segment.Array, segment.Offset, segment.Count);
                    }
                    else if (readableBuffer.Length == 0)
                    {
                        return;
                    }
                }
                finally
                {
                    var consumed = readableBuffer.Move(readableBuffer.Start, segment.Count);
                    _pipe.Reader.Advance(consumed, consumed);
                }
            }
        }

        public async Task Consume(CancellationToken cancellationToken = default(CancellationToken))
        {
            while (true)
            {
                var result = await _pipe.Reader.ReadAsync();
                var readableBuffer = result.Buffer;

                try
                {
                    if (result.IsCompleted)
                    {
                        return;
                    }
                }
                finally
                {
                    _pipe.Reader.Advance(readableBuffer.End, readableBuffer.End);
                }
            }
        }

        private class EmptyRequestBodyReader : RequestBodyReader
        {
            public EmptyRequestBodyReader()
                : base(MessageBody.ZeroContentLengthClose, null)
            {
            }

            public override Task<int> ReadAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
                => Task.FromResult(0);

            public override Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default(CancellationToken))
                => TaskCache.CompletedTask;
        }
    }
}
