﻿using System;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure
{
    public struct WritableBufferWriter
    {
        private const int _maxULongByteLength = 20;

        [ThreadStatic]
        private static byte[] _numericBytesScratch;

        private int _index;
        private WritableBuffer _buffer;
        private int _length;
        private Span<byte> _span;

        public WritableBufferWriter(WritableBuffer buffer)
        {
            _buffer = buffer;
            _index = 0;
            _span = buffer.Buffer.Span;
            _length = _span.Length;
        }

        public void WriteFast(byte[] source)
        {
            WriteFast(source, 0, source.Length);
        }

        public void WriteFast(ArraySegment<byte> source)
        {
            WriteFast(source.Array, source.Offset, source.Count);
        }

        public void WriteFast(byte[] source, int offset, int length)
        {
            var dest = _span;
            var index = _index;
            var destLength = _length;
            var sourceLength = length;

            if (sourceLength <= destLength)
            {
                ref byte pSource = ref source[offset];
                ref byte pDest = ref dest[index];
                Unsafe.CopyBlockUnaligned(ref pDest, ref pSource, (uint)sourceLength);
                Advance(sourceLength);
                return;
            }

            WriteMultiBuffer(source, offset, length);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow()
        {
            // Advance the buffer to what we've written for this block
            _buffer.Advance(_index);
            _buffer.Ensure();
            _index = 0;
            _span = _buffer.Buffer.Span;
            _length = _span.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Advance(int length)
        {
            _index += length;
            _length -= length;
        }

        public void Commit()
        {
            if (_length > 0)
            {
                _buffer.Advance(_index);
            }
        }

        private void WriteMultiBuffer(byte[] source, int offset, int length)
        {
            var remaining = length;
            var span = _span;
            var index = _index;
            var remainingInSpan = _length;

            while (remaining > 0)
            {
                if (remainingInSpan == 0)
                {
                    Grow();

                    span = _span;
                    index = _index;
                    remainingInSpan = _length;
                }

                var writable = Math.Min(remaining, remainingInSpan);

                ref byte pSource = ref source[offset];
                ref byte pDest = ref span[index];

                Unsafe.CopyBlockUnaligned(ref pDest, ref pSource, (uint)writable);

                remaining -= writable;
                offset += writable;
                index += writable;
                remainingInSpan -= writable;

                Advance(writable);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteNumeric(ulong number)
        {
            const byte AsciiDigitStart = (byte)'0';

            var span = _span;
            var index = _index;
            var remainingInSpan = _length;

            // Fast path, try copying to the available memory directly
            var simpleWrite = true;

            if (remainingInSpan > 0)
            {
                fixed (byte* output = &span[index])
                {
                    var start = output;
                    if (number < 10 && remainingInSpan >= 1)
                    {
                        *(start) = (byte)(((uint)number) + AsciiDigitStart);
                        Advance(1);
                    }
                    else if (number < 100 && remainingInSpan >= 2)
                    {
                        var val = (uint)number;
                        var tens = (byte)((val * 205u) >> 11); // div10, valid to 1028

                        *(start) = (byte)(tens + AsciiDigitStart);
                        *(start + 1) = (byte)(val - (tens * 10) + AsciiDigitStart);
                        Advance(2);
                    }
                    else if (number < 1000 && remainingInSpan >= 3)
                    {
                        var val = (uint)number;
                        var digit0 = (byte)((val * 41u) >> 12); // div100, valid to 1098
                        var digits01 = (byte)((val * 205u) >> 11); // div10, valid to 1028

                        *(start) = (byte)(digit0 + AsciiDigitStart);
                        *(start + 1) = (byte)(digits01 - (digit0 * 10) + AsciiDigitStart);
                        *(start + 2) = (byte)(val - (digits01 * 10) + AsciiDigitStart);
                        Advance(3);
                    }
                    else
                    {
                        simpleWrite = false;
                    }
                }
            }

            if (!simpleWrite)
            {
                WriteNumericMultiWrite(number);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteNumericMultiWrite(ulong number)
        {
            const byte AsciiDigitStart = (byte)'0';

            var value = number;
            var position = _maxULongByteLength;
            var byteBuffer = NumericBytesScratch;
            do
            {
                // Consider using Math.DivRem() if available
                var quotient = value / 10;
                byteBuffer[--position] = (byte)(AsciiDigitStart + (value - quotient * 10)); // 0x30 = '0'
                value = quotient;
            }
            while (value != 0);

            var length = _maxULongByteLength - position;
            WriteFast(new ArraySegment<byte>(byteBuffer, position, length));
        }

        public unsafe void WriteAscii(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return;
            }

            var dest = _span;
            var index = _index;
            var destLength = _length;
            var sourceLength = data.Length;

            // Fast path, try copying to the available memory directly
            if (sourceLength <= destLength)
            {
                fixed (char* input = data)
                fixed (byte* output = &dest[index])
                {
                    EncodeAsciiCharsToBytes(input, output, sourceLength);
                }

                Advance(sourceLength);
            }
            else
            {
                WriteAsciiMultiWrite(data);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private unsafe void WriteAsciiMultiWrite(string data)
        {
            var remaining = data.Length;
            var span = _span;
            var index = _index;
            var remainingInSpan = _length;

            fixed (char* input = data)
            {
                var inputSlice = input;

                while (remaining > 0)
                {
                    if (remainingInSpan == 0)
                    {
                        Grow();

                        index = _index;
                        span = _span;
                        remainingInSpan = span.Length;
                    }

                    var writable = Math.Min(remaining, remainingInSpan);

                    fixed (byte* output = &span[index])
                    {
                        EncodeAsciiCharsToBytes(inputSlice, output, writable);
                    }

                    inputSlice += writable;
                    remaining -= writable;
                    remainingInSpan -= writable;
                    index += writable;

                    Advance(writable);
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

        private static byte[] NumericBytesScratch => _numericBytesScratch ?? CreateNumericBytesScratch();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static byte[] CreateNumericBytesScratch()
        {
            var bytes = new byte[_maxULongByteLength];
            _numericBytesScratch = bytes;
            return bytes;
        }
    }
}
