// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class HPackDecoderTests
    {
        // TODO: verify dynamic table state after decoding

        private static readonly byte[] _newHeaderBytes = Encoding.ASCII.GetBytes("new-header");

        // n     e     w       -      h     e     a     d     e     r      *
        // 10101000 10111110 00010110 10011100 10100011 10010000 10110110 01111111
        private static readonly byte[] _newHeaderHuffmanBytes = new byte[] { 0xa8, 0xbe, 0x16, 0x9c, 0xa3, 0x90, 0xb6, 0x7f };

        private static readonly byte[] _valueBytes = Encoding.ASCII.GetBytes("value");

        // v      a     l      u      e    *
        // 11101110 00111010 00101101 00101111
        private static readonly byte[] _valueHuffmanBytes = new byte [] { 0xee, 0x3a, 0x2d, 0x2f };

        private static readonly byte[] _newName = new byte[] { (byte)_newHeaderBytes.Length }
            .Concat(_newHeaderBytes)
            .Concat(new byte[] { (byte)_valueBytes.Length })
            .Concat(_valueBytes)
            .ToArray();

        private static readonly byte[] _newNameHuffmanName = new byte[] { (byte)(0x80 | _newHeaderHuffmanBytes.Length) }
            .Concat(_newHeaderHuffmanBytes)
            .Concat(new byte[] { (byte)_valueBytes.Length })
            .Concat(_valueBytes)
            .ToArray();

        private static readonly byte[] _newNameHuffmanValue = new byte[] { (byte)_newHeaderBytes.Length }
            .Concat(_newHeaderBytes)
            .Concat(new byte[] { (byte)(0x80 | _valueHuffmanBytes.Length) })
            .Concat(_valueHuffmanBytes)
            .ToArray();

        private static readonly byte[] _newNameHuffmanNameAndValue = new byte[] { (byte)(0x80 | _newHeaderHuffmanBytes.Length) }
            .Concat(_newHeaderHuffmanBytes)
            .Concat(new byte[] { (byte)(0x80 | _valueHuffmanBytes.Length) })
            .Concat(_valueHuffmanBytes)
            .ToArray();

        private static readonly byte[] _indexedName = new byte[] { (byte)_valueBytes.Length }
            .Concat(_valueBytes)
            .ToArray();

        private static readonly byte[] _indexedNameHuffmanValue = new byte[] { (byte)(0x80 | _valueHuffmanBytes.Length) }
            .Concat(_valueHuffmanBytes)
            .ToArray();

        // Indexed Header Field Representation - Static Table - ":method: GET"
        private static readonly byte[] _indexedHeaderStatic = new byte[] { 0x82 };

        // Indexed Header Field Representation - Dynamic Table - Index 62 (first element in dynamic table)
        private static readonly byte[] _indexedHeaderDynamic = new byte[] { 0xbe };

        // Literal Header Field with Incremental Indexing Representation - New Name - "new-header: value"
        private static readonly byte[] _literalHeaderFieldWithIndexingNewName = new byte[] { 0x40 }
            .Concat(_newName)
            .ToArray();

        // Literal Header Field with Incremental Indexing Representation - New Name - "new-header: value" - Huffman encoded name
        private static readonly byte[] _literalHeaderFieldWithIndexingNewNameHuffmanName = new byte[] { 0x40 }
            .Concat(_newNameHuffmanName)
            .ToArray();

        // Literal Header Field with Incremental Indexing Representation - New Name - "new-header: value" - Huffman encoded value
        private static readonly byte[] _literalHeaderFieldWithIndexingNewNameHuffmanValue = new byte[] { 0x40 }
            .Concat(_newNameHuffmanValue)
            .ToArray();

        // Literal Header Field with Incremental Indexing Representation - New Name - "new-header: value" - Huffman encoded name and value
        private static readonly byte[] _literalHeaderFieldWithIndexingNewNameHuffmanNameAndValue = new byte[] { 0x40 }
            .Concat(_newNameHuffmanNameAndValue)
            .ToArray();

        // Literal Header Field with Incremental Indexing Representation - Indexed Name - "user-agent: value"
        private static readonly byte[] _literalHeaderFieldWithIndexingIndexedName = new byte[] { 0x7a }
            .Concat(_indexedName)
            .ToArray();

        // Literal Header Field with Incremental Indexing Representation - Indexed Name - "user-agent: value" - Huffman encoded value
        private static readonly byte[] _literalHeaderFieldWithIndexingIndexedNameHuffmanValue = new byte[] { 0x7a }
            .Concat(_indexedNameHuffmanValue)
            .ToArray();

        // Literal Header Field with Incremental Indexing Representation - Indexed Name - Index 62 (first index in dynamic table)
        private static readonly byte[] _literalHeaderFieldWithIndexingIndexedNameIndex62 = new byte[] { 0x7e }
            .Concat(_indexedName)
            .ToArray();

        // Literal Header Field without Indexing Representation - New Name - "new-header: value"
        private static readonly byte[] _literalHeaderFieldWithoutIndexingNewName = new byte[] { 0x00 }
            .Concat(_newName)
            .ToArray();

        // Literal Header Field without Indexing Representation - New Name - "new-header: value" - Huffman encoded name
        private static readonly byte[] _literalHeaderFieldWithoutIndexingNewNameHuffmanName = new byte[] { 0x00 }
            .Concat(_newNameHuffmanName)
            .ToArray();

        // Literal Header Field without Indexing Representation - New Name - "new-header: value" - Huffman encoded value
        private static readonly byte[] _literalHeaderFieldWithoutIndexingNewNameHuffmanValue = new byte[] { 0x00 }
            .Concat(_newNameHuffmanValue)
            .ToArray();

        // Literal Header Field without Indexing Representation - New Name - "new-header: value" - Huffman encoded name and value
        private static readonly byte[] _literalHeaderFieldWithoutIndexingNewNameHuffmanNameAndValue = new byte[] { 0x00 }
            .Concat(_newNameHuffmanNameAndValue)
            .ToArray();

        // Literal Header Field without Indexing Representation - Indexed Name - "user-agent: value"
        private static readonly byte[] _literalHeaderFieldWithoutIndexingIndexedName = new byte[] { 0x0f, 0x2b }
            .Concat(_indexedName)
            .ToArray();

        // Literal Header Field without Indexing Representation - Indexed Name - "user-agent: value" - Huffman encoded value
        private static readonly byte[] _literalHeaderFieldWithoutIndexingIndexedNameHuffmanValue = new byte[] { 0x0f, 0x2b }
            .Concat(_indexedNameHuffmanValue)
            .ToArray();

        // Literal Header Field without Indexing Representation - Indexed Name - Index 62 (first index in dynamic table)
        private static readonly byte[] _literalHeaderFieldWithoutIndexingIndexedNameIndex62 = new byte[] { 0x0f, 0x2f }
            .Concat(_indexedName)
            .ToArray();

        // Literal Header Field Never Indexed Representation - New Name - "new-header: value"
        private static readonly byte[] _literalHeaderFieldNeverIndexedNewName = new byte[] { 0x00 }
            .Concat(_newName)
            .ToArray();

        // Literal Header Field Never Indexed Representation - New Name - "new-header: value" - Huffman encoded name
        private static readonly byte[] _literalHeaderFieldNeverIndexedNewNameHuffmanName = new byte[] { 0x00 }
            .Concat(_newNameHuffmanName)
            .ToArray();

        // Literal Header Field Never Indexed Representation - New Name - "new-header: value" - Huffman encoded value
        private static readonly byte[] _literalHeaderFieldNeverIndexedNewNameHuffmanValue = new byte[] { 0x00 }
            .Concat(_newNameHuffmanValue)
            .ToArray();

        // Literal Header Field Never Indexed Representation - New Name - "new-header: value" - Huffman encoded name and value
        private static readonly byte[] _literalHeaderFieldNeverIndexedNewNameHuffmanNameAndValue = new byte[] { 0x00 }
            .Concat(_newNameHuffmanNameAndValue)
            .ToArray();

        // Literal Header Field Never Indexed Representation - Indexed Name - "user-agent: value"
        private static readonly byte[] _literalHeaderFieldNeverIndexedIndexedName = new byte[] { 0x0f, 0x2b }
            .Concat(_indexedName)
            .ToArray();

        // Literal Header Field Never Indexed Representation - Indexed Name - "user-agent: value" - Huffman encoded value
        private static readonly byte[] _literalHeaderFieldNeverIndexedIndexedNameHuffmanValue = new byte[] { 0x0f, 0x2b }
            .Concat(_indexedNameHuffmanValue)
            .ToArray();

        // Literal Header Field Never Indexed Representation - Indexed Name - Index 62 (first index in dynamic table)
        private static readonly byte[] _literalHeaderFieldNeverIndexedIndexedNameIndex62 = new byte[] { 0x1f, 0x2f }
            .Concat(_indexedName)
            .ToArray();

        private readonly DynamicTable _dynamicTable;
        private readonly HPackDecoder _decoder;

        public HPackDecoderTests()
        {
            _dynamicTable = new DynamicTable(4096);
            _decoder = new HPackDecoder(_dynamicTable);
        }

        [Fact]
        public void DecodesIndexedHeaderField_StaticTable()
        {
            var headers = new HttpRequestHeaders();
            _decoder.Decode(_indexedHeaderStatic, headers);
            Assert.Equal("GET", ((IHeaderDictionary)headers)[":method"]);
        }

        [Fact]
        public void DecodesIndexedHeaderField_DynamicTable()
        {
            var headers = new HttpRequestHeaders();

            // Add the header to the dynamic table
            _decoder.Decode(_literalHeaderFieldWithIndexingNewName, headers);
            ((IHeaderDictionary)headers).Clear();

            // Index it
            _decoder.Decode(_indexedHeaderDynamic, headers);
            Assert.Equal("value", ((IHeaderDictionary)headers)["new-header"]);
        }

        [Fact]
        public void DecodesIndexedHeaderField_OutOfRange_Error()
        {
            var headers = new HttpRequestHeaders();
            Assert.Throws<IndexOutOfRangeException>(() => _decoder.Decode(_indexedHeaderDynamic, headers));
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_NewName()
        {
            TestDecodeWithIndexing(_literalHeaderFieldWithIndexingNewName, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_NewName_HuffmanEncodedName()
        {
            TestDecodeWithIndexing(_literalHeaderFieldWithIndexingNewNameHuffmanName, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_NewName_HuffmanEncodedValue()
        {
            TestDecodeWithIndexing(_literalHeaderFieldWithIndexingNewNameHuffmanValue, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_NewName_HuffmanEncodedNameAndValue()
        {
            TestDecodeWithIndexing(_literalHeaderFieldWithIndexingNewNameHuffmanNameAndValue, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_IndexedName()
        {
            TestDecodeWithIndexing(_literalHeaderFieldWithIndexingIndexedName, "user-agent", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_IndexedName_HuffmanEncodedValue()
        {
            TestDecodeWithIndexing(_literalHeaderFieldWithIndexingIndexedNameHuffmanValue, "user-agent", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_IndexedName_OutOfRange_Error()
        {
            var headers = new HttpRequestHeaders();
            Assert.Throws<IndexOutOfRangeException>(() => _decoder.Decode(_literalHeaderFieldWithIndexingIndexedNameIndex62, headers));
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_NewName()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldWithoutIndexingNewName, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_NewName_HuffmanEncodedName()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldWithoutIndexingNewNameHuffmanName, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_NewName_HuffmanEncodedValue()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldWithoutIndexingNewNameHuffmanValue, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_NewName_HuffmanEncodedNameAndValue()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldWithoutIndexingNewNameHuffmanNameAndValue, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_IndexedName()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldWithoutIndexingIndexedName, "user-agent", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_IndexedName_HuffmanEncodedValue()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldWithoutIndexingIndexedNameHuffmanValue, "user-agent", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_IndexedName_OutOfRange_Error()
        {
            var headers = new HttpRequestHeaders();
            Assert.Throws<IndexOutOfRangeException>(() => _decoder.Decode(_literalHeaderFieldWithoutIndexingIndexedNameIndex62, headers));
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_NewName()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldNeverIndexedNewName, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_NewName_HuffmanEncodedName()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldNeverIndexedNewNameHuffmanName, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_NewName_HuffmanEncodedValue()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldNeverIndexedNewNameHuffmanValue, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_NewName_HuffmanEncodedNameAndValue()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldNeverIndexedNewNameHuffmanNameAndValue, "new-header", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_IndexedName()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldNeverIndexedIndexedName, "user-agent", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_IndexedName_HuffmanEncodedValue()
        {
            TestDecodeWithoutIndexing(_literalHeaderFieldNeverIndexedIndexedNameHuffmanValue, "user-agent", "value");
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_IndexedName_OutOfRange_Error()
        {
            var headers = new HttpRequestHeaders();
            Assert.Throws<IndexOutOfRangeException>(() => _decoder.Decode(_literalHeaderFieldNeverIndexedIndexedNameIndex62, headers));
        }

        [Fact]
        public void DecodesDynamicTableSizeUpdate()
        {
            Assert.True(false);
        }

        [Fact]
        public void DecodesDynamicTableSizeUpdate_GreaterThanLimit_Error()
        {
            Assert.True(false);
        }

        private void TestDecodeWithIndexing(byte[] data, string expectedHeaderName, string expectedHeaderValue)
        {
            TestDecode(data, expectedHeaderName, expectedHeaderValue, expectDynamicTableEntry: true);
        }

        private void TestDecodeWithoutIndexing(byte[] data, string expectedHeaderName, string expectedHeaderValue)
        {
            TestDecode(data, expectedHeaderName, expectedHeaderValue, expectDynamicTableEntry: false);
        }

        private void TestDecode(byte[] data, string expectedHeaderName, string expectedHeaderValue, bool expectDynamicTableEntry)
        {
            Assert.Equal(0, _dynamicTable.Count);
            Assert.Equal(0, _dynamicTable.Size);

            var headers = new HttpRequestHeaders();
            _decoder.Decode(data, headers);

            Assert.Equal(expectedHeaderValue, ((IHeaderDictionary)headers)[expectedHeaderName]);

            if (expectDynamicTableEntry)
            {
                Assert.Equal(1, _dynamicTable.Count);
                Assert.Equal(expectedHeaderName, _dynamicTable[0].Name);
                Assert.Equal(expectedHeaderValue, _dynamicTable[0].Value);
                Assert.Equal(expectedHeaderName.Length + expectedHeaderValue.Length + 32, _dynamicTable.Size);
            }
            else
            {
                Assert.Equal(0, _dynamicTable.Count);
                Assert.Equal(0, _dynamicTable.Size);
            }
        }
    }
}
