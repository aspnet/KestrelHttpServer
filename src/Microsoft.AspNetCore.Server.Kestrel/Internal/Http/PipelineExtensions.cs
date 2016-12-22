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

            return new ValueTask<ArraySegment<byte>>(pipelineReader.PeekAsyncAwaited());
        }

        private static async Task<ArraySegment<byte>> PeekAsyncAwaited(this IPipelineReader pipelineReader)
        {
            ReadResult result;
            Memory<byte> segment;
            do
            {
                result = await pipelineReader.ReadAsync();

                segment = result.Buffer.First;
                var x = result.Buffer.Slice(0, segment.Length);
                pipelineReader.Advance(x.Start);
            }
            while (segment.Length != 0 || result.IsCompleted || result.IsCancelled);

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