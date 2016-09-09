// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    class FrameRequestStream : Stream
    {
        private MessageBody _body;
        private FrameStreamState _state;
        private Exception _error;

        public FrameRequestStream()
        {
            _state = FrameStreamState.Closed;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }
            set
            {
                throw new NotSupportedException();
            }
        }

        public override void Flush()
        {
            // No-op.
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            // No-op.
            return TaskUtilities.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // ValueTask uses .GetAwaiter().GetResult() if necessary
            return ReadAsync(buffer, offset, count).Result;
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (destination == null)
            {
                return TaskUtilities.GetFaultedTask(new ArgumentNullException(nameof(destination)));
            }

            if (bufferSize <= 0)
            {
                return TaskUtilities.GetFaultedTask(new ArgumentOutOfRangeException(nameof(bufferSize)));
            }

            if (!destination.CanWrite)
            {
                return TaskUtilities.GetFaultedTask(new InvalidOperationException(nameof(destination) + " is not writable."));
            }

            var validateTask = ValidateState(cancellationToken);

            if (validateTask != null)
            {
                // Invalid state
                return validateTask;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                Task result = TaskUtilities.CompletedTask;
                while (!cancellationToken.IsCancellationRequested)
                {
                    var readTask = _body.ReadAsync(new ArraySegment<byte>(buffer, 0, buffer.Length), cancellationToken);
                    if (readTask.IsCompletedSuccessfully)
                    {
                        if (readTask.Result == 0)
                        {
                            // Complete, return buffer and return CompletedTask
                            result = TaskUtilities.CompletedTask;
                            break;
                        }

                        result = destination.WriteAsync(buffer, 0, readTask.Result, cancellationToken);

                        if (!result.IsCompleted)
                        {
                            // Not sync complete go async
                            return CopyToAsyncAwaited(null, result, destination, buffer, cancellationToken);
                        }
                        else if (result.Status != TaskStatus.RanToCompletion)
                        {
                            // Faulted task, return buffer and return faulting task as result
                            break;
                        }
                    }
                    else
                    {
                        // Not sync complete go async
                        return CopyToAsyncAwaited(readTask.AsTask(), null, destination, buffer, cancellationToken);
                    }
                }

                if (!result.IsFaulted && cancellationToken.IsCancellationRequested)
                {
                    // cancelled, return Cancelled task
                    result = TaskUtilities.GetCancelledTask(cancellationToken);
                }

                ArrayPool<byte>.Shared.Return(buffer);
                return result;
            }
            catch(Exception e)
            {
                // Exception thrown, return buffer and return faulted Task.
                ArrayPool<byte>.Shared.Return(buffer);
                return TaskUtilities.GetFaultedTask(e);
            }
        }

        private async Task CopyToAsyncAwaited(Task<int> incompleteReadTask, Task writeTask, Stream destination, byte[] buffer, CancellationToken cancellationToken)
        {
            try
            {
                int bytesRead;
                // Complete the incomplete task sequence
                if (incompleteReadTask != null)
                {
                    bytesRead = await incompleteReadTask;
                    if (bytesRead == 0)
                    {
                        return;
                    }
                    else
                    {
                        await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    }
                }
                else
                {
                    await writeTask;
                }

                // Normal async copy loop
                while ((bytesRead = await _body.ReadAsync(new ArraySegment<byte>(buffer, 0, buffer.Length), cancellationToken)) != 0)
                {
                    await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                }
            }
            finally
            {
                // Return the buffer
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

#if NET451
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            var task = ReadAsync(buffer, offset, count, default(CancellationToken), state);
            if (callback != null)
            {
                task.ContinueWith(t => callback.Invoke(t));
            }
            return task;
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            return ((Task<int>)asyncResult).GetAwaiter().GetResult();
        }

        private Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken, object state)
        {
            var tcs = new TaskCompletionSource<int>(state);
            var task = ReadAsync(buffer, offset, count, cancellationToken);
            task.ContinueWith((task2, state2) =>
            {
                var tcs2 = (TaskCompletionSource<int>)state2;
                if (task2.IsCanceled)
                {
                    tcs2.SetCanceled();
                }
                else if (task2.IsFaulted)
                {
                    tcs2.SetException(task2.Exception);
                }
                else
                {
                    tcs2.SetResult(task2.Result);
                }
            }, tcs, cancellationToken);
            return tcs.Task;
        }
#endif

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var task = ValidateState(cancellationToken);
            if (task == null)
            {
                // Needs .AsTask to match Stream's Async method return types
                return _body.ReadAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken).AsTask();
            }
            return task;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public void StartAcceptingReads(MessageBody body)
        {
            // Only start if not aborted
            if (_state == FrameStreamState.Closed)
            {
                _state = FrameStreamState.Open;
                _body = body;
            }
        }

        public void PauseAcceptingReads()
        {
            _state = FrameStreamState.Closed;
        }

        public void ResumeAcceptingReads()
        {
            if (_state == FrameStreamState.Closed)
            {
                _state = FrameStreamState.Open;
            }
        }

        public void StopAcceptingReads()
        {
            // Can't use dispose (or close) as can be disposed too early by user code
            // As exampled in EngineTests.ZeroContentLengthNotSetAutomaticallyForCertainStatusCodes
            _state = FrameStreamState.Closed;
            _body = null;
        }

        public void Abort(Exception error = null)
        {
            // We don't want to throw an ODE until the app func actually completes.
            // If the request is aborted, we throw an TaskCanceledException instead,
            // unless error is not null, in which case we throw it.
            if (_state != FrameStreamState.Closed)
            {
                _state = FrameStreamState.Aborted;
                _error = error;
            }
        }

        private Task<int> ValidateState(CancellationToken cancellationToken)
        {
            switch (_state)
            {
                case FrameStreamState.Open:
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return TaskUtilities.GetCancelledZeroTask(cancellationToken);
                    }
                    break;
                case FrameStreamState.Closed:
                    throw new ObjectDisposedException(nameof(FrameRequestStream));
                case FrameStreamState.Aborted:
                    return _error != null ?
                        TaskUtilities.GetFaultedTask(_error) :
                        TaskUtilities.GetCancelledZeroTask();
            }
            return null;
        }
    }
}
