// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
        private const int DynamicTableInitialMaxSize = 4096;

        // Indexed Header Field Representation - Static Table - Index 2 (:method: GET)
        private static readonly byte[] _indexedHeaderStatic = new byte[] { 0x82 };

        // Indexed Header Field Representation - Dynamic Table - Index 62 (first index in dynamic table)
        private static readonly byte[] _indexedHeaderDynamic = new byte[] { 0xbe };

        // Literal Header Field with Incremental Indexing Representation - New Name
        private static readonly byte[] _literalHeaderFieldWithIndexingNewName = new byte[] { 0x40 };

        // Literal Header Field with Incremental Indexing Representation - Indexed Name - Index 58 (user-agent)
        private static readonly byte[] _literalHeaderFieldWithIndexingIndexedName = new byte[] { 0x7a };

        // Literal Header Field without Indexing Representation - New Name
        private static readonly byte[] _literalHeaderFieldWithoutIndexingNewName = new byte[] { 0x00 };

        // Literal Header Field without Indexing Representation - Indexed Name - Index 58 (user-agent)
        private static readonly byte[] _literalHeaderFieldWithoutIndexingIndexedName = new byte[] { 0x0f, 0x2b };

        // Literal Header Field Never Indexed Representation - New Name
        private static readonly byte[] _literalHeaderFieldNeverIndexedNewName = new byte[] { 0x10 };

        // Literal Header Field Never Indexed Representation - Indexed Name - Index 58 (user-agent)
        private static readonly byte[] _literalHeaderFieldNeverIndexedIndexedName = new byte[] { 0x1f, 0x2b };

        private const string _userAgentString = "user-agent";

        private const string _headerNameString = "new-header";

        private static readonly byte[] _headerNameBytes = Encoding.ASCII.GetBytes(_headerNameString);

        // n     e     w       -      h     e     a     d     e     r      *
        // 10101000 10111110 00010110 10011100 10100011 10010000 10110110 01111111
        private static readonly byte[] _headerNameHuffmanBytes = new byte[] { 0xa8, 0xbe, 0x16, 0x9c, 0xa3, 0x90, 0xb6, 0x7f };

        private const string _headerValueString = "value";

        private static readonly byte[] _headerValueBytes = Encoding.ASCII.GetBytes(_headerValueString);

        // v      a     l      u      e    *
        // 11101110 00111010 00101101 00101111
        private static readonly byte[] _headerValueHuffmanBytes = new byte [] { 0xee, 0x3a, 0x2d, 0x2f };

        private static readonly byte[] _headerName = new byte[] { (byte)_headerNameBytes.Length }
            .Concat(_headerNameBytes)
            .ToArray();

        private static readonly byte[] _headerNameHuffman = new byte[] { (byte)(0x80 | _headerNameHuffmanBytes.Length) }
            .Concat(_headerNameHuffmanBytes)
            .ToArray();

        private static readonly byte[] _headerValue = new byte[] { (byte)_headerValueBytes.Length }
            .Concat(_headerValueBytes)
            .ToArray();

        private static readonly byte[] _headerValueHuffman = new byte[] { (byte)(0x80 | _headerValueHuffmanBytes.Length) }
            .Concat(_headerValueHuffmanBytes)
            .ToArray();

        // &        *
        // 11111000 11111111
        private static readonly byte[] _huffmanLongPadding = new byte[] { 0x82, 0xf8, 0xff };

        // EOS                              *
        // 11111111 11111111 11111111 11111111
        private static readonly byte[] _huffmanEos = new byte[] { 0x84, 0xff, 0xff, 0xff, 0xff };

        private readonly DynamicTable _dynamicTable;
        private readonly HPackDecoder _decoder;

        public HPackDecoderTests()
        {
            _dynamicTable = new DynamicTable(DynamicTableInitialMaxSize);
            _decoder = new HPackDecoder(DynamicTableInitialMaxSize, _dynamicTable);
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
            _dynamicTable.Insert(_headerNameString, _headerValueString);

            // Index it
            _decoder.Decode(_indexedHeaderDynamic, headers);
            Assert.Equal(_headerValueString, ((IHeaderDictionary)headers)[_headerNameString]);
        }

        [Fact]
        public void DecodesIndexedHeaderField_OutOfRange_Error()
        {
            var headers = new HttpRequestHeaders();
            var exception = Assert.Throws<HPackDecodingException>(() => _decoder.Decode(_indexedHeaderDynamic, headers));
            Assert.Equal(CoreStrings.FormatHPackErrorIndexOutOfRange(62), exception.Message);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_NewName()
        {
            var encoded = _literalHeaderFieldWithIndexingNewName
                .Concat(_headerName)
                .Concat(_headerValue)
                .ToArray();

            TestDecodeWithIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_NewName_HuffmanEncodedName()
        {
            var encoded = _literalHeaderFieldWithIndexingNewName
                .Concat(_headerNameHuffman)
                .Concat(_headerValue)
                .ToArray();

            TestDecodeWithIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_NewName_HuffmanEncodedValue()
        {
            var encoded = _literalHeaderFieldWithIndexingNewName
                .Concat(_headerName)
                .Concat(_headerValueHuffman)
                .ToArray();

            TestDecodeWithIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_NewName_HuffmanEncodedNameAndValue()
        {
            var encoded = _literalHeaderFieldWithIndexingNewName
                .Concat(_headerNameHuffman)
                .Concat(_headerValueHuffman)
                .ToArray();

            TestDecodeWithIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_IndexedName()
        {
            var encoded = _literalHeaderFieldWithIndexingIndexedName
                .Concat(_headerValue)
                .ToArray();

            TestDecodeWithIndexing(encoded, _userAgentString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_IndexedName_HuffmanEncodedValue()
        {
            var encoded = _literalHeaderFieldWithIndexingIndexedName
                .Concat(_headerValueHuffman)
                .ToArray();

            TestDecodeWithIndexing(encoded, _userAgentString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithIncrementalIndexing_IndexedName_OutOfRange_Error()
        {
            // 01      (Literal Header Field without Indexing Representation)
            // 11 1110 (Indexed Name - Index 62 encoded with 6-bit prefix - see http://httpwg.org/specs/rfc7541.html#integer.representation)
            // Index 62 is the first entry in the dynamic table. If there's nothing there, the decoder should throw.

            var headers = new HttpRequestHeaders();
            var exception = Assert.Throws<HPackDecodingException>(() => _decoder.Decode(new byte[] { 0x7e }, headers));
            Assert.Equal(CoreStrings.FormatHPackErrorIndexOutOfRange(62), exception.Message);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_NewName()
        {
            var encoded = _literalHeaderFieldWithoutIndexingNewName
                .Concat(_headerName)
                .Concat(_headerValue)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_NewName_HuffmanEncodedName()
        {
            var encoded = _literalHeaderFieldWithoutIndexingNewName
                .Concat(_headerNameHuffman)
                .Concat(_headerValue)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_NewName_HuffmanEncodedValue()
        {
            var encoded = _literalHeaderFieldWithoutIndexingNewName
                .Concat(_headerName)
                .Concat(_headerValueHuffman)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_NewName_HuffmanEncodedNameAndValue()
        {
            var encoded = _literalHeaderFieldWithoutIndexingNewName
                .Concat(_headerNameHuffman)
                .Concat(_headerValueHuffman)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_IndexedName()
        {
            var encoded = _literalHeaderFieldWithoutIndexingIndexedName
                .Concat(_headerValue)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _userAgentString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_IndexedName_HuffmanEncodedValue()
        {
            var encoded = _literalHeaderFieldWithoutIndexingIndexedName
                .Concat(_headerValueHuffman)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _userAgentString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldWithoutIndexing_IndexedName_OutOfRange_Error()
        {
            // 0000           (Literal Header Field without Indexing Representation)
            // 1111 0010 1111 (Indexed Name - Index 62 encoded with 4-bit prefix - see http://httpwg.org/specs/rfc7541.html#integer.representation)
            // Index 62 is the first entry in the dynamic table. If there's nothing there, the decoder should throw.

            var headers = new HttpRequestHeaders();
            var exception = Assert.Throws<HPackDecodingException>(() => _decoder.Decode(new byte[] { 0x0f, 0x2f }, headers));
            Assert.Equal(CoreStrings.FormatHPackErrorIndexOutOfRange(62), exception.Message);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_NewName()
        {
            var encoded = _literalHeaderFieldNeverIndexedNewName
                .Concat(_headerName)
                .Concat(_headerValue)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_NewName_HuffmanEncodedName()
        {
            var encoded = _literalHeaderFieldNeverIndexedNewName
                .Concat(_headerNameHuffman)
                .Concat(_headerValue)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_NewName_HuffmanEncodedValue()
        {
            var encoded = _literalHeaderFieldNeverIndexedNewName
                .Concat(_headerName)
                .Concat(_headerValueHuffman)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_NewName_HuffmanEncodedNameAndValue()
        {
            var encoded = _literalHeaderFieldNeverIndexedNewName
                .Concat(_headerNameHuffman)
                .Concat(_headerValueHuffman)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _headerNameString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_IndexedName()
        {
            // 0001           (Literal Header Field Never Indexed Representation)
            // 1111 0010 1011 (Indexed Name - Index 58 encoded with 4-bit prefix - see http://httpwg.org/specs/rfc7541.html#integer.representation)
            // Concatenated with value bytes
            var encoded = _literalHeaderFieldNeverIndexedIndexedName
                .Concat(_headerValue)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _userAgentString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_IndexedName_HuffmanEncodedValue()
        {
            // 0001           (Literal Header Field Never Indexed Representation)
            // 1111 0010 1011 (Indexed Name - Index 58 encoded with 4-bit prefix - see http://httpwg.org/specs/rfc7541.html#integer.representation)
            // Concatenated with Huffman encoded value bytes
            var encoded = _literalHeaderFieldNeverIndexedIndexedName
                .Concat(_headerValueHuffman)
                .ToArray();

            TestDecodeWithoutIndexing(encoded, _userAgentString, _headerValueString);
        }

        [Fact]
        public void DecodesLiteralHeaderFieldNeverIndexed_IndexedName_OutOfRange_Error()
        {
            // 0001           (Literal Header Field Never Indexed Representation)
            // 1111 0010 1111 (Indexed Name - Index 62 encoded with 4-bit prefix - see http://httpwg.org/specs/rfc7541.html#integer.representation)
            // Index 62 is the first entry in the dynamic table. If there's nothing there, the decoder should throw.

            var headers = new HttpRequestHeaders();
            var exception = Assert.Throws<HPackDecodingException>(() => _decoder.Decode(new byte[] { 0x1f, 0x2f }, headers));
            Assert.Equal(CoreStrings.FormatHPackErrorIndexOutOfRange(62), exception.Message);
        }

        [Fact]
        public void DecodesDynamicTableSizeUpdate()
        {
            // 001   (Dynamic Table Size Update)
            // 11110 (30 encoded with 5-bit prefix - see http://httpwg.org/specs/rfc7541.html#integer.representation)

            Assert.Equal(DynamicTableInitialMaxSize, _dynamicTable.MaxSize);

            var headers = new HttpRequestHeaders();
            _decoder.Decode(new byte[] { 0x3e }, headers);

            Assert.Equal(30, _dynamicTable.MaxSize);
        }

        [Fact]
        public void DecodesDynamicTableSizeUpdate_GreaterThanLimit_Error()
        {
            // 001                     (Dynamic Table Size Update)
            // 11111 11100010 00011111 (4097 encoded with 5-bit prefix - see http://httpwg.org/specs/rfc7541.html#integer.representation)

            Assert.Equal(DynamicTableInitialMaxSize, _dynamicTable.MaxSize);

            var headers = new HttpRequestHeaders();
            var exception = Assert.Throws<HPackDecodingException>(() => _decoder.Decode(new byte[] { 0x3f, 0xe2, 0x1f }, headers));
            Assert.Equal(CoreStrings.FormatHPackErrorDynamicTableSizeUpdateTooLarge(4097, DynamicTableInitialMaxSize), exception.Message);
        }

        public static readonly TheoryData<byte[]> _huffmanDecodingErrorData = new TheoryData<byte[]>
        {
            // Invalid Huffman encoding in header name

            _literalHeaderFieldWithIndexingNewName.Concat(_huffmanLongPadding).ToArray(),
            _literalHeaderFieldWithIndexingNewName.Concat(_huffmanEos).ToArray(),

            _literalHeaderFieldWithoutIndexingNewName.Concat(_huffmanLongPadding).ToArray(),
            _literalHeaderFieldWithoutIndexingNewName.Concat(_huffmanEos).ToArray(),

            _literalHeaderFieldNeverIndexedNewName.Concat(_huffmanLongPadding).ToArray(),
            _literalHeaderFieldNeverIndexedNewName.Concat(_huffmanEos).ToArray(),

            // Invalid Huffman encoding in header value

            _literalHeaderFieldWithIndexingIndexedName.Concat(_huffmanLongPadding).ToArray(),
            _literalHeaderFieldWithIndexingIndexedName.Concat(_huffmanEos).ToArray(),

            _literalHeaderFieldWithoutIndexingIndexedName.Concat(_huffmanLongPadding).ToArray(),
            _literalHeaderFieldWithoutIndexingIndexedName.Concat(_huffmanEos).ToArray(),

            _literalHeaderFieldNeverIndexedIndexedName.Concat(_huffmanLongPadding).ToArray(),
            _literalHeaderFieldNeverIndexedIndexedName.Concat(_huffmanEos).ToArray()
        };

        [Theory]
        [MemberData(nameof(_huffmanDecodingErrorData))]
        public void WrapsHuffmanDecodingExceptionInHPackDecodingException(byte[] data)
        {
            var headers = new HttpRequestHeaders();
            var exception = Assert.Throws<HPackDecodingException>(() => _decoder.Decode(data, headers));
            Assert.Equal(CoreStrings.HPackHuffmanError, exception.Message);
            Assert.IsType<HuffmanDecodingException>(exception.InnerException);
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
