// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class SocketReceiver : SocketOperation
    {
        private MemoryHandle _memoryHandle;

        internal SocketReceiver(Socket socket, PipeScheduler scheduler, SocketConnection socketConnection)
            : base(socket, scheduler, socketConnection, CompletionCallback)
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

        private static void CompletionCallback(uint errno, uint bytesTransferred, IntPtr overlapped, object state)
        {
            var socketReceiver = (SocketReceiver)state;
            socketReceiver._memoryHandle.Dispose();
            socketReceiver.OperationCompletionCallback(errno, bytesTransferred, overlapped);
        }
    }
}
