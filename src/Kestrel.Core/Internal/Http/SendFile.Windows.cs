// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public partial class HttpProtocol
    {
        public class SendFile : IDisposable
        {
            private static readonly ArraySegment<byte> _endChunkBytes = CreateAsciiByteArraySegment("\r\n");
            private static ArraySegment<byte> CreateAsciiByteArraySegment(string text) => new ArraySegment<byte>(Encoding.ASCII.GetBytes(text));

            private HttpProtocol _httpProtocol;
            private TaskCompletionSource<Task> _completion;
            private Memory<byte> _memory;
            private PipeWriter _writer;

            private SafeFileHandle _fileHandle;
            private PreAllocatedOverlapped _preAllocatedOverlapped;
            private ThreadPoolBoundHandle _threadPoolBoundHandle;
            private unsafe NativeOverlapped* _overlapped;
            private CancellationToken _cancellationToken;

            private bool _isDisposed;

            private long _offset;
            private long _remaining;
            private int _chunkHeaderLength;
            private Task _readTask;

            public SendFile(HttpProtocol httpProtocol, PipeWriter writer)
            {
                _writer = writer;
                _httpProtocol = httpProtocol;
            }

            public unsafe Task SendFileAsync(string path, long offset, long count, CancellationToken cancellationToken)
            {
                var fileHandle = CreateFile(path, FileAccess.Read, FileShare.Read, IntPtr.Zero, FileMode.Open, FileAttributes.Overlapped | FileAttributes.SequentialScan, IntPtr.Zero);
                if (fileHandle.IsInvalid)
                {
                    throw new IOException();
                }

                if (!SetFileCompletionNotificationModes(fileHandle, CompletionNotificationModes.SkipCompletionPortOnSuccess | CompletionNotificationModes.SkipSetEventOnHandle))
                {
                    throw new IOException();
                }

                var handle = ThreadPoolBoundHandle.BindHandle(fileHandle);

                _fileHandle = fileHandle;
                _threadPoolBoundHandle = handle;
                _offset = offset;
                _remaining = count;
                _completion = new TaskCompletionSource<Task>();
                _cancellationToken = cancellationToken;

                var overlapped = new PreAllocatedOverlapped((errorCode, numBytes, pOverlapped) => IOCallback(errorCode, (int)numBytes, pOverlapped), this, null);
                _preAllocatedOverlapped = overlapped;

                //// For the first write, ensure headers are flushed if WriteDataAsync isn't called.
                var firstWrite = !_httpProtocol.HasResponseStarted;

                // TODO: Handle files larger than int.MaxValue; for content-length checking
                if (firstWrite)
                {
                    // Only verify a int.MaxValue at a time, rather than full long
                    var initializeTask = _httpProtocol.InitializeResponseAsync((int)Math.Min(_remaining, int.MaxValue));
                    if (!ReferenceEquals(initializeTask, Task.CompletedTask))
                    {
                        return SendFileAsyncAwaited(initializeTask);
                    }
                }
                else
                {
                    // Only verify a int.MaxValue at a time, rather than full long
                    _httpProtocol.VerifyAndUpdateWrite((int)Math.Min(_remaining, int.MaxValue));
                }

                return SendFileAsync(firstWrite);
            }

            private Task SendFileAsync(bool firstWrite)
            {
                if (_httpProtocol._canHaveBody)
                {
                    if (_httpProtocol._autoChunk)
                    {
                        if (_remaining == 0)
                        {
                            return !firstWrite ? Task.CompletedTask : _httpProtocol.FlushAsync(_cancellationToken);
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
                    return !firstWrite ? Task.CompletedTask : _httpProtocol.FlushAsync(_cancellationToken);
                }

                _readTask = ReadAsync(Task.CompletedTask);
                return _completion.Task.Unwrap();
            }

            private async Task SendFileAsyncAwaited(Task initializeTask)
            {
                await initializeTask;
                await SendFileAsync(firstWrite: true);
            }

            private async Task ChainTasks(Task previousTask, Task nextTask)
            {
                await previousTask;
                await nextTask;
            }

            public unsafe static void IOCallback(uint errorCode, int numBytes, NativeOverlapped* pOverlapped)
            {
                var state = ThreadPoolBoundHandle.GetNativeOverlappedState(pOverlapped);
                var operation = (SendFile)state;

                operation.IOCallback(errorCode, numBytes);
            }

            public unsafe void IOCallback(uint errorCode, int numBytes)
            {
                try
                {
                    _threadPoolBoundHandle.FreeNativeOverlapped(_overlapped);
                    /*
                    try
                    {
                        HttpProtocol.VerifyAndUpdateWrite((int)numBytes);
                    }
                    catch (Exception ex)
                    {
                        Completion.TrySetException(ex);
                        return;
                    }*/
                    _offset += numBytes;
                    _remaining -= numBytes;
                    if (_httpProtocol._autoChunk)
                    {
                        WriteChunkedFooter(numBytes);
                    }
                    else
                    {
                        _writer.Advance(numBytes);
                    }
                    var awaitable = _writer.FlushAsync();

                    var previousTask = _readTask;
                    if (numBytes == 0 || _remaining == 0)
                    {
                        _completion.TrySetResult(ChainTasks(previousTask, awaitable.AsTask()));
                        return;
                    }

                    if (awaitable.IsCompleted)
                    {
                        // No back pressure being applied so continue reading
                        _readTask = ReadAsync(previousTask);
                    }
                    else
                    {
                        _readTask = ReadAsyncAwaitFlush(previousTask, awaitable);
                    }
                }
                catch (Exception ex)
                {
                    _completion.TrySetException(ex);
                }
            }

            public async Task ReadAsync(Task previousTask)
            {
                if (!ReferenceEquals(previousTask, Task.CompletedTask) && 
                    !previousTask.IsCompletedSuccessfully())
                {
                    await previousTask;
                }

                var completedInline = false;
                do
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        _completion.TrySetCanceled();
                        return;
                    }

                    var bufferSize = (int)Math.Min(2048, _remaining);
                    _memory = _writer.GetMemory(bufferSize);

                    if (Read(out int numberOfBytesRead) == 0)
                    {
                        completedInline = false;
                    }
                    else
                    {
                        completedInline = true;
                        _offset += numberOfBytesRead;
                        _remaining -= numberOfBytesRead;
                        if (_httpProtocol._autoChunk)
                        {
                            WriteChunkedFooter(numberOfBytesRead);
                        }
                        else
                        {
                            _writer.Advance(numberOfBytesRead);
                        }

                        var awaitable = _writer.FlushAsync();
                        if (numberOfBytesRead == 0 || _remaining == 0)
                        {
                            _completion.TrySetResult(awaitable.AsTask());
                            return;
                        }
                        else if (!awaitable.IsCompleted)
                        {
                            // Back pressure being applied
                            var flushResult = await awaitable;
                            if (!flushResult.IsCompleted)
                            {
                                _completion.TrySetException(new OperationCanceledException());
                                return;
                            }
                        }
                    }
                } while (completedInline);

                var errorCode = (Win32ErrorCode)Marshal.GetLastWin32Error();
                if (errorCode == Win32ErrorCode.IOPending)
                {
                    // Completing async
                    return;
                }

                if (errorCode == Win32ErrorCode.EndOfFile)
                {
                    _completion.TrySetException(new EndOfStreamException());
                }
                else
                {
                    _completion.TrySetException(Marshal.GetExceptionForHR((int)errorCode));
                }
            }

            private int Read(out int numberOfBytesRead)
            {
                var span = _memory.Span;
                var count = (int)Math.Min(span.Length, _remaining);
                if (_httpProtocol._autoChunk)
                {
                    count = AvailableToWriteChunked(count, span.Length);
                    span = WriteChunkedHeader(span, count);
                }

                int result = 0;
                unsafe
                {
                    var overlapped = _threadPoolBoundHandle.AllocateNativeOverlapped(_preAllocatedOverlapped);
                    overlapped->OffsetLow = unchecked((int)_offset);
                    overlapped->OffsetHigh = (int)(_offset >> 32);
                    _overlapped = overlapped;

                    result = ReadFile(_fileHandle, ref MemoryMarshal.GetReference(span), count, out numberOfBytesRead, overlapped);
                }

                return result;
            }

            private async Task ReadAsyncAwaitFlush(Task previousTask, ValueTask<FlushResult> awaitable)
            {
                // Keep reading once we get the completion
                await previousTask;
                var flushResult = await awaitable;
                if (!flushResult.IsCompleted)
                {
                    _readTask = ReadAsync(Task.CompletedTask);
                }
                else
                {
                    _completion.TrySetException(new OperationCanceledException());
                }
            }

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

            public void Dispose()
            {
                if (!_isDisposed)
                {
                    if (_fileHandle != null && !_fileHandle.IsInvalid)
                    {
                        _fileHandle.Dispose();
                    }
                    _threadPoolBoundHandle?.Dispose();
                    _preAllocatedOverlapped?.Dispose();
                    _isDisposed = true;
                }
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

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern unsafe bool SetFileCompletionNotificationModes(
                SafeFileHandle file,
                CompletionNotificationModes fileAccess
            );

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern unsafe int ReadFile(
                SafeFileHandle file,
                ref byte destination,
                int bytesToRead,
                out int bytesRead,
                NativeOverlapped* overlapped
            );

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(
            string fileName,
            [MarshalAs(UnmanagedType.U4)] FileAccess fileAccess,
            [MarshalAs(UnmanagedType.U4)] FileShare fileShare,
            IntPtr securityAttributes,
            [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
            [MarshalAs(UnmanagedType.U4)] FileAttributes flags,
            IntPtr template
        );

        private enum CompletionNotificationModes : byte
        {
            None = 0,
            SkipCompletionPortOnSuccess = 1,
            SkipSetEventOnHandle = 2
        }

        [Flags]
        private enum FileAttributes : uint
        {
            Overlapped = 0x40000000,
            NoBuffering = 0x20000000,
            SequentialScan = 0x08000000
        }

        private enum Win32ErrorCode : int
        {
            EndOfFile = 0x26,
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