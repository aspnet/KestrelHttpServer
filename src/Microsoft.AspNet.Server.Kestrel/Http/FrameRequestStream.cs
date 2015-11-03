// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public class FrameRequestStream : Stream
    {
        private readonly MessageBody _body;
        private bool _stopped;

        public FrameRequestStream(MessageBody body)
        {
            _body = body;
        }

        public override bool CanRead { get { return true; } }

        public override bool CanSeek { get { return false; } }

        public override bool CanWrite { get { return false; } }

        public override long Length
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override long Position { get; set; }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_stopped)
            {
                throw new ObjectDisposedException(nameof(FrameRequestStream));
            }

            return ReadAsync(buffer, offset, count).Result;
        }

#if NET451
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            if (_stopped)
            {
                throw new ObjectDisposedException(nameof(FrameRequestStream));
            }

            var task = ReadAsync(buffer, offset, count, CancellationToken.None, state);
            if (callback != null)
            {
                task.ContinueWith(t => callback.Invoke(t));
            }
            return task;
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            return ((Task<int>)asyncResult).Result;
        }
#endif

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_stopped)
            {
                throw new ObjectDisposedException(nameof(FrameRequestStream));
            }

            return _body.ReadAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken);
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken, object state)
        {
            if (_stopped)
            {
                throw new ObjectDisposedException(nameof(FrameRequestStream));
            }

            var tcs = new TaskCompletionSource<int>(state);
            var task = _body.ReadAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken);
            task.ContinueWith((task2, state2) =>
            {
                var tcs2 = (TaskCompletionSource<int>)state2;
                if (task2.IsCanceled)
                {
                    tcs2.SetCanceled();
                }
                else if (task2.IsFaulted)
                {
                    tcs2.SetException(task2.Exception);
                }
                else
                {
                    tcs2.SetResult(task2.Result);
                }
            }, tcs);
            return tcs.Task;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public void StopAcceptingReads()
        {
            // Can't use dispose (or close) as can be disposed too early by user code
            // As exampled in EngineTests.ZeroContentLengthNotSetAutomaticallyForCertainStatusCodes
            _stopped = true;
        }
    }
}
