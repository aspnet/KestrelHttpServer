using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public static class PipelineExtensions
    {
        public static ValueTask<ArraySegment<byte>> PeekAsync(this IPipelineReader pipelineReader)
        {
            var input = pipelineReader.ReadAsync();
            while (input.IsCompleted)
            {
                var result = input.GetResult();
                pipelineReader.Advance(result.Buffer.Start, result.Buffer.Start);

                if (!result.Buffer.IsEmpty)
                {
                    ArraySegment<byte> data;
                    var segment = result.Buffer.First;
                    var arrayResult = segment.TryGetArray(out data);
                    Debug.Assert(arrayResult);

                    return new ValueTask<ArraySegment<byte>>(data);
                }
                input = pipelineReader.ReadAsync();
            }

            return new ValueTask<ArraySegment<byte>>(pipelineReader.PeekAsyncAwaited(input));
        }

        private static async Task<ArraySegment<byte>> PeekAsyncAwaited(this IPipelineReader pipelineReader, ReadableBufferAwaitable readingTask)
        {
            while (true)
            {
                var result = await readingTask;
                pipelineReader.Advance(result.Buffer.Start);

                if (!result.Buffer.IsEmpty)
                {
                    ArraySegment<byte> data;
                    var segment = result.Buffer.First;
                    var arrayResult = segment.TryGetArray(out data);
                    Debug.Assert(arrayResult);

                    return data;
                }
                readingTask = pipelineReader.ReadAsync();
            }
        }

        public static Span<byte> ToSpan(this ReadableBuffer buffer)
        {
            if (buffer.IsSingleSpan)
            {
                return buffer.First.Span;
            }
            else
            {
                // todo: slow
                return buffer.ToArray();
            }
        }
    }
}