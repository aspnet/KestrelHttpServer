using System;
using System.Linq;
using System.Numerics;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
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
        public void FindFirstEqualByte()
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
        public void FindFirstEqualByteSlow()
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
            var chars = raw.ToCharArray().Select(c => (byte)c).ToArray();
            Buffer.BlockCopy(chars, 0, block.Array, block.Start, chars.Length);
            block.End += chars.Length;

            var begin = block.GetIterator();
            var searchFor = search.ToCharArray();

            int found = -1;
            if (searchFor.Length == 1)
            {
                var search0 = new Vector<byte>((byte)searchFor[0]);
                found = begin.Seek(ref search0);
            }
            else if (searchFor.Length == 2)
            {
                var search0 = new Vector<byte>((byte)searchFor[0]);
                var search1 = new Vector<byte>((byte)searchFor[1]);
                found = begin.Seek(ref search0, ref search1);
            }
            else if (searchFor.Length == 3)
            {
                var search0 = new Vector<byte>((byte)searchFor[0]);
                var search1 = new Vector<byte>((byte)searchFor[1]);
                var search2 = new Vector<byte>((byte)searchFor[2]);
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
                Assert.True(head.Put((byte)i), $"Fail to put data at {i}.");
            }

            // Can't put anything by the end
            Assert.False(head.Put(0xFF));

            for (var i = 0; i < 4; ++i)
            {
                _pool.Return(blocks[i]);
            }
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
        [InlineData("HTTP/1.0\r", '\r', true, "HTTP/1.0")]
        [InlineData("HTTP/1.1\r", '\r', true, "HTTP/1.1")]
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
        [InlineData("HTTP/1.0\r", '\r')]
        [InlineData("HTTP/1.1\r", '\r')]
        public void KnownVersionsAreReferenceEqual(string input, char endChar)
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
            string knownString1;
            string knownString2;

            // Act
            var result1 = begin1.GetKnownVersion(out knownString1);
            var result2 = begin2.GetKnownVersion(out knownString2);
            // Assert
            Assert.True(result1);
            Assert.True(result2);

            Assert.Equal(knownString1, knownString2);
            Assert.True(ReferenceEquals(knownString1, knownString2));

            _pool.Return(block1);
            _pool.Return(block2);
        }

        [Theory]
        [InlineData("CONNECT / HTTP/1.1", ' ')]
        [InlineData("DELETE / HTTP/1.1", ' ')]
        [InlineData("GET / HTTP/1.1", ' ')]
        [InlineData("HEAD / HTTP/1.1", ' ')]
        [InlineData("PATCH / HTTP/1.1", ' ')]
        [InlineData("POST / HTTP/1.1", ' ')]
        [InlineData("PUT / HTTP/1.1", ' ')]
        [InlineData("OPTIONS / HTTP/1.1", ' ')]
        [InlineData("TRACE / HTTP/1.1", ' ')]
        public void KnownMethodsAreReferenceEqual(string input, char endChar)
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
            string knownString1;
            string knownString2;

            // Act
            var result1 = begin1.GetKnownVersion(out knownString1);
            var result2 = begin2.GetKnownVersion(out knownString2);

            // Assert
            Assert.Equal(knownString1, knownString2);
            Assert.True(ReferenceEquals(knownString1, knownString2));

            _pool.Return(block1);
            _pool.Return(block2);
        }
    }
}