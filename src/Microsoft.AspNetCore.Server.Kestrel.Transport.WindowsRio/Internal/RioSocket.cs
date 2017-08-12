// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    public class RioSocket
    {
        private readonly static WaitCallback _dataCallback = (instance, context, wait, waitResult) =>
        {
            ((RioSocket)GCHandle.FromIntPtr(context).Target).OnData();
        };

        private readonly BufferMapper _bufferMapper;

        private RioConnectedSocket _socket;

        private Event _dataEvent;

        private RioCompletionQueue _dataQueue;

        private RioRequestQueue _requestQueue;

        private GCHandle _handle;

        private WinThreadpoolWait _dataWait;

        private AutoResetGate<ReceiveResult> _receiveGate = new AutoResetGate<ReceiveResult>();
        private AutoResetGate<SocketState> _readyToSend = new AutoResetGate<SocketState>();
        private RioRequestResults _rioResults;

        private const int _maxOutstandingSends = 20;
        private const int _maxOutstandingReceives = 1;
        private int _outstandingSends;

        public IPEndPoint RemoteEndPoint { get; }
        public IPEndPoint LocalEndPoint { get; }

        public RioSocket(RioConnectedSocket socket, BufferMapper bufferMapper)
        {
            _socket = socket;
            _bufferMapper = bufferMapper;

            RemoteEndPoint = RioFunctions.GetPeerIPEndPoint(_socket);
            LocalEndPoint = RioFunctions.GetSockIPEndPoint(_socket);

            _dataEvent = Event.Create();
            _dataQueue = _dataEvent.CreateCompletionQueue(_maxOutstandingSends + _maxOutstandingReceives);

            _requestQueue = RioFunctions.CreateRequestQueue(socket, _dataQueue, _dataQueue, _maxOutstandingSends);


            _handle = GCHandle.Alloc(this, GCHandleType.Normal);
            var address = GCHandle.ToIntPtr(_handle);

            _dataWait = WinThreadpool.CreateThreadpoolWait(_dataCallback, address);

            Post();
        }

        public bool NoDelay
        {
            set => RioFunctions.SetTcpNodelay(_socket, value);
        }

        public void Shutdown(SocketShutdown how)
        {
            RioFunctions.Shutdown(_socket, how);
        }

        public void Dispose()
        {
            _handle.Free();
            WinThreadpool.CloseThreadpoolWait(_dataWait);
            _dataQueue.Dispose();
            _dataEvent.Dispose();
            _socket.Dispose();
        }

        public ReadCursor SendPartial(ReadableBuffer buffer)
        {
            var totalBytes = 0;

            var enumerator = buffer.GetEnumerator();
            if (enumerator.MoveNext())
            {
                var current = enumerator.Current;
                while (enumerator.MoveNext())
                {
                    var next = enumerator.Current;
                    var segment = _bufferMapper.GetSegmentFromBuffer(current);
                    totalBytes += current.Length;

                    current = next;

                    _outstandingSends++;
                    _requestQueue.SendCommit(ref segment);
                }
            }

            return buffer.Move(buffer.Start, totalBytes);
        }

        public void SendComplete(ReadableBuffer buffer)
        {
            if (buffer.IsSingleSpan)
            {
                var segment = _bufferMapper.GetSegmentFromBuffer(buffer.First);
                _outstandingSends++;
                _requestQueue.SendCommit(ref segment);
            }
            else
            {
                SendCompleteMulti(buffer);
            }
        }

        private void SendCompleteMulti(ReadableBuffer buffer)
        {
            var enumerator = buffer.GetEnumerator();
            if (enumerator.MoveNext())
            {
                var current = enumerator.Current;

                RioBufferSegment segment;
                while (enumerator.MoveNext())
                {
                    var next = enumerator.Current;

                    segment = _bufferMapper.GetSegmentFromBuffer(current);
                    current = next;

                    _outstandingSends++;
                    _requestQueue.QueueSend(ref segment);
                }

                segment = _bufferMapper.GetSegmentFromBuffer(current);
                _outstandingSends++;
                _requestQueue.SendCommit(ref segment);
            }
        }

        public AutoResetGate<SocketState> ReadyToSend()
        {
            return _readyToSend;
        }

        public AutoResetGate<ReceiveResult> ReceiveAsync(Buffer<byte> buffer)
        {
            var receiveBufferSeg = _bufferMapper.GetSegmentFromBuffer(buffer);
            _requestQueue.Receive(ref receiveBufferSeg);

            return _receiveGate;
        }

        private void OnData()
        {
            var isEnd = false;

            ref var results = ref _rioResults;
            while (true)
            {
                var count = _dataQueue.Dequeue(ref results);
                if (count == 0)
                {
                    break;
                }

                var sendCount = 0;
                for (var i = 0; i < count; i++)
                {
                    ref var result = ref results[i];

                    // TODO: result.Status

                    if (result.RequestCorrelation > 0)
                    {
                        // Receive
                        var byteReceived = (int) result.BytesTransferred;
                        if (byteReceived == 0)
                        {
                            isEnd = true;
                        }
                        _receiveGate.Open(new ReceiveResult
                        {
                            BytesReceived = byteReceived,
                            IsEnd = isEnd
                        });
                    }
                    else
                    {
                        sendCount++;
                    }
                }

                if (sendCount > 0)
                {
                    _readyToSend.Open(new SocketState { InputConsumed = false });
                }
            }

            _readyToSend.Open(new SocketState { InputConsumed = true });

            if (!isEnd)
            {
                _dataQueue.Notify();
                WinThreadpool.SetThreadpoolWait(_dataWait, _dataEvent);
            }
        }

        private void Post()
        {
            _dataQueue.Notify();
            WinThreadpool.SetThreadpoolWait(_dataWait, _dataEvent);
        }
    }

    public struct SocketState
    {
        public bool InputConsumed;
    }

    public struct ReceiveResult
    {
        public int BytesReceived;
        public bool IsEnd;
    }
}
