// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    internal class FrameRequestStream : ReadOnlyStream
    {
        private MessageBody _body;
        private FrameStreamState _state;
        private Exception _error;

        public FrameRequestStream()
        {
            _state = FrameStreamState.Closed;
        }

        public override bool CanSeek => false;

        public override long Length
            => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            // No-op.
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            // No-op.
            return TaskCache.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // ValueTask uses .GetAwaiter().GetResult() if necessary
            return ReadAsync(buffer, offset, count).Result;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var task = ValidateState(cancellationToken);
            if (task == null)
            {
                return _body.ReadAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken);
            }
            return task;
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (bufferSize <= 0)
            {
                throw new ArgumentException($"{nameof(bufferSize)} must be positive.", nameof(bufferSize));
            }

            var task = ValidateState(cancellationToken);
            if (task == null)
            {
                return _body.CopyToAsync(destination, cancellationToken);
            }
            return task;
        }

        public void StartAcceptingReads(MessageBody body)
        {
            // Only start if not aborted
            if (_state == FrameStreamState.Closed)
            {
                _state = FrameStreamState.Open;
                _body = body;
            }
        }

        public void PauseAcceptingReads()
        {
            _state = FrameStreamState.Closed;
        }

        public void ResumeAcceptingReads()
        {
            if (_state == FrameStreamState.Closed)
            {
                _state = FrameStreamState.Open;
            }
        }

        public void StopAcceptingReads()
        {
            // Can't use dispose (or close) as can be disposed too early by user code
            // As exampled in EngineTests.ZeroContentLengthNotSetAutomaticallyForCertainStatusCodes
            _state = FrameStreamState.Closed;
            _body = null;
        }

        public void Abort(Exception error = null)
        {
            // We don't want to throw an ODE until the app func actually completes.
            // If the request is aborted, we throw an TaskCanceledException instead,
            // unless error is not null, in which case we throw it.
            if (_state != FrameStreamState.Closed)
            {
                _state = FrameStreamState.Aborted;
                _error = error;
            }
        }

        private Task<int> ValidateState(CancellationToken cancellationToken)
        {
            switch (_state)
            {
                case FrameStreamState.Open:
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Task.FromCanceled<int>(cancellationToken);
                    }
                    break;
                case FrameStreamState.Closed:
                    throw new ObjectDisposedException(nameof(FrameRequestStream));
                case FrameStreamState.Aborted:
                    return _error != null ?
                        Task.FromException<int>(_error) :
                        Task.FromCanceled<int>(new CancellationToken(true));
            }
            return null;
        }
    }
}
