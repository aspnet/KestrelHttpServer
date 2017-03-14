// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Networking
{
    /// <summary>
    /// Summary description for UvWriteRequest
    /// </summary>
    public class UvWriteReq : UvRequest
    {
        private static readonly Libuv.uv_write_cb _uv_write_cb = (IntPtr ptr, int status) => UvWriteCb(ptr, status);

        private IntPtr _bufs;

        private const int BUFFER_COUNT = 4;

        private List<GCHandle> _pins = new List<GCHandle>(BUFFER_COUNT + 1);

        public UvWriteReq(IKestrelTrace logger) : base(logger)
        {
        }

        public void Init(UvLoopHandle loop)
        {
            var requestSize = loop.Libuv.req_size(Libuv.RequestType.WRITE);
            var bufferSize = Marshal.SizeOf<Libuv.uv_buf_t>() * BUFFER_COUNT;
            CreateMemory(
                loop.Libuv,
                loop.ThreadId,
                requestSize + bufferSize);
            _bufs = handle + requestSize;
            _state = _awaitableIsNotCompleted;
        }

        public unsafe UvWriteReqAwaiter Write(
            UvStreamHandle handle,
            ReadableBuffer buffer)
        {
            try
            {
                // add GCHandle to keeps this SafeHandle alive while request processing
                _pins.Add(GCHandle.Alloc(this, GCHandleType.Normal));

                var nBuffers = 0;
                foreach (var _ in buffer)
                {
                    nBuffers++;
                }

                var pBuffers = (Libuv.uv_buf_t*)_bufs;
                if (nBuffers > BUFFER_COUNT)
                {
                    // create and pin buffer array when it's larger than the pre-allocated one
                    var bufArray = new Libuv.uv_buf_t[nBuffers];
                    var gcHandle = GCHandle.Alloc(bufArray, GCHandleType.Pinned);
                    _pins.Add(gcHandle);
                    pBuffers = (Libuv.uv_buf_t*)gcHandle.AddrOfPinnedObject();
                }
                var index = 0;
                foreach (var memory in buffer)
                {
                    var tryGetPointerResult = memory.TryGetPointer(out var pointer);
                    Debug.Assert(tryGetPointerResult);

                    // create and pin each segment being written
                    pBuffers[index] = Libuv.buf_init(
                        (IntPtr) pointer,
                        memory.Length);
                    index++;
                }
                
                _uv.write(this, handle, pBuffers, nBuffers, _uv_write_cb);
            }
            catch
            {
                Unpin(this);
                throw;
            }
            return new UvWriteReqAwaiter(this);
        }

        public UvWriteReqAwaiter Write(
            UvStreamHandle handle,
            ArraySegment<ArraySegment<byte>> bufs)
        {
            return WriteArraySegmentInternal(handle, bufs, sendHandle: null);
        }

        public UvWriteReqAwaiter Write2(
            UvStreamHandle handle,
            ArraySegment<ArraySegment<byte>> bufs,
            UvStreamHandle sendHandle,
            Action<UvWriteReq, int, Exception, object> callback,
            object state)
        {
            return WriteArraySegmentInternal(handle, bufs, sendHandle);
        }

        private unsafe UvWriteReqAwaiter WriteArraySegmentInternal(
            UvStreamHandle handle,
            ArraySegment<ArraySegment<byte>> bufs,
            UvStreamHandle sendHandle)
        {
            try
            {
                // add GCHandle to keeps this SafeHandle alive while request processing
                _pins.Add(GCHandle.Alloc(this, GCHandleType.Normal));

                var pBuffers = (Libuv.uv_buf_t*)_bufs;
                var nBuffers = bufs.Count;
                if (nBuffers > BUFFER_COUNT)
                {
                    // create and pin buffer array when it's larger than the pre-allocated one
                    var bufArray = new Libuv.uv_buf_t[nBuffers];
                    var gcHandle = GCHandle.Alloc(bufArray, GCHandleType.Pinned);
                    _pins.Add(gcHandle);
                    pBuffers = (Libuv.uv_buf_t*)gcHandle.AddrOfPinnedObject();
                }

                for (var index = 0; index < nBuffers; index++)
                {
                    // create and pin each segment being written
                    var buf = bufs.Array[bufs.Offset + index];

                    var gcHandle = GCHandle.Alloc(buf.Array, GCHandleType.Pinned);
                    _pins.Add(gcHandle);
                    pBuffers[index] = Libuv.buf_init(
                        gcHandle.AddrOfPinnedObject() + buf.Offset,
                        buf.Count);
                }

                if (sendHandle == null)
                {
                    _uv.write(this, handle, pBuffers, nBuffers, _uv_write_cb);
                }
                else
                {
                    _uv.write2(this, handle, pBuffers, nBuffers, sendHandle, _uv_write_cb);
                }
            }
            catch
            {
                Unpin(this);
                throw;
            }
            return new UvWriteReqAwaiter(this);
        }

        private static void Unpin(UvWriteReq req)
        {
            foreach (var pin in req._pins)
            {
                pin.Free();
            }
            req._pins.Clear();
        }

        private static void UvWriteCb(IntPtr ptr, int status)
        {
            var req = FromIntPtr<UvWriteReq>(ptr);
            Unpin(req);
            
            Exception error = null;
            if (status < 0)
            {
                req.Libuv.Check(status, out error);
            }

            try
            {
                 req._error = error;
                 req._status = status;
                 req.Complete();
            }
            catch (Exception ex)
            {
                req._log.LogError(0, ex, "UvWriteCb");
                throw;
            }
        }

        public void Complete()
        {
            var awaitableState = _state;
            _state = _awaitableIsCompleted;

            if (!ReferenceEquals(awaitableState, _awaitableIsCompleted) &&
                !ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                awaitableState();
            }
        }

        public void OnCompleted(Action continuation)
        {
            var awaitableState = _state;
            if (_state == _awaitableIsNotCompleted)
            {
                _state = continuation;
            }

            if (ReferenceEquals(awaitableState, _awaitableIsCompleted))
            {
                continuation();
            }
            else if (!ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                _state = _awaitableIsCompleted;

                Task.Run(continuation);
                Task.Run(awaitableState);
            }
        }


        private static readonly Action _awaitableIsCompleted = () => { };
        private static readonly Action _awaitableIsNotCompleted = () => { };
        
        private Action _state;
        private Exception _error;
        private int _status;

        public UvWriteResult GetResult()
        {
            return new UvWriteResult(_status, _error);
        }
    }

    public struct UvWriteReqAwaiter: ICriticalNotifyCompletion
    {
        private readonly UvWriteReq _req;

        public UvWriteReqAwaiter(UvWriteReq req)
        {
            _req = req;
        }
        

        public bool IsCompleted => false;

        public UvWriteResult GetResult() => _req.GetResult();

        public UvWriteReqAwaiter GetAwaiter() => this;

        public void UnsafeOnCompleted(Action continuation) => _req.OnCompleted(continuation);

        public void OnCompleted(Action continuation) => _req.OnCompleted(continuation);
    }

    public struct UvWriteResult
    {
        public int Status;
        public Exception Error;

        public UvWriteResult(int status, Exception error)
        {
            Status = status;
            Error = error;
        }
    }
}