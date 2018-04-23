// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public partial class HttpProtocol
    {

        /// <summary>Used by CopyToAsync to enable awaiting the result of an overlapped I/O operation with minimal overhead.</summary>
        private sealed unsafe class AsyncCopyToAwaitable : ICriticalNotifyCompletion
        {
            /// <summary>Sentinel object used to indicate that the I/O operation has completed before being awaited.</summary>
            private readonly static Action s_sentinel = () => { };
            /// <summary>Cached delegate to IOCallback.</summary>
            internal static readonly IOCompletionCallback s_callback = IOCallback;

            // <summary>The FileStream that owns this instance.</summary>
            //internal readonly FileStream _fileStream;

            /// <summary>Tracked position representing the next location from which to read.</summary>
            internal long _position;
            /// <summary>The current native overlapped pointer.  This changes for each operation.</summary>
            internal NativeOverlapped* _nativeOverlapped;
            /// <summary>
            /// null if the operation is still in progress,
            /// s_sentinel if the I/O operation completed before the await,
            /// s_callback if it completed after the await yielded.
            /// </summary>
            internal Action _continuation;
            /// <summary>Last error code from completed operation.</summary>
            internal Win32ErrorCode _errorCode;
            /// <summary>Last number of read bytes from completed operation.</summary>
            internal uint _numBytes;

            internal ThreadPoolBoundHandle _threadPoolBinding;
            internal PreAllocatedOverlapped _awaitableOverlapped;
            internal SafeFileHandle _fileHandle;

            internal AsyncCopyToAwaitable(SafeFileHandle fileHandle, ThreadPoolBoundHandle threadPoolBinding)
            {
                _fileHandle = fileHandle;
                _threadPoolBinding = threadPoolBinding;
            }

            internal unsafe void AssignOveralppedAndSetOffset()
            {
                if (_nativeOverlapped == null)
                {
                    // Allocate a native overlapped for our reusable overlapped, and set position to read based on the next
                    // desired address stored in the awaitable.  (This position may be 0, if either we're at the beginning or
                    // if the stream isn't seekable.)
                    _nativeOverlapped = _threadPoolBinding.AllocateNativeOverlapped(_awaitableOverlapped);
                }

                _nativeOverlapped->OffsetLow = unchecked((int)_position);
                _nativeOverlapped->OffsetHigh = (int)(_position >> 32);
            }

            /// <summary>Lock object used to protect cancellation-related access to _nativeOverlapped.</summary>
            internal object CancellationLock => this;

            /// <summary>Initialize the awaitable.</summary>
            internal unsafe AsyncCopyToAwaitable()
            {
                //_fileStream = fileStream;
            }

            /// <summary>Reset state to prepare for the next read operation.</summary>
            internal void ResetForNextOperation()
            {
                Debug.Assert(_position >= 0, $"Expected non-negative position, got {_position}");
                _continuation = null;
                _errorCode = 0;
                _numBytes = 0;
            }

            /// <summary>Overlapped callback: store the results, then invoke the continuation delegate.</summary>
            internal static unsafe void IOCallback(uint errorCode, uint numBytes, NativeOverlapped* pOVERLAP)
            {
                var awaitable = (AsyncCopyToAwaitable)ThreadPoolBoundHandle.GetNativeOverlappedState(pOVERLAP);

                Debug.Assert(!ReferenceEquals(awaitable._continuation, s_sentinel), "Sentinel must not have already been set as the continuation");
                awaitable._errorCode = (Win32ErrorCode)errorCode;
                awaitable._numBytes = numBytes;

                (awaitable._continuation ?? Interlocked.CompareExchange(ref awaitable._continuation, s_sentinel, null))?.Invoke();
            }

            /// <summary>
            /// Called when it's known that the I/O callback for an operation will not be invoked but we'll
            /// still be awaiting the awaitable.
            /// </summary>
            internal void MarkCompleted()
            {
                Debug.Assert(_continuation == null, "Expected null continuation");
                _continuation = s_sentinel;
            }

            public AsyncCopyToAwaitable GetAwaiter() => this;
            public bool IsCompleted => ReferenceEquals(_continuation, s_sentinel);
            public void GetResult() { }
            public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);
            public void UnsafeOnCompleted(Action continuation)
            {
                if (ReferenceEquals(_continuation, s_sentinel) ||
                    Interlocked.CompareExchange(ref _continuation, continuation, null) != null)
                {
                    Debug.Assert(ReferenceEquals(_continuation, s_sentinel), $"Expected continuation set to s_sentinel, got ${_continuation}");
                    Task.Run(continuation);
                }
            }
        }

        public class SendFile
        {
            private static readonly ArraySegment<byte> _endChunkBytes = CreateAsciiByteArraySegment("\r\n");
            private static ArraySegment<byte> CreateAsciiByteArraySegment(string text) => new ArraySegment<byte>(Encoding.ASCII.GetBytes(text));

            private HttpProtocol _httpProtocol;
            private Memory<byte> _memory;
            private PipeWriter _writer;

            private long _remaining;
            private int _chunkHeaderLength;

            public SendFile(HttpProtocol httpProtocol, PipeWriter writer)
            {
                _writer = writer;
                _httpProtocol = httpProtocol;
            }

            public async Task SendFileAsync(string path, long offset, long count, CancellationToken cancellationToken)
            {
                _remaining = count;
                //// For the first write, ensure headers are flushed if WriteDataAsync isn't called.
                var firstWrite = !_httpProtocol.HasResponseStarted;

                // TODO: Handle files larger than int.MaxValue; for content-length checking
                if (firstWrite)
                {
                    // Only verify a int.MaxValue at a time, rather than full long
                    var initializeTask = _httpProtocol.InitializeResponseAsync((int)Math.Min(_remaining, int.MaxValue));
                    if (!ReferenceEquals(initializeTask, Task.CompletedTask))
                    {
                        await initializeTask;
                    }
                }
                else
                {
                    // Only verify a int.MaxValue at a time, rather than full long
                    _httpProtocol.VerifyAndUpdateWrite((int)Math.Min(_remaining, int.MaxValue));
                }

                if (_httpProtocol._canHaveBody)
                {
                    if (_httpProtocol._autoChunk)
                    {
                        if (_remaining == 0)
                        {
                            await (!firstWrite ? Task.CompletedTask : _httpProtocol.FlushAsync(cancellationToken));
                        }
                    }
                    else
                    {
                        _httpProtocol.CheckLastWrite();
                    }
                }
                else
                {
                    _httpProtocol.HandleNonBodyResponseWrite();
                    await (!firstWrite ? Task.CompletedTask : _httpProtocol.FlushAsync(cancellationToken));
                }

                var fileHandle = Kernel32.CreateFile(path, FileAccess.Read, FileShare.Read, IntPtr.Zero, FileMode.Open, FileAttributes.Overlapped | FileAttributes.SequentialScan, IntPtr.Zero);
                if (fileHandle.IsInvalid)
                {
                    throw new IOException();
                }

                if (!Kernel32.SetFileCompletionNotificationModes(fileHandle, CompletionNotificationModes.SkipCompletionPortOnSuccess | CompletionNotificationModes.SkipSetEventOnHandle))
                {
                    throw new IOException();
                }

                var threadPoolBinding = ThreadPoolBoundHandle.BindHandle(fileHandle);
                var readAwaitable = new AsyncCopyToAwaitable(fileHandle, threadPoolBinding);
                // Allocate an Overlapped we can use repeatedly for all operations
                var awaitableOverlapped = new PreAllocatedOverlapped(AsyncCopyToAwaitable.s_callback, readAwaitable, null);
                readAwaitable._awaitableOverlapped = awaitableOverlapped;
                readAwaitable._position = offset;

                var cancellationReg = default(CancellationTokenRegistration);
                try
                {
                    // Register for cancellation.  We do this once for the whole copy operation, and just try to cancel
                    // whatever read operation may currently be in progress, if there is one.  It's possible the cancellation
                    // request could come in between operations, in which case we flag that with explicit calls to ThrowIfCancellationRequested
                    // in the read/write copy loop.
                    if (cancellationToken.CanBeCanceled)
                    {
                        cancellationReg = cancellationToken.Register(s =>
                        {
                            var innerAwaitable = (AsyncCopyToAwaitable)s;
                            unsafe
                            {
                                lock (innerAwaitable.CancellationLock) // synchronize with cleanup of the overlapped
                                {
                                    if (innerAwaitable._nativeOverlapped != null)
                                    {
                                        // Try to cancel the I/O.  We ignore the return value, as cancellation is opportunistic and we
                                        // don't want to fail the operation because we couldn't cancel it.
                                        Kernel32.CancelIoEx(innerAwaitable._fileHandle, innerAwaitable._nativeOverlapped);
                                    }
                                }
                            }
                        }, readAwaitable);
                    }

                    // Repeatedly read from this FileStream and write the results to the destination stream.
                    while (_remaining > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        readAwaitable.ResetForNextOperation();
                        int numBytesRead;
                        try
                        {
                            readAwaitable.AssignOveralppedAndSetOffset();
                            // Kick off the read.
                            if (!ReadFileNative(fileHandle, readAwaitable, out Win32ErrorCode errorCode) 
                                && errorCode == Win32ErrorCode.IOPending)
                            {
                                await readAwaitable;
                                errorCode = readAwaitable._errorCode;
                            }

                            switch (errorCode)
                            {
                                case Win32ErrorCode.Success:
                                case Win32ErrorCode.BrokenPipe:
                                case Win32ErrorCode.EndOfFile:
                                    break;
                                case Win32ErrorCode.ERROR_OPERATION_ABORTED: // canceled
                                    throw new OperationCanceledException(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true));
                                default:
                                    // Everything else is an error (and there won't be a callback).
                                    throw Marshal.GetExceptionForHR((int)errorCode);
                            }

                            // Successful operation.  If we got zero bytes, we're done: exit the read/write loop.
                            numBytesRead = (int)readAwaitable._numBytes;
                            if (numBytesRead == 0)
                            {
                                break;
                            }

                            // Otherwise, update the read position for next time accordingly.
                            readAwaitable._position += numBytesRead;
                            _remaining -= numBytesRead;
                        }
                        finally
                        {
                            // Free the resources for this read operation
                            unsafe
                            {
                                NativeOverlapped* overlapped;
                                lock (readAwaitable.CancellationLock) // just an Exchange, but we need this to be synchronized with cancellation, so using the same lock
                                {
                                    overlapped = readAwaitable._nativeOverlapped;
                                    readAwaitable._nativeOverlapped = null;
                                }
                                if (overlapped != null)
                                {
                                    threadPoolBinding.FreeNativeOverlapped(overlapped);
                                }
                            }
                        }

                        // Write out the read data.
                        if (_httpProtocol._autoChunk)
                        {
                            WriteChunkedFooter(numBytesRead);
                        }
                        else
                        {
                            _writer.Advance(numBytesRead);
                        }
                        var awaitable = _writer.FlushAsync(cancellationToken);
                        if (!awaitable.IsCompleted)
                        {
                            var flushResult = await awaitable;
                            if (!flushResult.IsCompleted)
                            {
                                new OperationCanceledException();
                            }
                        }
                    }
                }
                finally
                {
                    // Cleanup from the whole copy operation
                    cancellationReg.Dispose();
                    awaitableOverlapped.Dispose();
                }



                //var handle = ThreadPoolBoundHandle.BindHandle(fileHandle);


                //_fileHandle = fileHandle;
                //_threadPoolBoundHandle = handle;
                //_offset = offset;
                //_remaining = count;
                //_completion = new TaskCompletionSource<int>();
                //_cancellationToken = cancellationToken;

                //var overlapped = new PreAllocatedOverlapped((errorCode, numBytes, pOverlapped) => IOCallback(errorCode, (int)numBytes, pOverlapped), this, null);
                //_preAllocatedOverlapped = overlapped;
                //_overlapped = null;

                ////// For the first write, ensure headers are flushed if WriteDataAsync isn't called.
                //var firstWrite = !_httpProtocol.HasResponseStarted;

                //// TODO: Handle files larger than int.MaxValue; for content-length checking
                //if (firstWrite)
                //{
                //    // Only verify a int.MaxValue at a time, rather than full long
                //    var initializeTask = _httpProtocol.InitializeResponseAsync((int)Math.Min(_remaining, int.MaxValue));
                //    if (!ReferenceEquals(initializeTask, Task.CompletedTask))
                //    {
                //        return SendFileAsyncAwaited(initializeTask);
                //    }
                //}
                //else
                //{
                //    // Only verify a int.MaxValue at a time, rather than full long
                //    _httpProtocol.VerifyAndUpdateWrite((int)Math.Min(_remaining, int.MaxValue));
                //}

                //return SendFileAsync(firstWrite);
            }

            private unsafe bool ReadFileNative(SafeFileHandle handle, AsyncCopyToAwaitable readAwaitable, out Win32ErrorCode errorCode)
            {
                var bufferSize = (int)Math.Min(2048, _remaining);
                _memory = _writer.GetMemory(bufferSize);
                var bytes = _memory.Span;
                var count = (int)Math.Min(bytes.Length, _remaining);
                if (_httpProtocol._autoChunk)
                {
                    count = AvailableToWriteChunked(count, bytes.Length);
                    bytes = WriteChunkedHeader(bytes, count);
                }

                int r = Kernel32.ReadFile(handle, ref MemoryMarshal.GetReference(bytes), count, out uint numBytesRead, readAwaitable._nativeOverlapped);
                if (r == 0)
                {
                    errorCode = GetLastWin32ErrorAndDisposeHandleIfInvalid(handle);
                    return false;
                }
                else
                {
                    errorCode = 0;
                    readAwaitable._numBytes = numBytesRead;
                    return true;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private Win32ErrorCode GetLastWin32ErrorAndDisposeHandleIfInvalid(SafeFileHandle fileHandle, bool throwIfInvalidHandle = false)
            {
                Win32ErrorCode errorCode = (Win32ErrorCode)Marshal.GetLastWin32Error();

                // If ERROR_INVALID_HANDLE is returned, it doesn't suffice to set
                // the handle as invalid; the handle must also be closed.
                //
                // Marking the handle as invalid but not closing the handle
                // resulted in exceptions during finalization and locked column
                // values (due to invalid but unclosed handle) in SQL Win32FileStream
                // scenarios.
                //
                // A more mainstream scenario involves accessing a file on a
                // network share. ERROR_INVALID_HANDLE may occur because the network
                // connection was dropped and the server closed the handle. However,
                // the client side handle is still open and even valid for certain
                // operations.
                //
                // Note that _parent.Dispose doesn't throw so we don't need to special case.
                // SetHandleAsInvalid only sets _closed field to true (without
                // actually closing handle) so we don't need to call that as well.
                if (errorCode == Win32ErrorCode.ERROR_INVALID_HANDLE)
                {
                    DisposeInvalidHandle(fileHandle, throwIfInvalidHandle, errorCode);
                }

                return errorCode;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private void DisposeInvalidHandle(SafeFileHandle fileHandle, bool throwIfInvalid, Win32ErrorCode errorCode)
            {
                fileHandle.Dispose();

                if (throwIfInvalid)
                    throw Marshal.GetExceptionForHR((int)errorCode);
            }

            //private Task SendFileAsync(bool firstWrite)
            //{
            //    if (_httpProtocol._canHaveBody)
            //    {
            //        if (_httpProtocol._autoChunk)
            //        {
            //            if (_remaining == 0)
            //            {
            //                return !firstWrite ? Task.CompletedTask : _httpProtocol.FlushAsync(_cancellationToken);
            //            }
            //        }
            //        else
            //        {
            //            _httpProtocol.CheckLastWrite();
            //        }
            //    }
            //    else
            //    {
            //        _httpProtocol.HandleNonBodyResponseWrite();
            //        return !firstWrite ? Task.CompletedTask : _httpProtocol.FlushAsync(_cancellationToken);
            //    }

            //    _readTask = ReadAsync();
            //    return _completion.Task;
            //}

            //private async Task SendFileAsyncAwaited(Task initializeTask)
            //{
            //    await initializeTask;
            //    await SendFileAsync(firstWrite: true);
            //}

            //public unsafe static void IOCallback(uint errorCode, int numBytes, NativeOverlapped* pOverlapped)
            //{
            //    var state = ThreadPoolBoundHandle.GetNativeOverlappedState(pOverlapped);
            //    var operation = (SendFile)state;

            //    operation.IOCallback(errorCode, numBytes);
            //}

            //public unsafe void IOCallback(uint errorCode, int numBytes)
            //{
            //    try
            //    {
            //        _threadPoolBoundHandle.FreeNativeOverlapped(_overlapped);
            //        _overlapped = null;
            //        /*
            //        try
            //        {
            //            HttpProtocol.VerifyAndUpdateWrite((int)numBytes);
            //        }
            //        catch (Exception ex)
            //        {
            //            Completion.TrySetException(ex);
            //            return;
            //        }*/
            //        _offset += numBytes;
            //        _remaining -= numBytes;
            //        if (_httpProtocol._autoChunk)
            //        {
            //            WriteChunkedFooter(numBytes);
            //        }
            //        else
            //        {
            //            _writer.Advance(numBytes);
            //        }
            //        var awaitable = _writer.FlushAsync();
            //        if (numBytes == 0 || _remaining == 0)
            //        {
            //            _completion.TrySetResult(0);
            //            return;
            //        }

            //        if (awaitable.IsCompleted)
            //        {
            //            // No back pressure being applied so continue reading
            //            _readTask = ReadAsync();
            //        }
            //        else
            //        {
            //            _readTask = ReadAsyncAwaitFlush(awaitable);
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        _completion.TrySetException(ex);
            //    }
            //}

            //public async Task ReadAsync()
            //{
            //    try
            //    {
            //        unsafe
            //        {
            //            _overlapped = _threadPoolBoundHandle.AllocateNativeOverlapped(_preAllocatedOverlapped);
            //        }

            //        var completedInline = false;
            //        do
            //        {
            //            if (_cancellationToken.IsCancellationRequested)
            //            {
            //                _completion.TrySetCanceled();
            //                return;
            //            }

            //            var bufferSize = (int)Math.Min(2048, _remaining);
            //            _memory = _writer.GetMemory(bufferSize);

            //            if (Read(out int numberOfBytesRead) == 0)
            //            {
            //                completedInline = false;
            //            }
            //            else
            //            {
            //                completedInline = true;

            //                _offset += numberOfBytesRead;
            //                _remaining -= numberOfBytesRead;
            //                if (_httpProtocol._autoChunk)
            //                {
            //                    WriteChunkedFooter(numberOfBytesRead);
            //                }
            //                else
            //                {
            //                    _writer.Advance(numberOfBytesRead);
            //                }

            //                var awaitable = _writer.FlushAsync();
            //                if (numberOfBytesRead == 0 || _remaining == 0)
            //                {
            //                    _completion.TrySetResult(0);
            //                    return;
            //                }
            //                else if (!awaitable.IsCompleted)
            //                {
            //                    // Back pressure being applied
            //                    var flushResult = await awaitable;
            //                    if (!flushResult.IsCompleted)
            //                    {
            //                        _completion.TrySetException(new OperationCanceledException());
            //                        return;
            //                    }
            //                }
            //            }
            //        } while (completedInline);

            //        var errorCode = (Win32ErrorCode)Marshal.GetLastWin32Error();
            //        if (errorCode == Win32ErrorCode.IOPending)
            //        {
            //            // Completing async
            //            return;
            //        }

            //        if (errorCode == Win32ErrorCode.EndOfFile)
            //        {
            //            _completion.TrySetException(new EndOfStreamException());
            //        }
            //        else
            //        {
            //            _completion.TrySetException(Marshal.GetExceptionForHR((int)errorCode));
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        _completion.TrySetException(ex);
            //    }
            //}

            //private int Read(out int numberOfBytesRead)
            //{
            //    var span = _memory.Span;
            //    var count = (int)Math.Min(span.Length, _remaining);
            //    if (_httpProtocol._autoChunk)
            //    {
            //        count = AvailableToWriteChunked(count, span.Length);
            //        span = WriteChunkedHeader(span, count);
            //    }

            //    int result = 0;
            //    unsafe
            //    {
            //        var overlapped = _overlapped;
            //        overlapped->OffsetLow = unchecked((int)_offset);
            //        overlapped->OffsetHigh = (int)(_offset >> 32);

            //        result = Kernel32.ReadFile(_fileHandle, ref MemoryMarshal.GetReference(span), count, out numberOfBytesRead, overlapped);
            //    }

            //    return result;
            //}

            //private async Task ReadAsyncAwaitFlush(ValueTask<FlushResult> awaitable)
            //{
            //    // Keep reading once we get the completion
            //    var flushResult = await awaitable;
            //    if (!flushResult.IsCompleted)
            //    {
            //        _readTask = ReadAsync();
            //    }
            //    else
            //    {
            //        _completion.TrySetException(new OperationCanceledException());
            //    }
            //}

            private Span<byte> WriteChunkedHeader(Span<byte> span, int count)
            {
                var chunkSegment = ChunkWriter.BeginChunkBytes(count);
                _chunkHeaderLength = chunkSegment.Count;
                chunkSegment.AsSpan().CopyTo(span);
                _memory = _memory.Slice(chunkSegment.Count);
                return span.Slice(chunkSegment.Count);
            }

            private void WriteChunkedFooter(int numberOfBytesRead)
            {
                var span = _memory.Span;
                span = span.Slice(numberOfBytesRead);
                _endChunkBytes.AsSpan().CopyTo(span);
                _writer.Advance(numberOfBytesRead + _endChunkBytes.Count + _chunkHeaderLength);
            }
        }

        private static int AvailableToWriteChunked(int totalRemaining, int totalAvailable)
        {
            if (totalAvailable - totalRemaining > 12) return totalRemaining;
            // Bit lazy on the boundaries
            var remaining = totalRemaining - 4; // remove the chunking delimeters
            if (remaining > 0xfff_ffff)
            {
                remaining -= 8;
            }
            else if (remaining > 0xff_ffff)
            {
                remaining -= 7;
            }
            else if (remaining > 0xf_ffff)
            {
                remaining -= 6;
            }
            else if (remaining > 0xffff)
            {
                remaining -= 5;
            }
            else if (remaining > 0xfff)
            {
                remaining -= 4;
            }
            else if (remaining > 0xff)
            {
                remaining -= 3;
            }
            else if (remaining > 0xf)
            {
                remaining -= 2;
            }
            else
            {
                remaining -= 1;
            }

            return remaining;
        }

        internal static class Kernel32
        {
            const string Kernel32_Dll = "kernel32.dll";

            [DllImport(Kernel32_Dll, SetLastError = true)]
            public static extern unsafe bool CancelIoEx(SafeHandle handle, NativeOverlapped* lpOverlapped);

            [DllImport(Kernel32_Dll, SetLastError = true)]
            public static extern unsafe bool SetFileCompletionNotificationModes(
                    SafeFileHandle file,
                    CompletionNotificationModes fileAccess
                );

            [DllImport(Kernel32_Dll, SetLastError = true)]
            public static extern unsafe int ReadFile(
                    SafeFileHandle file,
                    ref byte destination,
                    int bytesToRead,
                    out uint bytesRead,
                    NativeOverlapped* overlapped
                );

            [DllImport(Kernel32_Dll, SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern SafeFileHandle CreateFile(
                string fileName,
                FileAccess fileAccess,
                FileShare fileShare,
                IntPtr securityAttributes,
                FileMode creationDisposition,
                FileAttributes flags,
                IntPtr template
            );
        }

        internal enum CompletionNotificationModes : byte
        {
            None = 0,
            SkipCompletionPortOnSuccess = 1,
            SkipSetEventOnHandle = 2
        }

        [Flags]
        internal enum FileAttributes : uint
        {
            Overlapped = 0x40000000,
            NoBuffering = 0x20000000,
            SequentialScan = 0x08000000
        }

        internal enum Win32ErrorCode : int
        {
            Success = 0,
            ERROR_INVALID_HANDLE = 0x6,
            EndOfFile = 0x26,
            BrokenPipe = 0x6D,
            ERROR_OPERATION_ABORTED = 0x3E3,
            IOPending = 0x3E5
        }
    }

    internal static class TaskExtensions
    {
        public static bool IsCompletedSuccessfully(this Task task)
        {
#if NETCOREAPP2_1
            return task.IsCompletedSuccessfully;
#else
            return (task.Status & TaskStatus.RanToCompletion) != 0;
#endif
        }
    }
}