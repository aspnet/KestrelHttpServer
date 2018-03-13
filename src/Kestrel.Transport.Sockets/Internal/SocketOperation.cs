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

        private readonly SocketConnection _socketConnection;

        private readonly SocketAwaitable _awaitable;

        private readonly Action<uint, uint, IntPtr, object> _completionCallback;
        private bool _skipCompletionPortOnSuccess;

        internal SocketOperation(Socket socket, PipeScheduler scheduler, SocketConnection socketConnection, Action<uint, uint, IntPtr, object> completionCallback)
        {
            _socket = socket;
            _socketConnection = socketConnection;
            _completionCallback = completionCallback;

            _awaitable = new SocketAwaitable(scheduler);

            try
            {
                _skipCompletionPortOnSuccess = SetFileCompletionNotificationModes(_socket.Handle,
                    FileCompletionNotificationModes.SkipCompletionPortOnSuccess | FileCompletionNotificationModes.SkipSetEventOnHandle);
            } catch { }
        }

        protected unsafe NativeOverlapped* GetOverlapped()
        {
            var overlapped = new Overlapped
            {
                AsyncResult = new CallbackAsyncResult
                {
                    Callback = (errno, nativeOverlappped, state) => ((SocketOperation)state)._completionCallback(0, errno, nativeOverlappped, state),
                    AsyncState = this
                }
            };

            return overlapped.UnsafePack(null, null);
        }

        protected unsafe SocketAwaitable GetAwaitable(NativeOverlapped* overlapped, SocketError errno, int bytesTransferred, out bool completedInline)
        {
            errno = errno == SocketError.Success ? SocketError.Success : (SocketError)Marshal.GetLastWin32Error();

            if (errno != SocketError.IOPending && (_skipCompletionPortOnSuccess || errno != SocketError.Success))
            {
                Overlapped.Free(overlapped);
                _awaitable.Complete(bytesTransferred, errno);
                completedInline = true;
            }
            else
            {
                completedInline = false;
            }

            return _awaitable;
        }

        protected unsafe void OperationCompletionCallback(uint errno, uint bytesTransferred, IntPtr overlapped)
        {
            var overlappedPtr = (NativeOverlapped*)overlapped;
            var socketError = GetSocketError(overlappedPtr, errno);

            Overlapped.Free(overlappedPtr);
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

        [DllImport("kernel32.dll")]
        private static unsafe extern bool SetFileCompletionNotificationModes(
            IntPtr handle,
            FileCompletionNotificationModes flags);

        [StructLayout(LayoutKind.Sequential)]
        protected struct WSABuffer
        {
            public int Length;
            public IntPtr Pointer;
        }

        [Flags]
        internal enum FileCompletionNotificationModes : byte
        {
            None = 0,
            SkipCompletionPortOnSuccess = 1,
            SkipSetEventOnHandle = 2
        }
    }
}
