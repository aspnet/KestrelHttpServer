// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
namespace Microsoft.AspNetCore.Server.Kestrel.Adapter.Internal
{
    public class StreamSocketOutput : ISocketOutput
    {
        private static readonly byte[] _endChunkBytes = Encoding.ASCII.GetBytes("\r\n");
        private static readonly byte[] _nullBuffer = new byte[0];

        private readonly Stream _outputStream;
        private readonly IPipe _pipe;

        public StreamSocketOutput(Stream outputStream, IPipe pipe)
        {
            _outputStream = outputStream;
            _pipe = pipe;
        }

        public void Write(ArraySegment<byte> buffer, bool chunk)
        {
            WriteAsync(buffer, chunk, default(CancellationToken)).GetAwaiter().GetResult();
        }

        public async Task WriteAsync(ArraySegment<byte> buffer, bool chunk, CancellationToken cancellationToken)
        {
            var writableBuffer = _pipe.Writer.Alloc();

            if (chunk && buffer.Array != null)
            {
                writableBuffer.Write(ChunkWriter.BeginChunkBytes(buffer.Count));
                writableBuffer.Write(buffer.Array);
                writableBuffer.Write(_endChunkBytes);
            }
            else
            {
                writableBuffer.Write(buffer.Array ?? _nullBuffer);
            }

            await writableBuffer.FlushAsync();
        }
        
        public void Flush()
        {
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            //return _outputStream.FlushAsync(cancellationToken);
            return Task.FromResult(0);
        }

        public WritableBuffer Alloc()
        {
            return _pipe.Writer.Alloc();
        }

        public async Task WriteOutputAsync()
        {
            while (true)
            {
                var readResult = await _pipe.Reader.ReadAsync();
                foreach (var memory in readResult.Buffer)
                {
                    var array = memory.GetArray();
                    _outputStream.Write(array.Array, array.Offset, array.Count);
                }
                _pipe.Reader.Advance(readResult.Buffer.End);
                if (readResult.Buffer.IsEmpty && readResult.IsCompleted)
                {
                    return;
                }
            }
        }

        public void Complete()
        {
            _pipe.Writer.Complete();
        }

    }
}
