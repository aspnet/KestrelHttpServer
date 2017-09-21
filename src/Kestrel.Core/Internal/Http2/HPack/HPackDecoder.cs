// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack
{
    public class HPackDecoder
    {
        private enum State
        {
            Ready,
            HeaderFieldIndex,
            HeaderNameIndex,
            HeaderNameLength,
            HeaderNameLengthContinue,
            HeaderName,
            HeaderValueLength,
            HeaderValueLengthContinue,
            HeaderValue,
            DynamicTableSizeUpdate
        }

        // TODO: add new configurable limit
        public const int MaxStringOctets = 4096;

        // http://httpwg.org/specs/rfc7541.html#rfc.section.6.1
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 1 |        Index (7+)         |
        // +---+---------------------------+
        private const byte IndexedHeaderFieldMask = 0x80;
        private const byte IndexedHeaderFieldRepresentation = 0x80;

        // http://httpwg.org/specs/rfc7541.html#rfc.section.6.2.1
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 1 |      Index (6+)       |
        // +---+---+-----------------------+
        private const byte LiteralHeaderFieldWithIncrementalIndexingMask = 0xc0;
        private const byte LiteralHeaderFieldWithIncrementalIndexingRepresentation = 0x40;

        // http://httpwg.org/specs/rfc7541.html#rfc.section.6.2.2
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 0 |  Index (4+)   |
        // +---+---+-----------------------+
        private const byte LiteralHeaderFieldWithoutIndexingMask = 0xf0;
        private const byte LiteralHeaderFieldWithoutIndexingRepresentation = 0x00;

        // http://httpwg.org/specs/rfc7541.html#rfc.section.6.2.3
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 0 | 1 |  Index (4+)   |
        // +---+---+-----------------------+
        private const byte LiteralHeaderFieldNeverIndexedMask = 0xf0;
        private const byte LiteralHeaderFieldNeverIndexedRepresentation = 0x10;

        // http://httpwg.org/specs/rfc7541.html#rfc.section.6.3
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 | 1 |   Max size (5+)   |
        // +---+---------------------------+
        private const byte DynamicTableSizeUpdateMask = 0xe0;
        private const byte DynamicTableSizeUpdateRepresentation = 0x20;

        // http://httpwg.org/specs/rfc7541.html#rfc.section.5.2
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | H |    String Length (7+)     |
        // +---+---------------------------+
        private const byte HuffmanMask = 0x80;

        private const int IndexedHeaderFieldPrefix = 7;
        private const int LiteralHeaderFieldWithIncrementalIndexingPrefix = 6;
        private const int LiteralHeaderFieldWithoutIndexingPrefix = 4;
        private const int LiteralHeaderFieldNeverIndexedPrefix = 4;
        private const int DynamicTableSizeUpdatePrefix = 5;
        private const int StringLengthPrefix = 7;

        private readonly int _maxDynamicTableSize;
        private readonly DynamicTable _dynamicTable;
        private readonly IntegerDecoder _integerDecoder = new IntegerDecoder();
        private readonly byte[] _stringOctets = new byte[MaxStringOctets];

        private State _state = State.Ready;
        private string _headerName = string.Empty;
        private string _headerValue = string.Empty;
        private int _stringLength;
        private int _stringIndex;
        private bool _index;
        private bool _huffman;

        public HPackDecoder(int maxDynamicTableSize)
            : this(maxDynamicTableSize, new DynamicTable(maxDynamicTableSize))
        {
            _maxDynamicTableSize = maxDynamicTableSize;
        }

        // For testing.
        internal HPackDecoder(int maxDynamicTableSize, DynamicTable dynamicTable)
        {
            _maxDynamicTableSize = maxDynamicTableSize;
            _dynamicTable = dynamicTable;
        }

        public void Decode(Span<byte> data, IHeaderDictionary headers, bool endHeaders)
        {
            for (var i = 0; i < data.Length; i++)
            {
                OnByte(data[i], headers);
            }

            if (endHeaders && _state != State.Ready)
            {
                throw new HPackDecodingException("The final header block fragment was incomplete and could not be fully decoded.");
            }
        }

        public void OnByte(byte b, IHeaderDictionary headers)
        {
            switch (_state)
            {
                case State.Ready:
                    if ((b & IndexedHeaderFieldMask) == IndexedHeaderFieldRepresentation)
                    {
                        var val = b & ~IndexedHeaderFieldMask;

                        if (_integerDecoder.BeginDecode((byte)val, IndexedHeaderFieldPrefix))
                        {
                            OnIndexedHeaderField(_integerDecoder.Value, headers);
                        }
                        else
                        {
                            _state = State.HeaderFieldIndex;
                        }
                    }
                    else if ((b & LiteralHeaderFieldWithIncrementalIndexingMask) == LiteralHeaderFieldWithIncrementalIndexingRepresentation)
                    {
                        _index = true;
                        var val = b & ~LiteralHeaderFieldWithIncrementalIndexingMask;

                        if (val == 0)
                        {
                            _state = State.HeaderNameLength;
                        }
                        else if (_integerDecoder.BeginDecode((byte)val, LiteralHeaderFieldWithIncrementalIndexingPrefix))
                        {
                            OnIndexedHeaderName(_integerDecoder.Value);
                        }
                        else
                        {
                            _state = State.HeaderNameIndex;
                        }
                    }
                    else if ((b & LiteralHeaderFieldWithoutIndexingMask) == LiteralHeaderFieldWithoutIndexingRepresentation)
                    {
                        _index = false;
                        var val = b & ~LiteralHeaderFieldWithoutIndexingMask;

                        if (val == 0)
                        {
                            _state = State.HeaderNameLength;
                        }
                        else if (_integerDecoder.BeginDecode((byte)val, LiteralHeaderFieldWithoutIndexingPrefix))
                        {
                            OnIndexedHeaderName(_integerDecoder.Value);
                        }
                        else
                        {
                            _state = State.HeaderNameIndex;
                        }
                    }
                    else if ((b & LiteralHeaderFieldNeverIndexedMask) == LiteralHeaderFieldNeverIndexedRepresentation)
                    {
                        _index = false;
                        var val = b & ~LiteralHeaderFieldNeverIndexedMask;

                        if (val == 0)
                        {
                            _state = State.HeaderNameLength;
                        }
                        else if (_integerDecoder.BeginDecode((byte)val, LiteralHeaderFieldNeverIndexedPrefix))
                        {
                            OnIndexedHeaderName(_integerDecoder.Value);
                        }
                        else
                        {
                            _state = State.HeaderNameIndex;
                        }
                    }
                    else if ((b & DynamicTableSizeUpdateMask) == DynamicTableSizeUpdateRepresentation)
                    {
                        if (_integerDecoder.BeginDecode((byte)(b & ~DynamicTableSizeUpdateMask), DynamicTableSizeUpdatePrefix))
                        {
                            // TODO: validate that it's less than what's defined via SETTINGS
                            _dynamicTable.Resize(_integerDecoder.Value);
                        }
                        else
                        {
                            _state = State.DynamicTableSizeUpdate;
                        }
                    }
                    else
                    {
                        // Can't happen
                        throw new HPackDecodingException($"Byte value {b} does not encode a valid header field representation.");
                    }

                    break;
                case State.HeaderFieldIndex:
                    if (_integerDecoder.Decode(b))
                    {
                        OnIndexedHeaderField(_integerDecoder.Value, headers);
                    }

                    break;
                case State.HeaderNameIndex:
                    if (_integerDecoder.Decode(b))
                    {
                        OnIndexedHeaderName(_integerDecoder.Value);
                    }

                    break;
                case State.HeaderNameLength:
                    _huffman = (b & HuffmanMask) != 0;

                    if (_integerDecoder.BeginDecode((byte)(b & ~HuffmanMask), StringLengthPrefix))
                    {
                        OnStringLength(_integerDecoder.Value, nextState: State.HeaderName);
                    }
                    else
                    {
                        _state = State.HeaderNameLengthContinue;
                    }

                    break;
                case State.HeaderNameLengthContinue:
                    if (_integerDecoder.Decode(b))
                    {
                        OnStringLength(_integerDecoder.Value, nextState: State.HeaderName);
                    }

                    break;
                case State.HeaderName:
                    _stringOctets[_stringIndex++] = b;

                    if (_stringIndex == _stringLength)
                    {
                        _headerName = OnString(nextState: State.HeaderValueLength);
                    }

                    break;
                case State.HeaderValueLength:
                    _huffman = (b & HuffmanMask) != 0;

                    if (_integerDecoder.BeginDecode((byte)(b & ~HuffmanMask), StringLengthPrefix))
                    {
                        OnStringLength(_integerDecoder.Value, nextState: State.HeaderValue);
                    }
                    else
                    {
                        _state = State.HeaderValueLengthContinue;
                    }

                    break;
                case State.HeaderValueLengthContinue:
                    if (_integerDecoder.Decode(b))
                    {
                        OnStringLength(_integerDecoder.Value, nextState: State.HeaderValue);
                    }

                    break;
                case State.HeaderValue:
                    _stringOctets[_stringIndex++] = b;

                    if (_stringIndex == _stringLength)
                    {
                        _headerValue = OnString(nextState: State.Ready);
                        headers.Append(_headerName, _headerValue);

                        if (_index)
                        {
                            _dynamicTable.Insert(_headerName, _headerValue);
                        }
                    }

                    break;
                case State.DynamicTableSizeUpdate:
                    if (_integerDecoder.Decode(b))
                    {
                        if (_integerDecoder.Value > _maxDynamicTableSize)
                        {
                            throw new HPackDecodingException(
                                CoreStrings.FormatHPackErrorDynamicTableSizeUpdateTooLarge(_integerDecoder.Value, _maxDynamicTableSize));
                        }

                        _dynamicTable.Resize(_integerDecoder.Value);
                        _state = State.Ready;
                    }

                    break;
                default:
                    // Can't happen
                    throw new HPackDecodingException("The HPACK decoder reached an invalid state.");
            }
        }

        private void OnIndexedHeaderField(int index, IHeaderDictionary headers)
        {
            var header = GetHeader(index);
            headers.Append(header.Name, header.Value);
            _state = State.Ready;
        }

        private void OnIndexedHeaderName(int index)
        {
            var header = GetHeader(index);
            _headerName = header.Name;
            _state = State.HeaderValueLength;
        }

        private void OnStringLength(int length, State nextState)
        {
            if (length > _stringOctets.Length)
            {
                throw new HPackDecodingException(CoreStrings.FormatHPackStringLengthTooLarge(length, _stringOctets.Length));
            }

            _stringLength = length;
            _stringIndex = 0;
            _state = nextState;
        }

        private string OnString(State nextState)
        {
            _state = nextState;

            if (_huffman)
            {
                try
                {
                    return Huffman.Decode(_stringOctets, 0, _stringLength);
                }
                catch (HuffmanDecodingException ex)
                {
                    throw new HPackDecodingException(CoreStrings.HPackHuffmanError, ex);
                }
            }
            else
            {
                return Encoding.ASCII.GetString(_stringOctets, 0, _stringLength);
            }
        }

        private HeaderField GetHeader(int index)
        {
            try
            {
                return index <= StaticTable.Instance.Count
                    ? StaticTable.Instance[index - 1]
                    : _dynamicTable[index - StaticTable.Instance.Count - 1];
            }
            catch (IndexOutOfRangeException ex)
            {
                throw new HPackDecodingException(CoreStrings.FormatHPackErrorIndexOutOfRange(index), ex);
            }
        }
    }
}
