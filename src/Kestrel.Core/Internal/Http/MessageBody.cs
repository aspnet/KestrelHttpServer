// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public abstract class MessageBody
    {
        private static readonly MessageBody _zeroContentLengthClose = new ForZeroContentLength(keepAlive: false);
        private static readonly MessageBody _zeroContentLengthKeepAlive = new ForZeroContentLength(keepAlive: true);

        private readonly HttpProtocol _context;

        protected MessageBody(HttpProtocol context)
        {
            _context = context;
        }

        public static MessageBody ZeroContentLengthClose => _zeroContentLengthClose;

        public static MessageBody ZeroContentLengthKeepAlive => _zeroContentLengthKeepAlive;

        public bool RequestKeepAlive { get; protected set; }

        public bool RequestUpgrade { get; protected set; }

        public virtual bool IsEmpty => false;

        protected IKestrelTrace Log => _context.ServiceContext.Log;

        public virtual async Task<int> ReadAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
        {
            TryInit();

            while (true)
            {
                var result = await _context.RequestBodyPipe.Reader.ReadAsync();
                var readableBuffer = result.Buffer;
                var consumed = readableBuffer.End;

                try
                {
                    if (!readableBuffer.IsEmpty)
                    {
                        //  buffer.Count is int
                        var actual = (int) Math.Min(readableBuffer.Length, buffer.Count);
                        var slice = readableBuffer.Slice(0, actual);
                        consumed = readableBuffer.Move(readableBuffer.Start, actual);
                        slice.CopyTo(buffer);
                        return actual;
                    }
                    else if (result.IsCompleted)
                    {
                        return 0;
                    }
                }
                finally
                {
                    _context.RequestBodyPipe.Reader.Advance(consumed);
                }
            }
        }

        public virtual async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default(CancellationToken))
        {
            TryInit();

            while (true)
            {
                var result = await _context.RequestBodyPipe.Reader.ReadAsync();
                var readableBuffer = result.Buffer;
                var consumed = readableBuffer.End;

                try
                {
                    if (!readableBuffer.IsEmpty)
                    {
                        foreach (var memory in readableBuffer)
                        {
                            var array = memory.GetArray();
                            await destination.WriteAsync(array.Array, array.Offset, array.Count, cancellationToken);
                        }
                    }
                    else if (result.IsCompleted)
                    {
                        return;
                    }
                }
                finally
                {
                    _context.RequestBodyPipe.Reader.Advance(consumed);
                }
            }
        }

        public virtual Task ConsumeAsync()
        {
            TryInit();

            return OnConsumeAsync();
        }

        protected abstract Task OnConsumeAsync();

        public abstract Task StopAsync();

        private void TryInit()
        {
            if (!_context.HasStartedConsumingRequestBody)
            {
                OnReadStarting();
                _context.HasStartedConsumingRequestBody = true;
                OnReadStarted();
            }
        }

        protected virtual void OnReadStarting()
        {
        }

        protected virtual void OnReadStarted()
        {
        }

        private class ForZeroContentLength : MessageBody
        {
            public ForZeroContentLength(bool keepAlive)
                : base(null)
            {
                RequestKeepAlive = keepAlive;
            }

            public override bool IsEmpty => true;

            public override Task<int> ReadAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default(CancellationToken)) => Task.FromResult(0);

            public override Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default(CancellationToken)) => Task.CompletedTask;

            public override Task ConsumeAsync() => Task.CompletedTask;

            public override Task StopAsync() => Task.CompletedTask;

            protected override Task OnConsumeAsync() => Task.CompletedTask;
        }
    }
}
