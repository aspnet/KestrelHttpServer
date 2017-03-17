// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public static class PipelineExtensions
    {
        public static ValueTask<ArraySegment<byte>> PeekAsync(this IPipeReader pipelineReader)
        {
            var input = pipelineReader.ReadAsync();
            while (input.IsCompleted)
            {
                var result = input.GetResult();
                try
                {
                    if (!result.Buffer.IsEmpty)
                    {
                        var segment = result.Buffer.First;
                        var data = segment.GetArray();

                        return new ValueTask<ArraySegment<byte>>(data);
                    }
                    else if (result.IsCompleted)
                    {
                        return default(ValueTask<ArraySegment<byte>>);
                    }
                }
                finally
                {
                    pipelineReader.Advance(result.Buffer.Start, result.Buffer.Start);
                }
                input = pipelineReader.ReadAsync();
            }

            return new ValueTask<ArraySegment<byte>>(pipelineReader.PeekAsyncAwaited(input));
        }

        private static async Task<ArraySegment<byte>> PeekAsyncAwaited(this IPipeReader pipelineReader, ReadableBufferAwaitable readingTask)
        {
            while (true)
            {
                var result = await readingTask;

                try
                {
                    if (!result.Buffer.IsEmpty)
                    {
                        var segment = result.Buffer.First;
                        return segment.GetArray();
                    }
                    else if (result.IsCompleted)
                    {
                        return default(ArraySegment<byte>);
                    }
                }
                finally
                {
                    pipelineReader.Advance(result.Buffer.Start, result.Buffer.Start);
                }

                readingTask = pipelineReader.ReadAsync();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<byte> ToSpan(this ReadableBuffer buffer)
        {
            if (buffer.IsSingleSpan)
            {
                return buffer.First.Span;
            }
            return buffer.ToArray();
        }

        public static ArraySegment<byte> GetArray(this Memory<byte> memory)
        {
            ArraySegment<byte> result;
            if (!memory.TryGetArray(out result))
            {
                throw new InvalidOperationException("Memory backed by array was expected");
            }
            return result;
        }

        public static void WriteAscii(this WritableBuffer buffer, string data)
        {
            buffer.Write(Encoding.ASCII.GetBytes(data));
        }
        public static void Write(this WritableBuffer buffer, string data)
        {
            buffer.Write(Encoding.UTF8.GetBytes(data));
        }

        public static void WriteNumeric(this WritableBuffer buffer, ulong number)
        {
            buffer.Write(number.ToString());
        }
    }
}