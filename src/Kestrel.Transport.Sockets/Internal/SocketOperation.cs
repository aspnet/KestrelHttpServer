// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class SocketOperation
    {
        protected readonly Socket _socket;

        private readonly ThreadPoolBoundHandle _threadPoolBoundHandle;
        private readonly SocketConnection _socketConnection;

        private readonly PreAllocatedOverlapped _overlapped;
        private readonly SocketAwaitable _awaitable;

        private readonly IOCompletionCallback _completionCallback;

        internal SocketOperation(Socket socket, PipeScheduler scheduler, SocketConnection socketConnection, ThreadPoolBoundHandle threadPoolBoundHandle, IOCompletionCallback completionCallback)
        {
            _socket = socket;
            _socketConnection = socketConnection;
            _threadPoolBoundHandle = threadPoolBoundHandle;
            _completionCallback = completionCallback;

            _awaitable = new SocketAwaitable(scheduler);
            _overlapped = new PreAllocatedOverlapped(_completionCallback, state: this, pinData: null);
        }

        protected unsafe NativeOverlapped* GetOverlapped()
        {
            return _threadPoolBoundHandle.AllocateNativeOverlapped(_overlapped);
        }

        protected unsafe SocketAwaitable GetAwaitable(NativeOverlapped* overlapped, SocketError errno, int bytesTransferred, out bool completedInline)
        {
            completedInline = false;

            if (errno != SocketError.Success)
            {
                errno = (SocketError)Marshal.GetLastWin32Error();

                if (errno != SocketError.IOPending && errno != SocketError.Success)
                {
                    _threadPoolBoundHandle.FreeNativeOverlapped(overlapped);
                    _awaitable.Complete(bytesTransferred, errno);
                    completedInline = true;
                }
            }

            return _awaitable;
        }

        protected unsafe void OperationCompletionCallback(uint errno, uint bytesTransferred, NativeOverlapped* overlapped)
        {
            var socketError = GetSocketError(overlapped, errno);

            _threadPoolBoundHandle.FreeNativeOverlapped(overlapped);
            _awaitable.Complete((int)bytesTransferred, socketError);
        }

        // https://github.com/dotnet/corefx/blob/a26a684033c0bfcfbde8e55c51b8b03fb7a3bafc/src/System.Net.Sockets/src/System/Net/Sockets/BaseOverlappedAsyncResult.Windows.cs#L61
        private unsafe SocketError GetSocketError(NativeOverlapped* overlapped, uint errno)
        {
            // Complete the IO and invoke the user's callback.
            var socketError = (SocketError)errno;

            if (socketError != SocketError.Success && socketError != SocketError.OperationAborted)
            {
                // There are cases where passed errorCode does not reflect the details of the underlined socket error.
                // "So as of today, the key is the difference between WSAECONNRESET and ConnectionAborted,
                //  .e.g remote party or network causing the connection reset or something on the local host (e.g. closesocket
                // or receiving data after shutdown (SD_RECV)).  With Winsock/TCP stack rewrite in longhorn, there may
                // be other differences as well."
                if (_socketConnection.Aborted)
                {
                    socketError = SocketError.OperationAborted;
                }
                else
                {
                    // The async IO completed with a failure.
                    // Here we need to call WSAGetOverlappedResult() just so GetLastSocketError() will return the correct error.
                    bool success = WSAGetOverlappedResult(
                        _socket.Handle,
                        overlapped,
                        out _,
                        false,
                        out _);

                    if (!success)
                    {
                        socketError = (SocketError)Marshal.GetLastWin32Error();
                    }
                    else
                    {
                        Trace.Assert(false, $"Unexpectedly succeeded. errorCode: '{errno}'");
                    }
                }
            }

            return socketError;
        }

        [DllImport("ws2_32", SetLastError = true)]
        protected static unsafe extern SocketError WSARecv(
            IntPtr socketHandle,
            WSABuffer* buffers,
            int bufferCount,
            out int bytesTransferred,
            ref SocketFlags socketFlags,
            NativeOverlapped* overlapped,
            IntPtr completionRoutine);

        [DllImport("ws2_32", SetLastError = true)]
        protected static extern unsafe SocketError WSASend(
            IntPtr socketHandle,
            WSABuffer* buffers,
            int bufferCount,
            out int bytesTransferred,
            SocketFlags socketFlags,
            NativeOverlapped* overlapped,
            IntPtr completionRoutine);

        [DllImport("ws2_32", SetLastError = true)]
        protected static unsafe extern bool WSAGetOverlappedResult(
            IntPtr socketHandle,
            NativeOverlapped* overlapped,
            out uint bytesTransferred,
            bool wait,
            out SocketFlags socketFlags);

        [StructLayout(LayoutKind.Sequential)]
        protected struct WSABuffer
        {
            public int Length;
            public IntPtr Pointer;
        }
    }
}
