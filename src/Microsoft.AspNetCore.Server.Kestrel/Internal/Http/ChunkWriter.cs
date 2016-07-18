// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public static class ChunkWriter
    {
        private static readonly ArraySegment<byte> _endChunkBytes = CreateAsciiByteArraySegment("\r\n");
        private static readonly byte[] _hex = Encoding.ASCII.GetBytes("0123456789abcdef");

        private static ArraySegment<byte> CreateAsciiByteArraySegment(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            return new ArraySegment<byte>(bytes);
        }

        public static ArraySegment<byte> BeginChunkBytes(int dataCount, ref byte[] chunkArray)
        {
            if (chunkArray == null)
            {
                chunkArray = new byte[10];

                chunkArray[8] = (byte)'\r';
                chunkArray[9] = (byte)'\n';
            }

            var bytes = chunkArray;

            bytes[0] = _hex[((dataCount >> 0x1c) & 0x0f)];
            bytes[1] = _hex[((dataCount >> 0x18) & 0x0f)];
            bytes[2] = _hex[((dataCount >> 0x14) & 0x0f)];
            bytes[3] = _hex[((dataCount >> 0x10) & 0x0f)];
            bytes[4] = _hex[((dataCount >> 0x0c) & 0x0f)];
            bytes[5] = _hex[((dataCount >> 0x08) & 0x0f)];
            bytes[6] = _hex[((dataCount >> 0x04) & 0x0f)];
            bytes[7] = _hex[((dataCount >> 0x00) & 0x0f)];

            // Determine the most-significant non-zero nibble
            int total, shift;
            total = (dataCount > 0xffff) ? 0x10 : 0x00;
            dataCount >>= total;
            shift = (dataCount > 0x00ff) ? 0x08 : 0x00;
            dataCount >>= shift;
            total |= shift;
            total |= (dataCount > 0x000f) ? 0x04 : 0x00;

            var offset = 7 - (total >> 2);
            return new ArraySegment<byte>(bytes, offset, 10 - offset);
        }

        public static int WriteBeginChunkBytes(ref MemoryPoolIterator start, int dataCount, ref byte[] chunkArray)
        {
            var chunkSegment = BeginChunkBytes(dataCount, ref chunkArray);
            start.CopyFrom(chunkSegment);
            return chunkSegment.Count;
        }

        public static void WriteEndChunkBytes(ref MemoryPoolIterator start)
        {
            start.CopyFrom(_endChunkBytes);
        }
    }
}
