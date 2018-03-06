// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class SocketReceiver : SocketOperation
    {
        private static unsafe readonly IOCompletionCallback _completionCallback = new IOCompletionCallback(CompletionCallback);
        private MemoryHandle _memoryHandle;

        internal SocketReceiver(Socket socket, PipeScheduler scheduler, SocketConnection socketConnection, ThreadPoolBoundHandle threadPoolBoundHandle)
            : base(socket, scheduler, socketConnection, threadPoolBoundHandle, _completionCallback)
        {
        }

        public unsafe SocketAwaitable ReceiveAsync(Memory<byte> buffer)
        {
            _memoryHandle = buffer.Retain(pin: true);

            var overlapped = GetOverlapped();

            var wsaBuffer = new WSABuffer
            {
                Length = buffer.Length,
                Pointer = (IntPtr)_memoryHandle.Pointer
            };

            var socketFlags = SocketFlags.None;
            var errno = WSARecv(
                _socket.Handle,
                &wsaBuffer,
                1,
                out var bytesTransferred,
                ref socketFlags,
                overlapped,
                IntPtr.Zero);


            var awaitable = GetAwaitable(overlapped, errno, bytesTransferred, out var completedInline);

            if (completedInline)
            {
                _memoryHandle.Dispose();
            }

            return awaitable;
        }

        private static unsafe void CompletionCallback(uint errno, uint bytesTransferred, NativeOverlapped* overlapped)
        {
            var socketReceiver = (SocketReceiver)ThreadPoolBoundHandle.GetNativeOverlappedState(overlapped);
            socketReceiver._memoryHandle.Dispose();
            socketReceiver.OperationCompletionCallback(errno, bytesTransferred, overlapped);
        }
    }
}
