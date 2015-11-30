// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    class FrameDuplexStream : IBlockingAsyncDuplexStream<byte>
    {
        private readonly MessageBody _body;
        private readonly FrameContext _context;
        private StreamState _state;

        public FrameDuplexStream(MessageBody body, FrameContext context)
        {
            _body = body;
            _context = context;
            _state = StreamState.Open;
        }

        // ValueTask handles .GetAwaiter().GetResult() if required
        public int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count).Result;

        public ValueTask<int> ReadAsync(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count, CancellationToken.None);

        public ValueTask<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateState();
            return _body.ReadAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken);
        }

        public void Flush()
        {
            ValidateState();
            _context.FrameControl.Flush();
        }

        public Task FlushAsync()
            => FlushAsync(CancellationToken.None);

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            ValidateState();
            return _context.FrameControl.FlushAsync(cancellationToken);
        }

        public void Write(byte value)
        {
            ValidateState();
            _context.FrameControl.Write(new ArraySegment<byte>(new byte[] { value }));
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            ValidateState();
            _context.FrameControl.Write(new ArraySegment<byte>(buffer, offset, count));
        }

        public Task WriteAsync(byte[] buffer, int offset, int count)
            => WriteAsync(buffer, offset, count, CancellationToken.None);

        public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateState();
            return _context.FrameControl.WriteAsync(new ArraySegment<byte>(buffer, offset, count), cancellationToken);
        }

        private void ValidateState()
        {
            switch (_state)
            {
                case StreamState.Open:
                    return;
                case StreamState.Disposed:
                    throw new ObjectDisposedException(nameof(FrameDuplexStream));
                case StreamState.Aborted:
                    throw new IOException("The request has been aborted.");
                default:
                    throw new IOException("Invalid Stream State.");
            }
        }

        public void Abort()
        {
            if (_state != StreamState.Disposed)
            {
                _state = StreamState.Aborted;
            }
        }

        public void Dispose()
        {
            _state = StreamState.Disposed;
        }

        enum StreamState
        {
            Open,
            Disposed,
            Aborted
        }
    }
}
