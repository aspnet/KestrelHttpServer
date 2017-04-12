﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class PipelineExtensionTests : IDisposable
    {
        // ulong.MaxValue.ToString().Length
        private const int _ulongMaxValueLength = 20;

        private readonly IPipe _pipe;
        private readonly PipeFactory _pipeFactory = new PipeFactory();

        public PipelineExtensionTests()
        {
            _pipe = _pipeFactory.Create();
        }

        public void Dispose()
        {
            _pipeFactory.Dispose();
        }

        [Theory]
        [InlineData(ulong.MinValue)]
        [InlineData(ulong.MaxValue)]
        [InlineData(4_8_15_16_23_42)]
        public void WritesNumericToAscii(ulong number)
        {
            var writerBuffer = _pipe.Writer.Alloc();
            var writer = new WritableBufferWriter(writerBuffer);
            PipelineExtensions.WriteNumeric(ref writer, number);
            writerBuffer.FlushAsync().GetAwaiter().GetResult();

            var reader = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
            var numAsStr = number.ToString();
            var expected = Encoding.ASCII.GetBytes(numAsStr);
            AssertExtensions.Equal(expected, reader.Buffer.Slice(0, numAsStr.Length).ToArray());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(_ulongMaxValueLength / 2)]
        [InlineData(_ulongMaxValueLength - 1)]
        public void WritesNumericAcrossSpanBoundaries(int gapSize)
        {
            var writerBuffer = _pipe.Writer.Alloc(100);
            var writer = new WritableBufferWriter(writerBuffer);
            // almost fill up the first block
            var spacer = new byte[writer.Span.Length - gapSize];
            writer.Write(spacer);

            var bufferLength = writer.Span.Length;
            PipelineExtensions.WriteNumeric(ref writer, ulong.MaxValue);
            Assert.NotEqual(bufferLength, writer.Span.Length);

            writerBuffer.FlushAsync().GetAwaiter().GetResult();

            var reader = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
            var numAsString = ulong.MaxValue.ToString();
            var written = reader.Buffer.Slice(spacer.Length, numAsString.Length);
            Assert.False(written.IsSingleSpan, "The buffer should cross spans");
            AssertExtensions.Equal(Encoding.ASCII.GetBytes(numAsString), written.ToArray());
        }

        [Theory]
        [InlineData("\0abcxyz", new byte[] { 0, 97, 98, 99, 120, 121, 122 })]
        [InlineData("!#$%i", new byte[] { 33, 35, 36, 37, 105 })]
        [InlineData("!#$%", new byte[] { 33, 35, 36, 37 })]
        [InlineData("!#$", new byte[] { 33, 35, 36 })]
        [InlineData("!#", new byte[] { 33, 35 })]
        [InlineData("!", new byte[] { 33 })]
        // null or empty
        [InlineData("", new byte[0])]
        [InlineData(null, new byte[0])]
        public void EncodesAsAscii(string input, byte[] expected)
        {
            var writerBuffer = _pipe.Writer.Alloc();
            var writer = new WritableBufferWriter(writerBuffer);
            PipelineExtensions.WriteAsciiNoValidation(ref writer, input);
            writerBuffer.FlushAsync().GetAwaiter().GetResult();
            var reader = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();

            if (expected.Length > 0)
            {
                AssertExtensions.Equal(
                    expected,
                    reader.Buffer.ToArray());
            }
            else
            {
                Assert.Equal(0, reader.Buffer.Length);
            }
        }

        [Theory]
        // non-ascii characters stored in 32 bits
        [InlineData("𤭢𐐝")]
        // non-ascii characters stored in 16 bits
        [InlineData("ñ٢⛄⛵")]
        public void WriteAsciiNoValidationWritesOnlyOneBytePerChar(string input)
        {
            // WriteAscii doesn't validate if characters are in the ASCII range
            // but it shouldn't produce more than one byte per character
            var writerBuffer = _pipe.Writer.Alloc();
            var writer = new WritableBufferWriter(writerBuffer);
            PipelineExtensions.WriteAsciiNoValidation(ref writer, input);
            writerBuffer.FlushAsync().GetAwaiter().GetResult();
            var reader = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();

            Assert.Equal(input.Length, reader.Buffer.Length);
        }

        [Fact]
        public void WriteAsciiNoValidation()
        {
            const byte maxAscii = 0x7f;
            var writerBuffer = _pipe.Writer.Alloc();
            var writer = new WritableBufferWriter(writerBuffer);
            for (var i = 0; i < maxAscii; i++)
            {
                PipelineExtensions.WriteAsciiNoValidation(ref writer, new string((char)i, 1));
            }
            writerBuffer.FlushAsync().GetAwaiter().GetResult();

            var reader = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
            var data = reader.Buffer.Slice(0, maxAscii).ToArray();
            for (var i = 0; i < maxAscii; i++)
            {
                Assert.Equal(i, data[i]);
            }
        }

        [Theory]
        [InlineData(2, 1)]
        [InlineData(3, 1)]
        [InlineData(4, 2)]
        [InlineData(5, 3)]
        [InlineData(7, 4)]
        [InlineData(8, 3)]
        [InlineData(8, 4)]
        [InlineData(8, 5)]
        [InlineData(100, 48)]
        public void WritesAsciiAcrossBlockBoundaries(int stringLength, int gapSize)
        {
            var testString = new string(' ', stringLength);
            var writerBuffer = _pipe.Writer.Alloc(100);
            var writer = new WritableBufferWriter(writerBuffer);
            // almost fill up the first block
            var spacer = new byte[writer.Span.Length - gapSize];
            writer.Write(spacer);
            Assert.Equal(gapSize, writer.Span.Length);

            var bufferLength = writer.Span.Length;
            PipelineExtensions.WriteAsciiNoValidation(ref writer, testString);
            Assert.NotEqual(bufferLength, writer.Span.Length);

            writerBuffer.FlushAsync().GetAwaiter().GetResult();

            var reader = _pipe.Reader.ReadAsync().GetAwaiter().GetResult();
            var written = reader.Buffer.Slice(spacer.Length, stringLength);
            Assert.False(written.IsSingleSpan, "The buffer should cross spans");
            AssertExtensions.Equal(Encoding.ASCII.GetBytes(testString), written.ToArray());
        }
    }
}
