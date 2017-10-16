// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    public static class ReadCursorOperations
    {
        public static int Seek(ReadCursor begin, ReadCursor end, out ReadCursor result, byte byte0)
        {
            var enumerator = new SegmentEnumerator(begin, end);
            while (enumerator.MoveNext())
            {
                var segmentPart = enumerator.Current;
                var segment = segmentPart.Segment;
                var span = segment.Buffer.Span.Slice(segmentPart.Start, segmentPart.Length);

                int index = span.IndexOf(byte0);
                if (index != -1)
                {
                    result = new ReadCursor(segment, segmentPart.Start + index);
                    return span[index];
                }
            }

            result = end;
            return -1;
        }

        public static int Seek(ReadCursor begin, ReadCursor end, out ReadCursor result, byte byte0, byte byte1)
        {
            var enumerator = new SegmentEnumerator(begin, end);
            while (enumerator.MoveNext())
            {
                var segmentPart = enumerator.Current;
                var segment = segmentPart.Segment;
                var span = segment.Buffer.Span.Slice(segmentPart.Start, segmentPart.Length);

                int index = span.IndexOfAny(byte0, byte1);

                if (index != -1)
                {
                    result = new ReadCursor(segment, segmentPart.Start + index);
                    return span[index];
                }
            }

            result = end;
            return -1;
        }

        public static int Seek(ReadCursor begin, ReadCursor end, out ReadCursor result, byte byte0, byte byte1, byte byte2)
        {
            var enumerator = new SegmentEnumerator(begin, end);
            while (enumerator.MoveNext())
            {
                var segmentPart = enumerator.Current;
                var segment = segmentPart.Segment;
                var span = segment.Buffer.Span.Slice(segmentPart.Start, segmentPart.Length);

                int index = span.IndexOfAny(byte0, byte1, byte2);

                if (index != -1)
                {
                    result = new ReadCursor(segment, segmentPart.Start + index);
                    return span[index];
                }
            }

            result = end;
            return -1;
        }
    }
}
