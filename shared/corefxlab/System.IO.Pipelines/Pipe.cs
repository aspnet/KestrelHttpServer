// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    /// <summary>
    /// Default <see cref="IPipeWriter"/> and <see cref="IPipeReader"/> implementation.
    /// </summary>
    internal class Pipe : IPipe, IPipeReader, IPipeWriter, IReadableBufferAwaiter, IWritableBufferAwaiter
    {
        private static readonly Action<object> _signalReaderAwaitable = state => ((Pipe)state).ReaderCancellationRequested();
        private static readonly Action<object> _signalWriterAwaitable = state => ((Pipe)state).WriterCancellationRequested();
        private static readonly Action<object> _invokeCompletionCallbacks = state => ((PipeCompletionCallbacks)state).Execute();
        private static readonly Action<object> _scheduleContinuation = o => ((Action)o)();

        // This sync objects protects the following state:
        // 1. _commitHead & _commitHeadIndex
        // 2. _length
        // 3. _readerAwaitable & _writerAwaitable
        private readonly object _sync = new object();

        private readonly BufferPool _pool;
        private readonly long _maximumSizeHigh;
        private readonly long _maximumSizeLow;

        private readonly IScheduler _readerScheduler;
        private readonly IScheduler _writerScheduler;

        private long _length;
        private long _currentWriteLength;

        private PipeAwaitable _readerAwaitable;
        private PipeAwaitable _writerAwaitable;

        private PipeCompletion _writerCompletion;
        private PipeCompletion _readerCompletion;

        // The read head which is the extent of the IPipelineReader's consumed bytes
        private BufferSegment _readHead;

        // The commit head which is the extent of the bytes available to the IPipelineReader to consume
        private BufferSegment _commitHead;
        private int _commitHeadIndex;

        // The write head which is the extent of the IPipelineWriter's written bytes
        private BufferSegment _writingHead;

        private PipeOperationState _readingState;
        private PipeOperationState _writingState;

        private bool _disposed;

        internal long Length => _length;

        /// <summary>
        /// Initializes the <see cref="Pipe"/> with the specifed <see cref="IBufferPool"/>.
        /// </summary>
        /// <param name="pool"></param>
        /// <param name="options"></param>
        public Pipe(BufferPool pool, PipeOptions options = null)
        {
            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            options = options ?? new PipeOptions();

            if (options.MaximumSizeLow < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options.MaximumSizeLow));
            }

            if (options.MaximumSizeHigh < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options.MaximumSizeHigh));
            }

            if (options.MaximumSizeLow > options.MaximumSizeHigh)
            {
                throw new ArgumentException(nameof(options.MaximumSizeHigh) + " should be greater or equal to " + nameof(options.MaximumSizeLow), nameof(options.MaximumSizeHigh));
            }

            _pool = pool;
            _maximumSizeHigh = options.MaximumSizeHigh;
            _maximumSizeLow = options.MaximumSizeLow;
            _readerScheduler = options.ReaderScheduler ?? InlineScheduler.Default;
            _writerScheduler = options.WriterScheduler ?? InlineScheduler.Default;
            _readerAwaitable = new PipeAwaitable(completed: false);
            _writerAwaitable = new PipeAwaitable(completed: true);
        }

        private void ResetState()
        {
            _readerCompletion.Reset();
            _writerCompletion.Reset();
            _commitHeadIndex = 0;
            _currentWriteLength = 0;
            _length = 0;
        }

        internal Buffer<byte> Buffer => _writingHead?.Buffer.Slice(_writingHead.End, _writingHead.WritableBytes) ?? Buffer<byte>.Empty;

        /// <summary>
        /// Allocates memory from the pipeline to write into.
        /// </summary>
        /// <param name="minimumSize">The minimum size buffer to allocate</param>
        /// <returns>A <see cref="WritableBuffer"/> that can be written to.</returns>
        WritableBuffer IPipeWriter.Alloc(int minimumSize)
        {
            if (_writerCompletion.IsCompleted)
            {
                PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.NoWritingAllowed, _writerCompletion.Location);
            }

            if (minimumSize < 0)
            {
                PipelinesThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.minimumSize);
            }

            lock (_sync)
            {
                // CompareExchange not required as its setting to current value if test fails
                _writingState.Begin(ExceptionResource.AlreadyWriting);

                if (minimumSize > 0)
                {
                    try
                    {
                        AllocateWriteHeadUnsynchronized(minimumSize);
                    }
                    catch (Exception)
                    {
                        // Reset producing state if allocation failed
                        _writingState.End(ExceptionResource.NoWriteToComplete);
                        throw;
                    }
                }

                _currentWriteLength = 0;
                return new WritableBuffer(this);
            }
        }

        internal void Ensure(int count = 1)
        {
            EnsureAlloc();

            var segment = _writingHead;
            if (segment == null)
            {
                // Changing commit head shared with Reader
                lock (_sync)
                {
                    segment = AllocateWriteHeadUnsynchronized(count);
                }
            }

            var bytesLeftInBuffer = segment.WritableBytes;

            // If inadequate bytes left or if the segment is readonly
            if (bytesLeftInBuffer == 0 || bytesLeftInBuffer < count || segment.ReadOnly)
            {
                var nextBuffer = _pool.Rent(count);
                var nextSegment = new BufferSegment(nextBuffer);

                segment.Next = nextSegment;

                _writingHead = nextSegment;
            }
        }

        private BufferSegment AllocateWriteHeadUnsynchronized(int count)
        {
            BufferSegment segment = null;

            if (_commitHead != null && !_commitHead.ReadOnly)
            {
                // Try to return the tail so the calling code can append to it
                int remaining = _commitHead.WritableBytes;

                if (count <= remaining)
                {
                    // Free tail space of the right amount, use that
                    segment = _commitHead;
                }
            }

            if (segment == null)
            {
                // No free tail space, allocate a new segment
                segment = new BufferSegment(_pool.Rent(count));
            }

            if (_commitHead == null)
            {
                // No previous writes have occurred
                _commitHead = segment;
            }
            else if (segment != _commitHead && _commitHead.Next == null)
            {
                // Append the segment to the commit head if writes have been committed
                // and it isn't the same segment (unused tail space)
                _commitHead.Next = segment;
            }

            // Set write head to assigned segment
            _writingHead = segment;

            return segment;
        }

        internal void Append(ReadableBuffer buffer)
        {
            if (buffer.IsEmpty)
            {
                return; // nothing to do
            }

            EnsureAlloc();

            BufferSegment clonedEnd;
            var clonedBegin = BufferSegment.Clone(buffer.Start, buffer.End, out clonedEnd);

            if (_writingHead == null)
            {
                // No active write
                lock (_sync)
                {
                    if (_commitHead == null)
                    {
                        // No allocated buffers yet, not locking as _readHead will be null
                        _commitHead = clonedBegin;
                    }
                    else
                    {
                        Debug.Assert(_commitHead.Next == null);
                        // Allocated buffer, append as next segment
                        _commitHead.Next = clonedBegin;
                    }
                }
            }
            else
            {
                Debug.Assert(_writingHead.Next == null);
                // Active write, append as next segment
                _writingHead.Next = clonedBegin;
            }

            // Move write head to end of buffer
            _writingHead = clonedEnd;
            _currentWriteLength += buffer.Length;
        }

        private void EnsureAlloc()
        {
            if (!_writingState.IsActive)
            {
                PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.NotWritingNoAlloc);
            }
        }

        internal void Commit()
        {
            // Changing commit head shared with Reader
            lock (_sync)
            {
                CommitUnsynchronized();
            }
        }

        internal void CommitUnsynchronized()
        {
            _writingState.End(ExceptionResource.NoWriteToComplete);

            if (_writingHead == null)
            {
                // Nothing written to commit
                return;
            }

            if (_readHead == null)
            {
                // Update the head to point to the head of the buffer.
                // This happens if we called alloc(0) then write
                _readHead = _commitHead;
            }

            // Always move the commit head to the write head
            _commitHead = _writingHead;
            _commitHeadIndex = _writingHead.End;
            _length += _currentWriteLength;

            // Do not reset if reader is complete
            if (_maximumSizeHigh > 0 &&
                _length >= _maximumSizeHigh &&
                !_readerCompletion.IsCompleted)
            {
                _writerAwaitable.Reset();
            }
            // Clear the writing state
            _writingHead = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AdvanceWriter(int bytesWritten)
        {
            EnsureAlloc();
            if (bytesWritten > 0)
            {
                if (_writingHead == null)
                {
                    PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.AdvancingWithNoBuffer);
                }

                Debug.Assert(!_writingHead.ReadOnly);
                Debug.Assert(_writingHead.Next == null);

                var buffer = _writingHead.Buffer;
                var bufferIndex = _writingHead.End + bytesWritten;

                if (bufferIndex > buffer.Length)
                {
                    PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.AdvancingPastBufferSize);
                }

                _writingHead.End = bufferIndex;
                _currentWriteLength += bytesWritten;
            }
            else if (bytesWritten < 0)
            {
                PipelinesThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.bytesWritten);
            } // and if zero, just do nothing; don't need to validate tail etc
        }

        internal WritableBufferAwaitable FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            Action awaitable;
            CancellationTokenRegistration cancellationTokenRegistration;
            lock (_sync)
            {
                if (_writingState.IsActive)
                {
                    // Commit the data as not already committed
                    CommitUnsynchronized();
                }

                awaitable = _readerAwaitable.Complete();

                cancellationTokenRegistration = _writerAwaitable.AttachToken(cancellationToken, _signalWriterAwaitable, this);
            }

            cancellationTokenRegistration.Dispose();

            TrySchedule(_readerScheduler, awaitable);

            return new WritableBufferAwaitable(this);
        }

        internal ReadableBuffer AsReadableBuffer()
        {
            if (_writingHead == null)
            {
                return new ReadableBuffer(); // Nothing written return empty
            }

            ReadCursor readStart;
            lock (_sync)
            {
                readStart = new ReadCursor(_commitHead, _commitHeadIndex);
            }

            return new ReadableBuffer(readStart, new ReadCursor(_writingHead, _writingHead.End));
        }

        /// <summary>
        /// Marks the pipeline as being complete, meaning no more items will be written to it.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the pipeline to complete.</param>
        void IPipeWriter.Complete(Exception exception)
        {
            if (_writingState.IsActive)
            {
                PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.CompleteWriterActiveWriter, _writingState.Location);
            }

            Action awaitable;
            PipeCompletionCallbacks completionCallbacks;
            bool readerCompleted;

            lock (_sync)
            {
                completionCallbacks = _writerCompletion.TryComplete(exception);
                awaitable = _readerAwaitable.Complete();
                readerCompleted = _readerCompletion.IsCompleted;
            }

            if (completionCallbacks != null)
            {
                TrySchedule(_readerScheduler, _invokeCompletionCallbacks, completionCallbacks);
            }

            TrySchedule(_readerScheduler, awaitable);

            if (readerCompleted)
            {
                Dispose();
            }
        }


        // Reading

        void IPipeReader.Advance(ReadCursor consumed, ReadCursor examined)
        {
            BufferSegment returnStart = null;
            BufferSegment returnEnd = null;

            // Reading commit head shared with writer
            Action continuation = null;
            lock (_sync)
            {
                var examinedEverything = examined.Segment == _commitHead && examined.Index == _commitHeadIndex;

                if (!consumed.IsDefault)
                {
                    if (_readHead == null)
                    {
                        PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.AdvanceToInvalidCursor);
                        return;
                    }

                    returnStart = _readHead;
                    returnEnd = consumed.Segment;

                    // Check if we crossed _maximumSizeLow and complete backpressure
                    var consumedBytes = ReadCursor.GetLength(returnStart, returnStart.Start, consumed.Segment, consumed.Index);
                    var oldLength = _length;
                    _length -= consumedBytes;

                    if (oldLength >= _maximumSizeLow &&
                        _length < _maximumSizeLow)
                    {
                        continuation = _writerAwaitable.Complete();
                    }

                    // Check if we consumed entire last segment
                    // if we are going to return commit head
                    // we need to check that there is no writing operation that
                    // might be using tailspace
                    if (consumed.Index == returnEnd.End &&
                        !(_commitHead == returnEnd && _writingState.IsActive))
                    {
                        var nextBlock = returnEnd.Next;
                        if (_commitHead == returnEnd)
                        {
                            _commitHead = nextBlock;
                            _commitHeadIndex = nextBlock?.Start ?? 0;
                        }

                        _readHead = nextBlock;
                        returnEnd = nextBlock;
                    }
                    else
                    {
                        _readHead = consumed.Segment;
                        _readHead.Start = consumed.Index;
                    }
                }

                // We reset the awaitable to not completed if we've examined everything the producer produced so far
                // but only if writer is not completed yet
                if (examinedEverything && !_writerCompletion.IsCompleted)
                {
                    // Prevent deadlock where reader awaits new data and writer await backpressure
                    if (!_writerAwaitable.IsCompleted)
                    {
                        PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.BackpressureDeadlock);
                    }
                    _readerAwaitable.Reset();
                }

                _readingState.End(ExceptionResource.NoReadToComplete);
            }

            while (returnStart != null && returnStart != returnEnd)
            {
                returnStart.Dispose();
                returnStart = returnStart.Next;
            }

            TrySchedule(_writerScheduler, continuation);
        }

        /// <summary>
        /// Signal to the producer that the consumer is done reading.
        /// </summary>
        /// <param name="exception">Optional Exception indicating a failure that's causing the pipeline to complete.</param>
        void IPipeReader.Complete(Exception exception)
        {
            if (_readingState.IsActive)
            {
                PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.CompleteReaderActiveReader, _readingState.Location);
            }

            PipeCompletionCallbacks completionCallbacks;
            Action awaitable;
            bool writerCompleted;

            lock (_sync)
            {
                completionCallbacks = _readerCompletion.TryComplete(exception);
                awaitable = _writerAwaitable.Complete();
                writerCompleted = _writerCompletion.IsCompleted;
            }

            if (completionCallbacks != null)
            {
                TrySchedule(_writerScheduler, _invokeCompletionCallbacks, completionCallbacks);
            }

            TrySchedule(_writerScheduler, awaitable);

            if (writerCompleted)
            {
                Dispose();
            }
        }

        void IPipeReader.OnWriterCompleted(Action<Exception, object> callback, object state)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            PipeCompletionCallbacks completionCallbacks;
            lock (_sync)
            {
                completionCallbacks = _writerCompletion.AddCallback(callback, state);
            }

            if (completionCallbacks != null)
            {
                TrySchedule(_readerScheduler, _invokeCompletionCallbacks, completionCallbacks);
            }
        }

        /// <summary>
        /// Cancel to currently pending call to <see cref="ReadAsync"/> without completing the <see cref="IPipeReader"/>.
        /// </summary>
        void IPipeReader.CancelPendingRead()
        {
            Action awaitable;
            lock (_sync)
            {
                awaitable = _readerAwaitable.Cancel();
            }
            TrySchedule(_readerScheduler, awaitable);
        }

        /// <summary>
        /// Cancel to currently pending call to <see cref="WritableBuffer.FlushAsync"/> without completing the <see cref="IPipeWriter"/>.
        /// </summary>
        void IPipeWriter.CancelPendingFlush()
        {
            Action awaitable;
            lock (_sync)
            {
                awaitable = _writerAwaitable.Cancel();
            }
            TrySchedule(_writerScheduler, awaitable);
        }

        void IPipeWriter.OnReaderCompleted(Action<Exception, object> callback, object state)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            PipeCompletionCallbacks completionCallbacks;
            lock (_sync)
            {
                completionCallbacks = _readerCompletion.AddCallback(callback, state);
            }

            if (completionCallbacks != null)
            {
                TrySchedule(_writerScheduler, _invokeCompletionCallbacks, completionCallbacks);
            }
        }

        ReadableBufferAwaitable IPipeReader.ReadAsync(CancellationToken token)
        {
            CancellationTokenRegistration cancellationTokenRegistration;
            if (_readerCompletion.IsCompleted)
            {
                PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.NoReadingAllowed, _readerCompletion.Location);
            }
            lock (_sync)
            {
                cancellationTokenRegistration = _readerAwaitable.AttachToken(token, _signalReaderAwaitable, this);
            }
            cancellationTokenRegistration.Dispose();
            return new ReadableBufferAwaitable(this);
        }

        bool IPipeReader.TryRead(out ReadResult result)
        {
            lock (_sync)
            {
                if (_readerCompletion.IsCompleted)
                {
                    PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.NoReadingAllowed, _readerCompletion.Location);
                }

                result = new ReadResult();
                if (_length > 0 || _readerAwaitable.IsCompleted)
                {
                    GetResult(ref result);
                    return true;
                }

                if (_readerAwaitable.HasContinuation)
                {
                    PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.AlreadyReading);
                }
                return false;
            }
        }

        private static void TrySchedule(IScheduler scheduler, Action action)
        {
            if (action != null)
            {
                scheduler.Schedule(_scheduleContinuation, action);
            }
        }

        private static void TrySchedule(IScheduler scheduler, Action<object> action, object state)
        {
            if (action != null)
            {
                scheduler.Schedule(action, state);
            }
        }

        private void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                // Return all segments
                var segment = _readHead;
                while (segment != null)
                {
                    var returnSegment = segment;
                    segment = segment.Next;

                    returnSegment.Dispose();
                }

                _readHead = null;
                _commitHead = null;
            }
        }

        // IReadableBufferAwaiter members

        bool IReadableBufferAwaiter.IsCompleted => _readerAwaitable.IsCompleted;

        void IReadableBufferAwaiter.OnCompleted(Action continuation)
        {
            Action awaitable;
            bool doubleCompletion;
            lock (_sync)
            {
                awaitable = _readerAwaitable.OnCompleted(continuation, out doubleCompletion);
            }
            if (doubleCompletion)
            {
                Writer.Complete(PipelinesThrowHelper.GetInvalidOperationException(ExceptionResource.NoConcurrentOperation));
            }
            TrySchedule(_readerScheduler, awaitable);
        }

        ReadResult IReadableBufferAwaiter.GetResult()
        {
            if (!_readerAwaitable.IsCompleted)
            {
                PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.GetResultNotCompleted);
            }

            var result = new ReadResult();
            lock (_sync)
            {
                GetResult(ref result);
            }
            return result;
        }

        private void GetResult(ref ReadResult result)
        {
            if (_writerCompletion.IsCompletedOrThrow())
            {
                result.ResultFlags |= ResultFlags.Completed;
            }

            var isCancelled = _readerAwaitable.ObserveCancelation();
            if (isCancelled)
            {
                result.ResultFlags |= ResultFlags.Cancelled;
            }

            // No need to read end if there is no head
            var head = _readHead;

            if (head != null)
            {
                // Reading commit head shared with writer
                result.ResultBuffer.BufferEnd.Segment = _commitHead;
                result.ResultBuffer.BufferEnd.Index = _commitHeadIndex;
                result.ResultBuffer.BufferLength = ReadCursor.GetLength(head, head.Start, _commitHead, _commitHeadIndex);

                result.ResultBuffer.BufferStart.Segment = head;
                result.ResultBuffer.BufferStart.Index = head.Start;
            }

            if (isCancelled)
            {
                _readingState.BeginTentative(ExceptionResource.AlreadyReading);
            }
            else
            {
                _readingState.Begin(ExceptionResource.AlreadyReading);
            }
        }

        // IWritableBufferAwaiter members

        bool IWritableBufferAwaiter.IsCompleted => _writerAwaitable.IsCompleted;

        FlushResult IWritableBufferAwaiter.GetResult()
        {
            var result = new FlushResult();
            lock (_sync)
            {
                if (!_writerAwaitable.IsCompleted)
                {
                    PipelinesThrowHelper.ThrowInvalidOperationException(ExceptionResource.GetResultNotCompleted);
                }

                // Change the state from to be cancelled -> observed
                if (_writerAwaitable.ObserveCancelation())
                {
                    result.ResultFlags |= ResultFlags.Cancelled;
                }
                if (_readerCompletion.IsCompletedOrThrow())
                {
                    result.ResultFlags |= ResultFlags.Completed;
                }
            }

            return result;
        }

        void IWritableBufferAwaiter.OnCompleted(Action continuation)
        {
            Action awaitable;
            bool doubleCompletion;
            lock (_sync)
            {
                awaitable = _writerAwaitable.OnCompleted(continuation, out doubleCompletion);
            }
            if (doubleCompletion)
            {
                Reader.Complete(PipelinesThrowHelper.GetInvalidOperationException(ExceptionResource.NoConcurrentOperation));
            }
            TrySchedule(_writerScheduler, awaitable);
        }

        private void ReaderCancellationRequested()
        {
            Action action;
            lock (_sync)
            {
                action = _readerAwaitable.Cancel();
            }
            TrySchedule(_readerScheduler, action);
        }

        private void WriterCancellationRequested()
        {
            Action action;
            lock (_sync)
            {
                action = _writerAwaitable.Cancel();
            }
            TrySchedule(_writerScheduler, action);
        }

        public IPipeReader Reader => this;
        public IPipeWriter Writer => this;

        public void Reset()
        {
            lock (_sync)
            {
                if (!_disposed)
                {
                    throw new InvalidOperationException("Both reader and writer need to be completed to be able to reset ");
                }

                _disposed = false;
                ResetState();
            }
        }
    }
}
