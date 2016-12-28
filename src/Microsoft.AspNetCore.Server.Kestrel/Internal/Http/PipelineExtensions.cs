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
            if (input.IsCompleted)
            {
                var result = input.GetResult();

                var segment = result.Buffer.First;
                var x = result.Buffer.Slice(0, segment.Length);
                pipelineReader.Advance(x.Start);

                ArraySegment<byte> data;
                var arrayResult = segment.TryGetArray(out data);
                Debug.Assert(arrayResult);

                return new ValueTask<ArraySegment<byte>>(data);
            }

            return new ValueTask<ArraySegment<byte>>(pipelineReader.PeekAsyncAwaited(input));
        }

        private static async Task<ArraySegment<byte>> PeekAsyncAwaited(this IPipelineReader pipelineReader, ReadableBufferAwaitable readingTask)
        {
            var result = await readingTask;
            var segment = result.Buffer.First;
            pipelineReader.Advance(result.Buffer.Start);
            ArraySegment<byte> data;
            var arrayResult = segment.TryGetArray(out data);
            Debug.Assert(arrayResult);
            return data;
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