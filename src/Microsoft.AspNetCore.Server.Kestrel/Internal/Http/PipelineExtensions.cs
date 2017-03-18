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

        public unsafe static void WriteAscii(this WritableBuffer buffer, string data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                if (buffer.Memory.IsEmpty)
                {
                    buffer.Ensure();
                }

                // Fast path, try copying to the available memory directly
                if (data.Length <= buffer.Memory.Length)
                {
                    fixed (char* input = data)
                    fixed (byte* output = &buffer.Memory.Span.DangerousGetPinnableReference())
                    {
                        EncodeAsciiCharsToBytes(input, output, data.Length);
                    }

                    buffer.Advance(data.Length);
                }
                else
                {
                    buffer.WriteAsciiMultiWrite(data);
                }
            }
        }

        public static void Write(this WritableBuffer buffer, string data)
        {
            buffer.Write(Encoding.UTF8.GetBytes(data));
        }

        public static void WriteNumeric(this WritableBuffer buffer, ulong number)
        {
            buffer.Write(number.ToString());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private unsafe static void WriteAsciiMultiWrite(this WritableBuffer buffer, string data)
        {
            var remaining = data.Length;

            fixed (char* input = data)
            {
                var inputSlice = input;

                while (remaining > 0)
                {
                    var writable = Math.Min(remaining, buffer.Memory.Length);

                    buffer.Ensure(writable);

                    if (writable == 0)
                    {
                        continue;
                    }

                    fixed (byte* output = &buffer.Memory.Span.DangerousGetPinnableReference())
                    {
                        EncodeAsciiCharsToBytes(inputSlice, output, writable);
                    }

                    inputSlice += writable;
                    remaining -= writable;

                    buffer.Advance(writable);
                }
            }
        }

        private unsafe static void EncodeAsciiCharsToBytes(char* input, byte* output, int length)
        {
            // Note: Not BIGENDIAN or check for non-ascii
            const int Shift16Shift24 = (1 << 16) | (1 << 24);
            const int Shift8Identity = (1 << 8) | (1);

            // Encode as bytes upto the first non-ASCII byte and return count encoded
            int i = 0;
            // Use Intrinsic switch
            if (IntPtr.Size == 8) // 64 bit
            {
                if (length < 4) goto trailing;

                int unaligned = (int)(((ulong)input) & 0x7) >> 1;
                // Unaligned chars
                for (; i < unaligned; i++)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                }

                // Aligned
                int ulongDoubleCount = (length - i) & ~0x7;
                for (; i < ulongDoubleCount; i += 8)
                {
                    ulong inputUlong0 = *(ulong*)(input + i);
                    ulong inputUlong1 = *(ulong*)(input + i + 4);
                    // Pack 16 ASCII chars into 16 bytes
                    *(uint*)(output + i) =
                        ((uint)((inputUlong0 * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong0 * Shift8Identity) >> 24) & 0xffff0000);
                    *(uint*)(output + i + 4) =
                        ((uint)((inputUlong1 * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong1 * Shift8Identity) >> 24) & 0xffff0000);
                }
                if (length - 4 > i)
                {
                    ulong inputUlong = *(ulong*)(input + i);
                    // Pack 8 ASCII chars into 8 bytes
                    *(uint*)(output + i) =
                        ((uint)((inputUlong * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong * Shift8Identity) >> 24) & 0xffff0000);
                    i += 4;
                }

            trailing:
                for (; i < length; i++)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                }
            }
            else // 32 bit
            {
                // Unaligned chars
                if ((unchecked((int)input) & 0x2) != 0)
                {
                    char ch = *input;
                    i = 1;
                    *(output) = (byte)ch; // Cast convert
                }

                // Aligned
                int uintCount = (length - i) & ~0x3;
                for (; i < uintCount; i += 4)
                {
                    uint inputUint0 = *(uint*)(input + i);
                    uint inputUint1 = *(uint*)(input + i + 2);
                    // Pack 4 ASCII chars into 4 bytes
                    *(ushort*)(output + i) = (ushort)(inputUint0 | (inputUint0 >> 8));
                    *(ushort*)(output + i + 2) = (ushort)(inputUint1 | (inputUint1 >> 8));
                }
                if (length - 1 > i)
                {
                    uint inputUint = *(uint*)(input + i);
                    // Pack 2 ASCII chars into 2 bytes
                    *(ushort*)(output + i) = (ushort)(inputUint | (inputUint >> 8));
                    i += 2;
                }

                if (i < length)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                    i = length;
                }
            }
        }
    }
}