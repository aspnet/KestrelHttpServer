// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public abstract class MessageBody
    {
        private static readonly MessageBody _zeroContentLengthClose = new ForZeroContentLength(keepAlive: false);
        private static readonly MessageBody _zeroContentLengthKeepAlive = new ForZeroContentLength(keepAlive: true);

        private readonly HttpProtocol _context;

        private bool _send100Continue = true;
        private long _consumedBytes;
        private bool _timingReads;
        private bool _backpressure;

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

        public virtual async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
        {
            TryInit();

            while (true)
            {
                var result = await TimedReadAsync(cancellationToken);
                var readableBuffer = result.Buffer;
                var consumed = readableBuffer.End;
                var actual = 0;

                try
                {
                    if (!readableBuffer.IsEmpty)
                    {
                        // buffer.Count is int
                        actual = (int)Math.Min(readableBuffer.Length, buffer.Length);
                        var slice = readableBuffer.Slice(0, actual);
                        consumed = readableBuffer.GetPosition(actual);
                        slice.CopyTo(buffer.Span);

                        return actual;
                    }

                    if (result.IsCompleted)
                    {
                        return 0;
                    }
                }
                finally
                {
                    CompleteTimedRead(consumed, actual);
                }
            }
        }

        public virtual async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default(CancellationToken))
        {
            TryInit();

            while (true)
            {
                var result = await TimedReadAsync(cancellationToken);
                var readableBuffer = result.Buffer;
                var consumed = readableBuffer.End;
                var bytesRead = 0;

                try
                {
                    if (!readableBuffer.IsEmpty)
                    {
                        foreach (var memory in readableBuffer)
                        {
                            // REVIEW: This *could* be slower if 2 things are true
                            // - The WriteAsync(ReadOnlyMemory<byte>) isn't overridden on the destination
                            // - We change the Kestrel Memory Pool to not use pinned arrays but instead use native memory

                            bytesRead += memory.Length;

#if NETCOREAPP2_1
                            await destination.WriteAsync(memory, cancellationToken);
#elif NETSTANDARD2_0
                            var array = memory.GetArray();
                            await destination.WriteAsync(array.Array, array.Offset, array.Count, cancellationToken);
#else
#error TFMs need to be updated
#endif
                        }
                    }

                    if (result.IsCompleted)
                    {
                        return;
                    }
                }
                finally
                {
                    // TODO: We should't wait for app logic here
                    CompleteTimedRead(consumed, bytesRead);
                }
            }
        }

        public virtual Task ConsumeAsync()
        {
            TryInit();

            return OnConsumeAsync();
        }

        public virtual Task StopAsync()
        {
            TryStopTimingReads();

            return OnStopAsync();
        }

        protected abstract Task OnConsumeAsync();

        protected abstract Task OnStopAsync();

        protected void TryProduceContinue()
        {
            if (_send100Continue)
            {
                _context.HttpResponseControl.ProduceContinue();
                _send100Continue = false;
            }
        }

        private void TryInit()
        {
            if (!_context.HasStartedConsumingRequestBody)
            {
                OnReadStarting();
                _context.HasStartedConsumingRequestBody = true;
                TryInitializeTimingReads();
                OnReadStarted();
            }
        }

        protected virtual void OnReadStarting()
        {
        }

        protected virtual void OnReadStarted()
        {
        }

        protected virtual void OnDataRead(int bytesRead)
        {
        }

        protected void AddAndCheckConsumedBytes(long consumedBytes)
        {
            _consumedBytes += consumedBytes;

            if (_consumedBytes > _context.MaxRequestBodySize)
            {
                BadHttpRequestException.Throw(RequestRejectionReason.RequestBodyTooLarge);
            }
        }

        private ValueTask<ReadResult> TimedReadAsync(CancellationToken cancellationToken)
        {
            var readAwaitable = _context.RequestBodyPipe.Reader.ReadAsync(cancellationToken);

            if (!readAwaitable.IsCompleted && _timingReads)
            {
                _backpressure = true;
                _context.TimeoutControl.ResumeTimingReads();
            }

            return readAwaitable;
        }

        private void CompleteTimedRead(SequencePosition consumed, int bytesRead)
        {
            _context.RequestBodyPipe.Reader.AdvanceTo(consumed);

            _context.TimeoutControl.BytesRead(bytesRead);

            if (_timingReads && _backpressure)
            {
                _backpressure = false;
                _context.TimeoutControl.PauseTimingReads();
            }

            // Update the flow-control window after advancing the pipe reader, so we don't risk overfilling
            // the pipe despite the client being well-behaved.
            OnDataRead(bytesRead);
        }

        private void TryInitializeTimingReads()
        {
            if (!RequestUpgrade)
            {
                Log.RequestBodyStart(_context.ConnectionIdFeature, _context.TraceIdentifier);

                var minRate = _context.MinRequestBodyDataRate;

                if (minRate != null)
                {
                    _timingReads = true;
                    _context.TimeoutControl.InitializeTimingReads(minRate);
                }
            }
        }

        private void TryStopTimingReads()
        {
            if (!RequestUpgrade)
            {
                Log.RequestBodyDone(_context.ConnectionIdFeature, _context.TraceIdentifier);

                if (_timingReads)
                {
                    _context.TimeoutControl.StopTimingReads();
                }
            }
        }

        private class ForZeroContentLength : MessageBody
        {
            public ForZeroContentLength(bool keepAlive)
                : base(null)
            {
                RequestKeepAlive = keepAlive;
            }

            public override bool IsEmpty => true;

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default(CancellationToken)) => new ValueTask<int>(0);

            public override Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default(CancellationToken)) => Task.CompletedTask;

            public override Task ConsumeAsync() => Task.CompletedTask;

            public override Task StopAsync() => Task.CompletedTask;

            protected override Task OnConsumeAsync() => Task.CompletedTask;

            protected override Task OnStopAsync() => Task.CompletedTask;
        }
    }
}
