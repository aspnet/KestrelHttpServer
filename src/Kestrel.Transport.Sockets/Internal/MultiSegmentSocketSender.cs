// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class MultiSegmentSocketSender : SocketOperation
    {
        private static unsafe readonly IOCompletionCallback _completionCallback = new IOCompletionCallback(CompletionCallback);

        private readonly List<MemoryHandle> _memoryHandleList = new List<MemoryHandle>();

        internal MultiSegmentSocketSender(Socket socket, PipeScheduler scheduler, SocketConnection socketConnection, ThreadPoolBoundHandle threadPoolBoundHandle)
            : base(socket, scheduler, socketConnection, threadPoolBoundHandle, _completionCallback)
        {
        }

        public unsafe SocketAwaitable SendAsync(ReadOnlySequence<byte> buffers)
        {
            var bufferCount = (int)buffers.Length;
            var wsaBuffers = stackalloc WSABuffer[bufferCount];

            var i = 0;
            foreach (var buffer in buffers)
            {
                var memoryHandle = buffer.Retain(pin: true);
                _memoryHandleList.Add(memoryHandle);

                wsaBuffers[i].Length = buffer.Length;
                wsaBuffers[i].Pointer = (IntPtr)memoryHandle.Pointer;

                i++;
            }

            var overlapped = GetOverlapped();

            var errno = WSASend(
                _socket.Handle,
                wsaBuffers,
                bufferCount,
                out var bytesTransferred,
                SocketFlags.None,
                overlapped,
                IntPtr.Zero);

            var awaitable = GetAwaitable(overlapped, errno, bytesTransferred, out var completedInline);

            if (completedInline)
            {
                DisposeHandles();
            }

            return awaitable;
        }

        private void DisposeHandles()
        {
            foreach (var handle in _memoryHandleList)
            {
                handle.Dispose();
            }

            _memoryHandleList.Clear();
        }

        private static unsafe void CompletionCallback(uint errno, uint bytesTransferred, NativeOverlapped* overlapped)
        {
            var socketSender = (MultiSegmentSocketSender)ThreadPoolBoundHandle.GetNativeOverlappedState(overlapped);
            socketSender.DisposeHandles();
            socketSender.OperationCompletionCallback(errno, bytesTransferred, overlapped);
        }
    }
}
