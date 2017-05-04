// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal
{
    public class RawStream : Stream
    {
        private readonly IPipeReader _input;
        private readonly ISocketOutput _output;

        public RawStream(IPipeReader input, ISocketOutput output)
        {
            _input = input;
            _output = output;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }
            set
            {
                throw new NotSupportedException();
            }
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
            // https://github.com/dotnet/corefx/blob/f9da3b4af08214764a51b2331f3595ffaf162abe/src/System.Threading.Tasks.Extensions/src/System/Threading/Tasks/ValueTask.cs#L156
            return ReadAsync(new ArraySegment<byte>(buffer, offset, count)).Result;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(new ArraySegment<byte>(buffer, offset, count));
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArraySegment<byte> segment;
            if (buffer != null)
            {
                segment = new ArraySegment<byte>(buffer, offset, count);
            }
            else
            {
                segment = default(ArraySegment<byte>);
            }
            _output.Write(segment);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            ArraySegment<byte> segment;
            if (buffer != null)
            {
                segment = new ArraySegment<byte>(buffer, offset, count);
            }
            else
            {
                segment = default(ArraySegment<byte>);
            }
            return _output.WriteAsync(segment, cancellationToken: token);
        }

        public override void Flush()
        {
            _output.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _output.FlushAsync(cancellationToken);
        }

        private async Task<int> ReadAsync(ArraySegment<byte> buffer)
        {
            while (true)
            {
                var result = await _input.ReadAsync();
                var readableBuffer = result.Buffer;
                try
                {
                    if (!readableBuffer.IsEmpty)
                    {
                        var count = Math.Min(readableBuffer.Length, buffer.Count);
                        readableBuffer = readableBuffer.Slice(0, count);
                        readableBuffer.CopyTo(buffer);
                        return count;
                    }
                    else if (result.IsCompleted)
                    {
                        return 0;
                    }
                }
                finally
                {
                    _input.Advance(readableBuffer.End, readableBuffer.End);
                }
            }
        }
    }
}
