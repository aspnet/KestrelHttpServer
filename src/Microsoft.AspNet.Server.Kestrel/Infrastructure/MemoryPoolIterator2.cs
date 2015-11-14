﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    public struct MemoryPoolIterator2
    {
        private const int _maxStackAllocBytes = 16384;
        /// <summary>
        /// Array of "minus one" bytes of the length of SIMD operations on the current hardware. Used as an argument in the
        /// vector dot product that counts matching character occurrence.
        /// </summary>
        private static Vector<byte> _dotCount = new Vector<byte>(Byte.MaxValue);

        /// <summary>
        /// Array of negative numbers starting at 0 and continuing for the length of SIMD operations on the current hardware.
        /// Used as an argument in the vector dot product that determines matching character index.
        /// </summary>
        private static Vector<byte> _dotIndex = new Vector<byte>(Enumerable.Range(0, Vector<byte>.Count).Select(x => (byte)-x).ToArray());

        private static Encoding _utf8 = Encoding.UTF8;

        private MemoryPoolBlock2 _block;
        private int _index;

        public MemoryPoolIterator2(MemoryPoolBlock2 block)
        {
            _block = block;
            _index = _block?.Start ?? 0;
        }
        public MemoryPoolIterator2(MemoryPoolBlock2 block, int index)
        {
            _block = block;
            _index = index;
        }

        public bool IsDefault => _block == null;

        public bool IsEnd
        {
            get
            {
                if (_block == null)
                {
                    return true;
                }
                else if (_index < _block.End)
                {
                    return false;
                }
                else
                {
                    var block = _block.Next;
                    while (block != null)
                    {
                        if (block.Start < block.End)
                        {
                            return false; // subsequent block has data - IsEnd is false
                        }
                        block = block.Next;
                    }
                    return true;
                }
            }
        }

        public MemoryPoolBlock2 Block => _block;

        public int Index => _index;

        public int Take()
        {
            if (_block == null)
            {
                return -1;
            }
            else if (_index < _block.End)
            {
                return _block.Array[_index++];
            }

            var block = _block;
            var index = _index;
            while (true)
            {
                if (index < block.End)
                {
                    _block = block;
                    _index = index + 1;
                    return block.Array[index];
                }
                else if (block.Next == null)
                {
                    return -1;
                }
                else
                {
                    block = block.Next;
                    index = block.Start;
                }
            }
        }

        public int Peek()
        {
            if (_block == null)
            {
                return -1;
            }
            else if (_index < _block.End)
            {
                return _block.Array[_index];
            }
            else if (_block.Next == null)
            {
                return -1;
            }

            var block = _block.Next;
            var index = block.Start;
            while (true)
            {
                if (index < block.End)
                {
                    return block.Array[index];
                }
                else if (block.Next == null)
                {
                    return -1;
                }
                else
                {
                    block = block.Next;
                    index = block.Start;
                }
            }
        }

        public int Seek(int char0)
        {
            if (IsDefault)
            {
                return -1;
            }

            var byte0 = (byte)char0;
            var vectorStride = Vector<byte>.Count;
            var ch0Vector = new Vector<byte>(byte0);

            var block = _block;
            var index = _index;
            var array = block.Array;
            while (true)
            {
                while (block.End == index)
                {
                    if (block.Next == null)
                    {
                        _block = block;
                        _index = index;
                        return -1;
                    }
                    block = block.Next;
                    index = block.Start;
                    array = block.Array;
                }
                while (block.End != index)
                {
                    var following = block.End - index;
                    if (following >= vectorStride)
                    {
                        var data = new Vector<byte>(array, index);
                        var ch0Equals = Vector.Equals(data, ch0Vector);
                        var ch0Count = Vector.Dot(ch0Equals, _dotCount);

                        if (ch0Count == 0)
                        {
                            index += vectorStride;
                            continue;
                        }
                        else if (ch0Count == 1)
                        {
                            _block = block;
                            _index = index + Vector.Dot(ch0Equals, _dotIndex);
                            return char0;
                        }
                        else
                        {
                            following = vectorStride;
                        }
                    }
                    while (following > 0)
                    {
                        if (block.Array[index] == byte0)
                        {
                            _block = block;
                            _index = index;
                            return char0;
                        }
                        following--;
                        index++;
                    }
                }
            }
        }

        public int Seek(int char0, int char1)
        {
            if (IsDefault)
            {
                return -1;
            }

            var byte0 = (byte)char0;
            var byte1 = (byte)char1;
            var vectorStride = Vector<byte>.Count;
            var ch0Vector = new Vector<byte>(byte0);
            var ch1Vector = new Vector<byte>(byte1);

            var block = _block;
            var index = _index;
            var array = block.Array;
            while (true)
            {
                while (block.End == index)
                {
                    if (block.Next == null)
                    {
                        _block = block;
                        _index = index;
                        return -1;
                    }
                    block = block.Next;
                    index = block.Start;
                    array = block.Array;
                }
                while (block.End != index)
                {
                    var following = block.End - index;
                    if (following >= vectorStride)
                    {
                        var data = new Vector<byte>(array, index);
                        var ch0Equals = Vector.Equals(data, ch0Vector);
                        var ch0Count = Vector.Dot(ch0Equals, _dotCount);
                        var ch1Equals = Vector.Equals(data, ch1Vector);
                        var ch1Count = Vector.Dot(ch1Equals, _dotCount);

                        if (ch0Count == 0 && ch1Count == 0)
                        {
                            index += vectorStride;
                            continue;
                        }
                        else if (ch0Count < 2 && ch1Count < 2)
                        {
                            var ch0Index = ch0Count == 1 ? Vector.Dot(ch0Equals, _dotIndex) : byte.MaxValue;
                            var ch1Index = ch1Count == 1 ? Vector.Dot(ch1Equals, _dotIndex) : byte.MaxValue;
                            if (ch0Index < ch1Index)
                            {
                                _block = block;
                                _index = index + ch0Index;
                                return char0;
                            }
                            else
                            {
                                _block = block;
                                _index = index + ch1Index;
                                return char1;
                            }
                        }
                        else
                        {
                            following = vectorStride;
                        }
                    }
                    while (following > 0)
                    {
                        var byteIndex = block.Array[index];
                        if (byteIndex == byte0)
                        {
                            _block = block;
                            _index = index;
                            return char0;
                        }
                        else if (byteIndex == byte1)
                        {
                            _block = block;
                            _index = index;
                            return char1;
                        }
                        following--;
                        index++;
                    }
                }
            }
        }

        public int Seek(int char0, int char1, int char2)
        {
            if (IsDefault)
            {
                return -1;
            }

            var byte0 = (byte)char0;
            var byte1 = (byte)char1;
            var byte2 = (byte)char2;
            var vectorStride = Vector<byte>.Count;
            var ch0Vector = new Vector<byte>(byte0);
            var ch1Vector = new Vector<byte>(byte1);
            var ch2Vector = new Vector<byte>(byte2);

            var block = _block;
            var index = _index;
            var array = block.Array;
            while (true)
            {
                while (block.End == index)
                {
                    if (block.Next == null)
                    {
                        _block = block;
                        _index = index;
                        return -1;
                    }
                    block = block.Next;
                    index = block.Start;
                    array = block.Array;
                }
                while (block.End != index)
                {
                    var following = block.End - index;
                    if (following >= vectorStride)
                    {
                        var data = new Vector<byte>(array, index);
                        var ch0Equals = Vector.Equals(data, ch0Vector);
                        var ch0Count = Vector.Dot(ch0Equals, _dotCount);
                        var ch1Equals = Vector.Equals(data, ch1Vector);
                        var ch1Count = Vector.Dot(ch1Equals, _dotCount);
                        var ch2Equals = Vector.Equals(data, ch2Vector);
                        var ch2Count = Vector.Dot(ch2Equals, _dotCount);

                        if (ch0Count == 0 && ch1Count == 0 && ch2Count == 0)
                        {
                            index += vectorStride;
                            continue;
                        }
                        else if (ch0Count < 2 && ch1Count < 2 && ch2Count < 2)
                        {
                            var ch0Index = ch0Count == 1 ? Vector.Dot(ch0Equals, _dotIndex) : byte.MaxValue;
                            var ch1Index = ch1Count == 1 ? Vector.Dot(ch1Equals, _dotIndex) : byte.MaxValue;
                            var ch2Index = ch2Count == 1 ? Vector.Dot(ch2Equals, _dotIndex) : byte.MaxValue;

                            int toReturn, toMove;
                            if (ch0Index < ch1Index)
                            {
                                if (ch0Index < ch2Index)
                                {
                                    toReturn = char0;
                                    toMove = ch0Index;
                                }
                                else
                                {
                                    toReturn = char2;
                                    toMove = ch2Index;
                                }
                            }
                            else
                            {
                                if (ch1Index < ch2Index)
                                {
                                    toReturn = char1;
                                    toMove = ch1Index;
                                }
                                else
                                {
                                    toReturn = char2;
                                    toMove = ch2Index;
                                }
                            }

                            _block = block;
                            _index = index + toMove;
                            return toReturn;
                        }
                        else
                        {
                            following = vectorStride;
                        }
                    }
                    while (following > 0)
                    {
                        var byteIndex = block.Array[index];
                        if (byteIndex == byte0)
                        {
                            _block = block;
                            _index = index;
                            return char0;
                        }
                        else if (byteIndex == byte1)
                        {
                            _block = block;
                            _index = index;
                            return char1;
                        }
                        else if (byteIndex == byte2)
                        {
                            _block = block;
                            _index = index;
                            return char2;
                        }
                        following--;
                        index++;
                    }
                }
            }
        }

        /// <summary>
        /// Save the data at the current location then move to the next available space.
        /// </summary>
        /// <param name="data">The byte to be saved.</param>
        /// <returns>true if the operation successes. false if can't find available space.</returns>
        public bool Put(byte data)
        {
            if (_block == null)
            {
                return false;
            }
            else if (_index < _block.End)
            {
                _block.Array[_index++] = data;
                return true;
            }

            var block = _block;
            var index = _index;
            while (true)
            {
                if (index < block.End)
                {
                    _block = block;
                    _index = index + 1;
                    block.Array[index] = data;
                    return true;
                }
                else if (block.Next == null)
                {
                    return false;
                }
                else
                {
                    block = block.Next;
                    index = block.Start;
                }
            }
        }

        public int GetLength(ref MemoryPoolIterator2 end)
        {
            if (IsDefault || end.IsDefault)
            {
                return -1;
            }

            var block = _block;
            var index = _index;
            var length = 0;
            while (true)
            {
                if (block == end._block)
                {
                    return length + end._index - index;
                }
                else if (block.Next == null)
                {
                    throw new InvalidOperationException("end did not follow iterator");
                }
                else
                {
                    length += block.End - index;
                    block = block.Next;
                    index = block.Start;
                }
            }
        }

        private static unsafe string SingleBlockAsciiString(byte[] input, int offset, int length)
        {
            // avoid declaring other local vars, or doing work with stackalloc
            // to prevent the .locals init cil flag , see: https://github.com/dotnet/coreclr/issues/1279
            char* output = stackalloc char[length];

            return SingleBlockAsciiIter(output, input, offset, length);
        }

        private static unsafe string SingleBlockAsciiIter(char* output, byte[] input, int offset, int length)
        {
            for (var i = 0; i < length; i++)
            {
                output[i] = (char)input[i + offset];
            }
            return new string(output, 0, length);
        }

        private static unsafe string MultiBlockAsciiString(MemoryPoolBlock2 startBlock, ref MemoryPoolIterator2 end, int inputOffset, int length)
        {
            // avoid declaring other local vars, or doing work with stackalloc
            // to prevent the .locals init cil flag , see: https://github.com/dotnet/coreclr/issues/1279
            char* output = stackalloc char[length];

            return MultiBlockAsciiIter(output, startBlock, ref end, inputOffset, length);
        }
        
        private static unsafe string MultiBlockAsciiIter(char* output, MemoryPoolBlock2 startBlock, ref MemoryPoolIterator2 end, int inputOffset, int length)
        {
            var outputOffset = 0;
            var block = startBlock;
            var remaining = length;

            while(true)
            {
                int following = (block != end._block ? block.End : end._index) - inputOffset;

                if (following > 0)
                {
                    var input = block.Array;
                    for (var i = 0; i < following; i++)
                    {
                        output[i + outputOffset] = (char)input[i + inputOffset];
                    }

                    remaining -= following;
                    outputOffset += following;
                }
                
                if (remaining == 0)
                {
                    return new string(output, 0, length);
                }

                block = block.Next;
                inputOffset = block.Start;
            }
        }

        public string GetAsciiStringHeap(MemoryPoolBlock2 startBlock, ref MemoryPoolIterator2 end, int inputOffset, int length)
        {
            var output = new char[length];

            var outputOffset = 0;
            var block = startBlock;
            var remaining = length;

            while (true)
            {
                int following = (block != end._block ? block.End : end._index) - inputOffset;

                if (following > 0)
                {
                    var input = block.Array;
                    for (var i = 0; i < following; i++)
                    {
                        output[i + outputOffset] = (char)input[i + inputOffset];
                    }

                    remaining -= following;
                    outputOffset += following;
                }

                if (remaining == 0)
                {
                    return new string(output, 0, length); 
                }

                block = block.Next;
                inputOffset = block.Start;
            }
        }

        public string GetAsciiString(ref MemoryPoolIterator2 end)
        {
            if (IsDefault || end.IsDefault)
            {
                return default(string);
            }

            var length = GetLength(ref end);

            if (length > _maxStackAllocBytes)
            {
                return GetAsciiStringHeap(_block, ref end, _index, length);
            }

            if (end._block == _block)
            {
                return SingleBlockAsciiString(_block.Array, _index, length);
            }

            return MultiBlockAsciiString(_block, ref end, _index, length);
        }

        public string GetUtf8String(ref MemoryPoolIterator2 end)
        {
            if (IsDefault || end.IsDefault)
            {
                return default(string);
            }
            if (end._block == _block)
            {
                return _utf8.GetString(_block.Array, _index, end._index - _index);
            }
            
            var decoder = _utf8.GetDecoder();
            var length = GetLength(ref end);

            var charLength = length * 2;
            var chars = new char[charLength];
            var charIndex = 0;

            var block = _block;
            var index = _index;
            var remaining = length;
            while (true)
            {
                int bytesUsed;
                int charsUsed;
                bool completed;
                var following = block.End - index;
                if (remaining <= following)
                {
                    decoder.Convert(
                        block.Array,
                        index,
                        remaining,
                        chars,
                        charIndex,
                        charLength - charIndex,
                        true,
                        out bytesUsed,
                        out charsUsed,
                        out completed);
                    return new string(chars, 0, charIndex + charsUsed);
                }
                else if (block.Next == null)
                {
                    decoder.Convert(
                        block.Array,
                        index,
                        following,
                        chars,
                        charIndex,
                        charLength - charIndex,
                        true,
                        out bytesUsed,
                        out charsUsed,
                        out completed);
                    return new string(chars, 0, charIndex + charsUsed);
                }
                else
                {
                    decoder.Convert(
                        block.Array,
                        index,
                        following,
                        chars,
                        charIndex,
                        charLength - charIndex,
                        false,
                        out bytesUsed,
                        out charsUsed,
                        out completed);
                    charIndex += charsUsed;
                    remaining -= following;
                    block = block.Next;
                    index = block.Start;
                }
            }
        }

        public ArraySegment<byte> GetArraySegment(ref MemoryPoolIterator2 end, ArraySegment<byte> scratchBuffer)
        {
            if (IsDefault || end.IsDefault)
            {
                return default(ArraySegment<byte>);
            }
            if (end._block == _block)
            {
                return new ArraySegment<byte>(_block.Array, _index, end._index - _index);
            }

            var length = GetLength(ref end);

            if (length < scratchBuffer.Count)
            {
                CopyTo(scratchBuffer.Array, scratchBuffer.Offset, length, out length);
                return new ArraySegment<byte>(scratchBuffer.Array, scratchBuffer.Offset, length);
            }
            else
            {
                var array = new byte[length];
                CopyTo(array, 0, length, out length);
                return new ArraySegment<byte>(array, 0, length);
            }
        }

        public MemoryPoolIterator2 CopyTo(byte[] array, int offset, int count, out int actual)
        {
            if (IsDefault)
            {
                actual = 0;
                return this;
            }

            var block = _block;
            var index = _index;
            var remaining = count;
            while (true)
            {
                var following = block.End - index;
                if (remaining <= following)
                {
                    actual = count;
                    Buffer.BlockCopy(block.Array, index, array, offset, remaining);
                    return new MemoryPoolIterator2(block, index + remaining);
                }
                else if (block.Next == null)
                {
                    actual = count - remaining + following;
                    Buffer.BlockCopy(block.Array, index, array, offset, following);
                    return new MemoryPoolIterator2(block, index + following);
                }
                else
                {
                    Buffer.BlockCopy(block.Array, index, array, offset, following);
                    offset += following;
                    remaining -= following;
                    block = block.Next;
                    index = block.Start;
                }
            }
        }
        public MemoryPoolIterator2 Skip(int limit, out int actual)
        {
            if (IsDefault)
            {
                actual = 0;
                return this;
            }

            var block = _block;
            var index = _index;
            var remaining = limit;
            while (true)
            {
                var following = block.End - index;
                if (remaining <= following)
                {
                    actual = limit;
                    return new MemoryPoolIterator2(block, index + remaining);
                }
                else if (block.Next == null)
                {
                    actual = limit - remaining + following;
                    return new MemoryPoolIterator2(block, index + following);
                }
                else
                {
                    remaining -= following;
                    block = block.Next;
                    index = block.Start;
                }
            }
        }
    }
}
