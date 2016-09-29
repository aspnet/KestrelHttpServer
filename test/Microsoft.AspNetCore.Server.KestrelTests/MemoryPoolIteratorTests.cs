// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class MemoryPoolIteratorTests : IDisposable
    {
        private readonly MemoryPool _pool;

        public MemoryPoolIteratorTests()
        {
            _pool = new MemoryPool();
        }

        public void Dispose()
        {
            _pool.Dispose();
        }

        [Fact]
        public void TestFindFirstEqualByte()
        {
            var bytes = Enumerable.Repeat<byte>(0xff, Vector<byte>.Count).ToArray();
            for (int i = 0; i < Vector<byte>.Count; i++)
            {
                Vector<byte> vector = new Vector<byte>(bytes);
                Assert.Equal(i, MemoryPoolIterator.FindFirstEqualByte(ref vector));
                bytes[i] = 0;
            }

            for (int i = 0; i < Vector<byte>.Count; i++)
            {
                bytes[i] = 1;
                Vector<byte> vector = new Vector<byte>(bytes);
                Assert.Equal(i, MemoryPoolIterator.FindFirstEqualByte(ref vector));
                bytes[i] = 0;
            }
        }

        [Fact]
        public void TestFindFirstEqualByteSlow()
        {
            var bytes = Enumerable.Repeat<byte>(0xff, Vector<byte>.Count).ToArray();
            for (int i = 0; i < Vector<byte>.Count; i++)
            {
                Vector<byte> vector = new Vector<byte>(bytes);
                Assert.Equal(i, MemoryPoolIterator.FindFirstEqualByteSlow(ref vector));
                bytes[i] = 0;
            }

            for (int i = 0; i < Vector<byte>.Count; i++)
            {
                bytes[i] = 1;
                Vector<byte> vector = new Vector<byte>(bytes);
                Assert.Equal(i, MemoryPoolIterator.FindFirstEqualByteSlow(ref vector));
                bytes[i] = 0;
            }
        }

        [Theory]
        [InlineData("a", "a", 'a', 0)]
        [InlineData("ab", "a", 'a', 0)]
        [InlineData("aab", "a", 'a', 0)]
        [InlineData("acab", "a", 'a', 0)]
        [InlineData("acab", "c", 'c', 1)]
        [InlineData("abcdefghijklmnopqrstuvwxyz", "lo", 'l', 11)]
        [InlineData("abcdefghijklmnopqrstuvwxyz", "ol", 'l', 11)]
        [InlineData("abcdefghijklmnopqrstuvwxyz", "ll", 'l', 11)]
        [InlineData("abcdefghijklmnopqrstuvwxyz", "lmr", 'l', 11)]
        [InlineData("abcdefghijklmnopqrstuvwxyz", "rml", 'l', 11)]
        [InlineData("abcdefghijklmnopqrstuvwxyz", "mlr", 'l', 11)]
        [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz", "lmr", 'l', 11)]
        [InlineData("aaaaaaaaaaalmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz", "lmr", 'l', 11)]
        [InlineData("aaaaaaaaaaacmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz", "lmr", 'm', 12)]
        [InlineData("aaaaaaaaaaarmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz", "lmr", 'r', 11)]
        [InlineData("/localhost:5000/PATH/%2FPATH2/ HTTP/1.1", " %?", '%', 21)]
        [InlineData("/localhost:5000/PATH/%2FPATH2/?key=value HTTP/1.1", " %?", '%', 21)]
        [InlineData("/localhost:5000/PATH/PATH2/?key=value HTTP/1.1", " %?", '?', 27)]
        [InlineData("/localhost:5000/PATH/PATH2/ HTTP/1.1", " %?", ' ', 27)]
        public void MemorySeek(string raw, string search, char expectResult, int expectIndex)
        {
            var block = _pool.Lease();
            var chars = raw.ToCharArray().Select(c => (byte) c).ToArray();
            Buffer.BlockCopy(chars, 0, block.Array, block.Start, chars.Length);
            block.End += chars.Length;

            var begin = block.GetIterator();
            var searchFor = search.ToCharArray();

            int found = -1;
            if (searchFor.Length == 1)
            {
                var search0 = new Vector<byte>((byte) searchFor[0]);
                found = begin.Seek(ref search0);
            }
            else if (searchFor.Length == 2)
            {
                var search0 = new Vector<byte>((byte) searchFor[0]);
                var search1 = new Vector<byte>((byte) searchFor[1]);
                found = begin.Seek(ref search0, ref search1);
            }
            else if (searchFor.Length == 3)
            {
                var search0 = new Vector<byte>((byte) searchFor[0]);
                var search1 = new Vector<byte>((byte) searchFor[1]);
                var search2 = new Vector<byte>((byte) searchFor[2]);
                found = begin.Seek(ref search0, ref search1, ref search2);
            }
            else
            {
                Assert.False(true, "Invalid test sample.");
            }

            Assert.Equal(expectResult, found);
            Assert.Equal(expectIndex, begin.Index - block.Start);

            _pool.Return(block);
        }

        [Fact]
        public void Put()
        {
            var blocks = new MemoryPoolBlock[4];
            for (var i = 0; i < 4; ++i)
            {
                blocks[i] = _pool.Lease();
                blocks[i].End += 16;

                for (var j = 0; j < blocks.Length; ++j)
                {
                    blocks[i].Array[blocks[i].Start + j] = 0x00;
                }

                if (i != 0)
                {
                    blocks[i - 1].Next = blocks[i];
                }
            }

            // put FF at first block's head
            var head = blocks[0].GetIterator();
            Assert.True(head.Put(0xFF));

            // data is put at correct position
            Assert.Equal(0xFF, blocks[0].Array[blocks[0].Start]);
            Assert.Equal(0x00, blocks[0].Array[blocks[0].Start + 1]);

            // iterator is moved to next byte after put
            Assert.Equal(1, head.Index - blocks[0].Start);

            for (var i = 0; i < 14; ++i)
            {
                // move itr to the end of the block 0
                head.Take();
            }

            // write to the end of block 0
            Assert.True(head.Put(0xFE));
            Assert.Equal(0xFE, blocks[0].Array[blocks[0].End - 1]);
            Assert.Equal(0x00, blocks[1].Array[blocks[1].Start]);

            // put data across the block link
            Assert.True(head.Put(0xFD));
            Assert.Equal(0xFD, blocks[1].Array[blocks[1].Start]);
            Assert.Equal(0x00, blocks[1].Array[blocks[1].Start + 1]);

            // paint every block
            head = blocks[0].GetIterator();
            for (var i = 0; i < 64; ++i)
            {
                Assert.True(head.Put((byte) i), $"Fail to put data at {i}.");
            }

            // Can't put anything by the end
            Assert.False(head.Put(0xFF));

            for (var i = 0; i < 4; ++i)
            {
                _pool.Return(blocks[i]);
            }
        }

        [Fact]
        public void PeekArraySegment()
        {
            // Arrange
            var block = _pool.Lease();
            var bytes = new byte[] {0, 1, 2, 3, 4, 5, 6, 7};
            Buffer.BlockCopy(bytes, 0, block.Array, block.Start, bytes.Length);
            block.End += bytes.Length;
            var scan = block.GetIterator();
            var originalIndex = scan.Index;

            // Act
            var result = scan.PeekArraySegment();

            // Assert
            Assert.Equal(new byte[] {0, 1, 2, 3, 4, 5, 6, 7}, result);
            Assert.Equal(originalIndex, scan.Index);

            _pool.Return(block);
        }

        [Fact]
        public void PeekArraySegmentOnDefaultIteratorReturnsDefaultArraySegment()
        {
            // Assert.Equals doesn't work since xunit tries to access the underlying array.
            Assert.True(default(ArraySegment<byte>).Equals(default(MemoryPoolIterator).PeekArraySegment()));
        }

        [Fact]
        public void PeekArraySegmentAtEndOfDataReturnsDefaultArraySegment()
        {
            // Arrange
            var block = _pool.Lease();
            var bytes = new byte[] {0, 1, 2, 3, 4, 5, 6, 7};
            Buffer.BlockCopy(bytes, 0, block.Array, block.Start, bytes.Length);
            block.End += bytes.Length;
            block.Start = block.End;

            var scan = block.GetIterator();

            // Act
            var result = scan.PeekArraySegment();

            // Assert
            // Assert.Equals doesn't work since xunit tries to access the underlying array.
            Assert.True(default(ArraySegment<byte>).Equals(result));

            _pool.Return(block);
        }

        [Fact]
        public void PeekArraySegmentAtBlockBoundary()
        {
            // Arrange
            var firstBlock = _pool.Lease();
            var lastBlock = _pool.Lease();

            var firstBytes = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
            var lastBytes = new byte[] { 8, 9, 10, 11, 12, 13, 14, 15 };

            Buffer.BlockCopy(firstBytes, 0, firstBlock.Array, firstBlock.Start, firstBytes.Length);
            firstBlock.End += lastBytes.Length;

            firstBlock.Next = lastBlock;
            Buffer.BlockCopy(lastBytes, 0, lastBlock.Array, lastBlock.Start, lastBytes.Length);
            lastBlock.End += lastBytes.Length;

            var scan = firstBlock.GetIterator();
            var originalIndex = scan.Index;
            var originalBlock = scan.Block;

            // Act
            var result = scan.PeekArraySegment();

            // Assert
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }, result);
            Assert.Equal(originalBlock, scan.Block);
            Assert.Equal(originalIndex, scan.Index);

            // Act
            // Advance past the data in the first block
            scan.Skip(8);
            result = scan.PeekArraySegment();

            // Assert
            Assert.Equal(new byte[] { 8, 9, 10, 11, 12, 13, 14, 15 }, result);
            Assert.Equal(originalBlock, scan.Block);
            Assert.Equal(originalIndex + 8, scan.Index);

            // Act
            // Add anther empty block between the first and last block
            var middleBlock = _pool.Lease();
            firstBlock.Next = middleBlock;
            middleBlock.Next = lastBlock;
            result = scan.PeekArraySegment();

            // Assert
            Assert.Equal(new byte[] { 8, 9, 10, 11, 12, 13, 14, 15 }, result);
            Assert.Equal(originalBlock, scan.Block);
            Assert.Equal(originalIndex + 8, scan.Index);

            _pool.Return(firstBlock);
            _pool.Return(middleBlock);
            _pool.Return(lastBlock);
        }

        [Fact]
        public void PeekLong()
        {
            // Arrange
            var block = _pool.Lease();
            var bytes = BitConverter.GetBytes(0x0102030405060708);
            Buffer.BlockCopy(bytes, 0, block.Array, block.Start, bytes.Length);
            block.End += bytes.Length;
            var scan = block.GetIterator();
            var originalIndex = scan.Index;

            // Act
            var result = scan.PeekLong();

            // Assert
            Assert.Equal(0x0102030405060708, result);
            Assert.Equal(originalIndex, scan.Index);

            _pool.Return(block);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        public void PeekLongAtBlockBoundary(int blockBytes)
        {
            // Arrange
            var nextBlockBytes = 8 - blockBytes;

            var block = _pool.Lease();
            block.End += blockBytes;

            var nextBlock = _pool.Lease();
            nextBlock.End += nextBlockBytes;

            block.Next = nextBlock;

            var bytes = BitConverter.GetBytes(0x0102030405060708);
            Buffer.BlockCopy(bytes, 0, block.Array, block.Start, blockBytes);
            Buffer.BlockCopy(bytes, blockBytes, nextBlock.Array, nextBlock.Start, nextBlockBytes);

            var scan = block.GetIterator();
            var originalIndex = scan.Index;

            // Act
            var result = scan.PeekLong();

            // Assert
            Assert.Equal(0x0102030405060708, result);
            Assert.Equal(originalIndex, scan.Index);

            _pool.Return(block);
            _pool.Return(nextBlock);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        public void SkipAtBlockBoundary(int blockBytes)
        {
            // Arrange
            var nextBlockBytes = 10 - blockBytes;

            var block = _pool.Lease();
            block.End += blockBytes;

            var nextBlock = _pool.Lease();
            nextBlock.End += nextBlockBytes;

            block.Next = nextBlock;

            var bytes = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            Buffer.BlockCopy(bytes, 0, block.Array, block.Start, blockBytes);
            Buffer.BlockCopy(bytes, blockBytes, nextBlock.Array, nextBlock.Start, nextBlockBytes);

            var scan = block.GetIterator();
            var originalIndex = scan.Index;

            // Act
            scan.Skip(8);
            var result = scan.Take();

            // Assert
            Assert.Equal(0x08, result);
            Assert.NotEqual(originalIndex, scan.Index);

            _pool.Return(block);
            _pool.Return(nextBlock);
        }

        [Fact]
        public void SkipThrowsWhenSkippingMoreBytesThanAvailableInSingleBlock()
        {
            // Arrange
            var block = _pool.Lease();
            block.End += 5;

            var scan = block.GetIterator();

            // Act/Assert
            Assert.ThrowsAny<InvalidOperationException>(() => scan.Skip(8));

            _pool.Return(block);
        }

        [Fact]
        public void SkipThrowsWhenSkippingMoreBytesThanAvailableInMultipleBlocks()
        {
            // Arrange
            var block = _pool.Lease();
            block.End += 3;

            var nextBlock = _pool.Lease();
            nextBlock.End += 2;
            block.Next = nextBlock;

            var scan = block.GetIterator();

            // Act/Assert
            Assert.ThrowsAny<InvalidOperationException>(() => scan.Skip(8));

            _pool.Return(block);
            _pool.Return(nextBlock);
        }

        [Theory]
        [InlineData("CONNECT / HTTP/1.1", ' ', true, "CONNECT")]
        [InlineData("DELETE / HTTP/1.1", ' ', true, "DELETE")]
        [InlineData("GET / HTTP/1.1", ' ', true, "GET")]
        [InlineData("HEAD / HTTP/1.1", ' ', true, "HEAD")]
        [InlineData("PATCH / HTTP/1.1", ' ', true, "PATCH")]
        [InlineData("POST / HTTP/1.1", ' ', true, "POST")]
        [InlineData("PUT / HTTP/1.1", ' ', true, "PUT")]
        [InlineData("OPTIONS / HTTP/1.1", ' ', true, "OPTIONS")]
        [InlineData("TRACE / HTTP/1.1", ' ', true, "TRACE")]
        [InlineData("GET/ HTTP/1.1", ' ', false, null)]
        [InlineData("get / HTTP/1.1", ' ', false, null)]
        [InlineData("GOT / HTTP/1.1", ' ', false, null)]
        [InlineData("ABC / HTTP/1.1", ' ', false, null)]
        [InlineData("PO / HTTP/1.1", ' ', false, null)]
        [InlineData("PO ST / HTTP/1.1", ' ', false, null)]
        [InlineData("short ", ' ', false, null)]
        public void GetsKnownMethod(string input, char endChar, bool expectedResult, string expectedKnownString)
        {
            // Arrange
            var block = _pool.Lease();
            var chars = input.ToCharArray().Select(c => (byte)c).ToArray();
            Buffer.BlockCopy(chars, 0, block.Array, block.Start, chars.Length);
            block.End += chars.Length;
            var begin = block.GetIterator();
            string knownString;

            // Act
            var result = begin.GetKnownMethod(out knownString);

            // Assert
            Assert.Equal(expectedResult, result);
            Assert.Equal(expectedKnownString, knownString);

            _pool.Return(block);
        }

        [Theory]
        [InlineData("HTTP/1.0\r", '\r', true, MemoryPoolIteratorExtensions.Http10Version)]
        [InlineData("HTTP/1.1\r", '\r', true, MemoryPoolIteratorExtensions.Http11Version)]
        [InlineData("HTTP/3.0\r", '\r', false, null)]
        [InlineData("http/1.0\r", '\r', false, null)]
        [InlineData("http/1.1\r", '\r', false, null)]
        [InlineData("short ", ' ', false, null)]
        public void GetsKnownVersion(string input, char endChar, bool expectedResult, string expectedKnownString)
        {
            // Arrange
            var block = _pool.Lease();
            var chars = input.ToCharArray().Select(c => (byte)c).ToArray();
            Buffer.BlockCopy(chars, 0, block.Array, block.Start, chars.Length);
            block.End += chars.Length;
            var begin = block.GetIterator();
            string knownString;

            // Act
            var result = begin.GetKnownVersion(out knownString);
            // Assert
            Assert.Equal(expectedResult, result);
            Assert.Equal(expectedKnownString, knownString);

            _pool.Return(block);
        }

        [Theory]
        [InlineData("HTTP/1.0\r", "HTTP/1.0")]
        [InlineData("HTTP/1.1\r", "HTTP/1.1")]
        public void KnownVersionsAreInterned(string input, string expected)
        {
            TestKnownStringsInterning(input, expected, MemoryPoolIteratorExtensions.GetKnownVersion);
        }

        [Theory]
        [InlineData("CONNECT / HTTP/1.1", "CONNECT")]
        [InlineData("DELETE / HTTP/1.1", "DELETE")]
        [InlineData("GET / HTTP/1.1", "GET")]
        [InlineData("HEAD / HTTP/1.1", "HEAD")]
        [InlineData("PATCH / HTTP/1.1", "PATCH")]
        [InlineData("POST / HTTP/1.1", "POST")]
        [InlineData("PUT / HTTP/1.1", "PUT")]
        [InlineData("OPTIONS / HTTP/1.1", "OPTIONS")]
        [InlineData("TRACE / HTTP/1.1", "TRACE")]
        public void KnownMethodsAreInterned(string input, string expected)
        {
            TestKnownStringsInterning(input, expected, MemoryPoolIteratorExtensions.GetKnownMethod);
        }

        [Theory]
        [MemberData(nameof(SeekByteLimitData))]
        public void TestSeekByteLimitWithinSameBlock(string input, char seek, int limit, int expectedBytesScanned, int expectedReturnValue)
        {
            MemoryPoolBlock block = null;

            try
            {
                // Arrange
                var seekVector = new Vector<byte>((byte)seek);

                block = _pool.Lease();
                var chars = input.ToString().ToCharArray().Select(c => (byte)c).ToArray();
                Buffer.BlockCopy(chars, 0, block.Array, block.Start, chars.Length);
                block.End += chars.Length;
                var scan = block.GetIterator();

                // Act
                int bytesScanned;
                var returnValue = scan.Seek(ref seekVector, out bytesScanned, limit);

                // Assert
                Assert.Equal(expectedBytesScanned, bytesScanned);
                Assert.Equal(expectedReturnValue, returnValue);

                Assert.Same(block, scan.Block);
                var expectedEndIndex = expectedReturnValue != -1 ?
                    block.Start + input.IndexOf(seek) :
                    block.Start + expectedBytesScanned;
                Assert.Equal(expectedEndIndex, scan.Index);
            }
            finally
            {
                // Cleanup
                if (block != null) _pool.Return(block);
            }
        }

        [Theory]
        [MemberData(nameof(SeekByteLimitData))]
        public void TestSeekByteLimitAcrossBlocks(string input, char seek, int limit, int expectedBytesScanned, int expectedReturnValue)
        {
            MemoryPoolBlock block1 = null;
            MemoryPoolBlock block2 = null;
            MemoryPoolBlock emptyBlock = null;

            try
            {
                // Arrange
                var seekVector = new Vector<byte>((byte)seek);

                var input1 = input.Substring(0, input.Length / 2);
                block1 = _pool.Lease();
                var chars1 = input1.ToCharArray().Select(c => (byte)c).ToArray();
                Buffer.BlockCopy(chars1, 0, block1.Array, block1.Start, chars1.Length);
                block1.End += chars1.Length;

                emptyBlock = _pool.Lease();
                block1.Next = emptyBlock;

                var input2 = input.Substring(input.Length / 2);
                block2 = _pool.Lease();
                var chars2 = input2.ToCharArray().Select(c => (byte)c).ToArray();
                Buffer.BlockCopy(chars2, 0, block2.Array, block2.Start, chars2.Length);
                block2.End += chars2.Length;
                emptyBlock.Next = block2;

                var scan = block1.GetIterator();

                // Act
                int bytesScanned;
                var returnValue = scan.Seek(ref seekVector, out bytesScanned, limit);

                // Assert
                Assert.Equal(expectedBytesScanned, bytesScanned);
                Assert.Equal(expectedReturnValue, returnValue);

                var seekCharIndex = input.IndexOf(seek);
                var expectedEndBlock = limit <= input.Length / 2 ?
                    block1 :
                    (seekCharIndex != -1 && seekCharIndex < input.Length / 2 ? block1 : block2);
                Assert.Same(expectedEndBlock, scan.Block);
                var expectedEndIndex = expectedReturnValue != -1 ?
                    expectedEndBlock.Start + (expectedEndBlock == block1 ? input1.IndexOf(seek) : input2.IndexOf(seek)) :
                    expectedEndBlock.Start + (expectedEndBlock == block1 ? expectedBytesScanned : expectedBytesScanned - (input.Length / 2));
                Assert.Equal(expectedEndIndex, scan.Index);
            }
            finally
            {
                // Cleanup
                if (block1 != null) _pool.Return(block1);
                if (emptyBlock != null) _pool.Return(emptyBlock);
                if (block2 != null) _pool.Return(block2);
            }
        }

        [Theory]
        [MemberData(nameof(SeekIteratorLimitData))]
        public void TestSeekIteratorLimitWithinSameBlock(string input, char seek, char limitAt, int expectedReturnValue)
        {
            MemoryPoolBlock block = null;

            try
            {
                // Arrange
                var seekVector = new Vector<byte>((byte)seek);
                var limitAtVector = new Vector<byte>((byte)limitAt);
                var afterSeekVector = new Vector<byte>((byte)'B');

                block = _pool.Lease();
                var chars = input.ToCharArray().Select(c => (byte)c).ToArray();
                Buffer.BlockCopy(chars, 0, block.Array, block.Start, chars.Length);
                block.End += chars.Length;
                var scan1 = block.GetIterator();
                var scan2_1 = scan1;
                var scan2_2 = scan1;
                var scan3_1 = scan1;
                var scan3_2 = scan1;
                var scan3_3 = scan1;
                var end = scan1;

                // Act
                var endReturnValue = end.Seek(ref limitAtVector);
                var returnValue1 = scan1.Seek(ref seekVector, ref end);
                var returnValue2_1 = scan2_1.Seek(ref seekVector, ref afterSeekVector, ref end);
                var returnValue2_2 = scan2_2.Seek(ref afterSeekVector, ref seekVector, ref end);
                var returnValue3_1 = scan3_1.Seek(ref seekVector, ref afterSeekVector, ref afterSeekVector, ref end);
                var returnValue3_2 = scan3_2.Seek(ref afterSeekVector, ref seekVector, ref afterSeekVector, ref end);
                var returnValue3_3 = scan3_3.Seek(ref afterSeekVector, ref afterSeekVector, ref seekVector, ref end);

                // Assert
                Assert.Equal(input.Contains(limitAt) ? limitAt : -1, endReturnValue);
                Assert.Equal(expectedReturnValue, returnValue1);
                Assert.Equal(expectedReturnValue, returnValue2_1);
                Assert.Equal(expectedReturnValue, returnValue2_2);
                Assert.Equal(expectedReturnValue, returnValue3_1);
                Assert.Equal(expectedReturnValue, returnValue3_2);
                Assert.Equal(expectedReturnValue, returnValue3_3);

                Assert.Same(block, scan1.Block);
                Assert.Same(block, scan2_1.Block);
                Assert.Same(block, scan2_2.Block);
                Assert.Same(block, scan3_1.Block);
                Assert.Same(block, scan3_2.Block);
                Assert.Same(block, scan3_3.Block);

                var expectedEndIndex = expectedReturnValue != -1 ? block.Start + input.IndexOf(seek) : end.Index;
                Assert.Equal(expectedEndIndex, scan1.Index);
                Assert.Equal(expectedEndIndex, scan2_1.Index);
                Assert.Equal(expectedEndIndex, scan2_2.Index);
                Assert.Equal(expectedEndIndex, scan3_1.Index);
                Assert.Equal(expectedEndIndex, scan3_2.Index);
                Assert.Equal(expectedEndIndex, scan3_3.Index);
            }
            finally
            {
                // Cleanup
                if (block != null) _pool.Return(block);
            }
        }

        [Theory]
        [MemberData(nameof(SeekIteratorLimitData))]
        public void TestSeekIteratorLimitAcrossBlocks(string input, char seek, char limitAt, int expectedReturnValue)
        {
            MemoryPoolBlock block1 = null;
            MemoryPoolBlock block2 = null;
            MemoryPoolBlock emptyBlock = null;

            try
            {
                // Arrange
                var seekVector = new Vector<byte>((byte)seek);
                var limitAtVector = new Vector<byte>((byte)limitAt);
                var afterSeekVector = new Vector<byte>((byte)'B');

                var input1 = input.Substring(0, input.Length / 2);
                block1 = _pool.Lease();
                var chars1 = input1.ToCharArray().Select(c => (byte)c).ToArray();
                Buffer.BlockCopy(chars1, 0, block1.Array, block1.Start, chars1.Length);
                block1.End += chars1.Length;

                emptyBlock = _pool.Lease();
                block1.Next = emptyBlock;

                var input2 = input.Substring(input.Length / 2);
                block2 = _pool.Lease();
                var chars2 = input2.ToCharArray().Select(c => (byte)c).ToArray();
                Buffer.BlockCopy(chars2, 0, block2.Array, block2.Start, chars2.Length);
                block2.End += chars2.Length;
                emptyBlock.Next = block2;

                var scan1 = block1.GetIterator();
                var scan2_1 = scan1;
                var scan2_2 = scan1;
                var scan3_1 = scan1;
                var scan3_2 = scan1;
                var scan3_3 = scan1;
                var end = scan1;

                // Act
                var endReturnValue = end.Seek(ref limitAtVector);
                var returnValue1 = scan1.Seek(ref seekVector, ref end);
                var returnValue2_1 = scan2_1.Seek(ref seekVector, ref afterSeekVector, ref end);
                var returnValue2_2 = scan2_2.Seek(ref afterSeekVector, ref seekVector, ref end);
                var returnValue3_1 = scan3_1.Seek(ref seekVector, ref afterSeekVector, ref afterSeekVector, ref end);
                var returnValue3_2 = scan3_2.Seek(ref afterSeekVector, ref seekVector, ref afterSeekVector, ref end);
                var returnValue3_3 = scan3_3.Seek(ref afterSeekVector, ref afterSeekVector, ref seekVector, ref end);

                // Assert
                Assert.Equal(input.Contains(limitAt) ? limitAt : -1, endReturnValue);
                Assert.Equal(expectedReturnValue, returnValue1);
                Assert.Equal(expectedReturnValue, returnValue2_1);
                Assert.Equal(expectedReturnValue, returnValue2_2);
                Assert.Equal(expectedReturnValue, returnValue3_1);
                Assert.Equal(expectedReturnValue, returnValue3_2);
                Assert.Equal(expectedReturnValue, returnValue3_3);

                var seekCharIndex = input.IndexOf(seek);
                var limitAtIndex = input.IndexOf(limitAt);
                var expectedEndBlock = seekCharIndex != -1 && seekCharIndex < input.Length / 2 ?
                    block1 :
                    (limitAtIndex != -1 && limitAtIndex < input.Length / 2 ? block1 : block2);
                Assert.Same(expectedEndBlock, scan1.Block);
                Assert.Same(expectedEndBlock, scan2_1.Block);
                Assert.Same(expectedEndBlock, scan2_2.Block);
                Assert.Same(expectedEndBlock, scan3_1.Block);
                Assert.Same(expectedEndBlock, scan3_2.Block);
                Assert.Same(expectedEndBlock, scan3_3.Block);

                var expectedEndIndex = expectedReturnValue != -1 ?
                    expectedEndBlock.Start + (expectedEndBlock == block1 ? input1.IndexOf(seek) : input2.IndexOf(seek)) :
                    end.Index;
                Assert.Equal(expectedEndIndex, scan1.Index);
                Assert.Equal(expectedEndIndex, scan2_1.Index);
                Assert.Equal(expectedEndIndex, scan2_2.Index);
                Assert.Equal(expectedEndIndex, scan3_1.Index);
                Assert.Equal(expectedEndIndex, scan3_2.Index);
                Assert.Equal(expectedEndIndex, scan3_3.Index);
            }
            finally
            {
                // Cleanup
                if (block1 != null) _pool.Return(block1);
                if (emptyBlock != null) _pool.Return(emptyBlock);
                if (block2 != null) _pool.Return(block2);
            }
        }

        private delegate bool GetKnownString(MemoryPoolIterator iter, out string result);

        private void TestKnownStringsInterning(string input, string expected, GetKnownString action)
        {
            // Arrange
            var chars = input.ToCharArray().Select(c => (byte)c).ToArray();
            var block1 = _pool.Lease();
            var block2 = _pool.Lease();
            Buffer.BlockCopy(chars, 0, block1.Array, block1.Start, chars.Length);
            Buffer.BlockCopy(chars, 0, block2.Array, block2.Start, chars.Length);
            block1.End += chars.Length;
            block2.End += chars.Length;
            var begin1 = block1.GetIterator();
            var begin2 = block2.GetIterator();

            // Act
            string knownString1, knownString2;
            var result1 = action(begin1, out knownString1);
            var result2 = action(begin2, out knownString2);

            _pool.Return(block1);
            _pool.Return(block2);

            // Assert
            Assert.True(result1);
            Assert.True(result2);
            Assert.Equal(knownString1, expected);
            Assert.Same(knownString1, knownString2);
        }

        public static IEnumerable<object[]> SeekByteLimitData
        {
            get
            {
                var vectorSpan = Vector<byte>.Count;

                // string input, char seek, int limit, int expectedBytesScanned, int expectedReturnValue
                var data = new List<object[]>();

                // Non-vector inputs

                data.Add(new object[] { "hello, world", 'h', 12, 1, 'h' });
                data.Add(new object[] { "hello, world", ' ', 12, 7, ' ' });
                data.Add(new object[] { "hello, world", 'd', 12, 12, 'd' });
                data.Add(new object[] { "hello, world", '!', 12, 12, -1 });
                data.Add(new object[] { "hello, world", 'h', 13, 1, 'h' });
                data.Add(new object[] { "hello, world", ' ', 13, 7, ' ' });
                data.Add(new object[] { "hello, world", 'd', 13, 12, 'd' });
                data.Add(new object[] { "hello, world", '!', 13, 12, -1 });
                data.Add(new object[] { "hello, world", 'h', 5, 1, 'h' });
                data.Add(new object[] { "hello, world", 'o', 5, 5, 'o' });
                data.Add(new object[] { "hello, world", ',', 5, 5, -1 });
                data.Add(new object[] { "hello, world", 'd', 5, 5, -1 });
                data.Add(new object[] { "abba", 'a', 4, 1, 'a' });
                data.Add(new object[] { "abba", 'b', 4, 2, 'b' });

                // Vector inputs

                // Single vector, no seek char in input, expect failure
                data.Add(new object[] { new string('a', vectorSpan), 'b', vectorSpan, vectorSpan, -1 });
                // Two vectors, no seek char in input, expect failure
                data.Add(new object[] { new string('a', vectorSpan * 2), 'b', vectorSpan * 2, vectorSpan * 2, -1 });
                // Two vectors plus non vector length (thus hitting slow path too), no seek char in input, expect failure
                data.Add(new object[] { new string('a', vectorSpan * 2 + vectorSpan / 2), 'b', vectorSpan * 2 + vectorSpan / 2, vectorSpan * 2 + vectorSpan / 2, -1 });

                // For each input length from 1/2 to 3 1/2 vector spans in 1/2 vector span increments...
                for (var length = vectorSpan / 2; length <= vectorSpan * 3 + vectorSpan / 2; length += vectorSpan / 2)
                {
                    // ...place the seek char at vector and input boundaries...
                    for (var i = Math.Min(vectorSpan - 1, length - 1); i < length; i += ((i + 1) % vectorSpan == 0) ? 1 : Math.Min(i + (vectorSpan - 1), length - 1))
                    {
                        var input = new StringBuilder(new string('a', length));
                        input[i] = 'b';

                        // ...and check with a seek byte limit before, at, and past the seek char position...
                        for (var limitOffset = -1; limitOffset <= 1; limitOffset++)
                        {
                            var limit = (i + 1) + limitOffset;

                            if (limit >= i + 1)
                            {
                                // ...that Seek() succeeds when the seek char is within that limit...
                                data.Add(new object[] { input.ToString(), 'b', limit, i + 1, 'b' });
                            }
                            else
                            {
                                // ...and fails when it's not.
                                data.Add(new object[] { input.ToString(), 'b', limit, Math.Min(length, limit), -1 });
                            }
                        }
                    }
                }

                return data;
            }
        }

        public static IEnumerable<object[]> SeekIteratorLimitData
        {
            get
            {
                var vectorSpan = Vector<byte>.Count;

                // string input, char seek, char limitAt, int expectedReturnValue
                var data = new List<object[]>();

                // Non-vector inputs

                data.Add(new object[] { "hello, world", 'h', 'd', 'h' });
                data.Add(new object[] { "hello, world", ' ', 'd', ' ' });
                data.Add(new object[] { "hello, world", 'd', 'd', 'd' });
                data.Add(new object[] { "hello, world", '!', 'd', -1 });
                data.Add(new object[] { "hello, world", 'h', 'w', 'h' });
                data.Add(new object[] { "hello, world", 'o', 'w', 'o' });
                data.Add(new object[] { "hello, world", 'r', 'w', -1 });
                data.Add(new object[] { "hello, world", 'd', 'w', -1 });

                // Vector inputs

                // Single vector, no seek char in input, expect failure
                data.Add(new object[] { new string('a', vectorSpan), 'b', 'b', -1 });
                // Two vectors, no seek char in input, expect failure
                data.Add(new object[] { new string('a', vectorSpan * 2), 'b', 'b', -1 });
                // Two vectors plus non vector length (thus hitting slow path too), no seek char in input, expect failure
                data.Add(new object[] { new string('a', vectorSpan * 2 + vectorSpan / 2), 'b', 'b', -1 });

                // For each input length from 1/2 to 3 1/2 vector spans in 1/2 vector span increments...
                for (var length = vectorSpan / 2; length <= vectorSpan * 3 + vectorSpan / 2; length += vectorSpan / 2)
                {
                    // ...place the seek char at vector and input boundaries...
                    for (var i = Math.Min(vectorSpan - 1, length - 1); i < length; i += ((i + 1) % vectorSpan == 0) ? 1 : Math.Min(i + (vectorSpan - 1), length - 1))
                    {
                        var input = new StringBuilder(new string('a', length));
                        input[i] = 'b';

                        // ...along with sentinel characters to seek the limit iterator to...
                        input[i - 1] = 'A';
                        if (i < length - 1) input[i + 1] = 'B';

                        // ...and check that Seek() succeeds with a limit iterator at or past the seek char position...
                        data.Add(new object[] { input.ToString(), 'b', 'b', 'b' });
                        if (i < length - 1) data.Add(new object[] { input.ToString(), 'b', 'B', 'b' });

                        // ...and fails with a limit iterator before the seek char position.
                        data.Add(new object[] { input.ToString(), 'b', 'A', -1 });
                    }
                }

                return data;
            }
        }
    }
}