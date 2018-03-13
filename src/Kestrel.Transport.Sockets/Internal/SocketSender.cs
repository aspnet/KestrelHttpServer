// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class SocketSender : SocketOperation
    {
        private readonly PipeScheduler _scheduler;
        private readonly SocketConnection _socketConnection;

        private MemoryHandle _memoryHandle;
        private MultiSegmentSocketSender _multiSegmentSocketSender;


        internal SocketSender(Socket socket, PipeScheduler scheduler, SocketConnection socketConnection)
            : base(socket, scheduler, socketConnection, CompletionCallback)
        {
            _scheduler = scheduler;
            _socketConnection = socketConnection;
        }

        public unsafe SocketAwaitable SendAsync(ReadOnlySequence<byte> buffers)
        {
            if (!buffers.IsSingleSegment)
            {
                if (_multiSegmentSocketSender == null)
                {
                    _multiSegmentSocketSender = new MultiSegmentSocketSender(_socket, _scheduler, _socketConnection);
                }

                return _multiSegmentSocketSender.SendAsync(buffers);
            }

            var overlapped = GetOverlapped();

            _memoryHandle = buffers.First.Retain(pin: true);
            
            var wsaBuffer = new WSABuffer
            {
                Length = buffers.First.Length,
                Pointer = (IntPtr)_memoryHandle.Pointer
            };

            var errno = WSASend(
                _socket.Handle,
                &wsaBuffer,
                1,
                out var bytesTransferred,
                SocketFlags.None,
                overlapped,
                IntPtr.Zero);

            var awaitable = GetAwaitable(overlapped, errno, bytesTransferred, out var completedInline);

            if (completedInline)
            {
                _memoryHandle.Dispose();
            }

            return awaitable;
        }

        public override void Dispose()
        {
            _multiSegmentSocketSender?.Dispose();
            base.Dispose();
        }

        private static void CompletionCallback(uint errno, uint bytesTransferred, IntPtr overlapped, object state)
        {
            var socketSender = (SocketSender)state;
            socketSender._memoryHandle.Dispose();
            socketSender.OperationCompletionCallback(errno, bytesTransferred, overlapped);
        }
    }
}
