// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.Extensions.Internal;
using Moq;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class RequestBodyReaderTests
    {
        [Theory]
        [MemberData(nameof(RequestData))]
        public async Task CopyToAsyncDoesNotCopyBlocks(FrameRequestHeaders headers, string[] data)
        {
            var writeCount = 0;
            var writeTcs = new TaskCompletionSource<byte[]>();

            var mockDestination = new Mock<Stream>();
            mockDestination
                .Setup(m => m.WriteAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), CancellationToken.None))
                .Callback((byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                {
                    writeTcs.SetResult(buffer);
                    writeCount++;
                })
                .Returns(TaskCache.CompletedTask);

            using (var input = new TestInput())
            {
                var body = MessageBody.For(HttpVersion.Http11, headers, input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var readerTask = reader.StartAsync(body);

                var copyToAsyncTask = reader.CopyToAsync(mockDestination.Object);

                var pipe = input.Pipe;
                var bytes = Encoding.ASCII.GetBytes(data[0]);
                var buffer = pipe.Writer.Alloc(2048);
                Assert.True(buffer.Buffer.TryGetArray(out var block));
                Buffer.BlockCopy(bytes, 0, block.Array, block.Offset, bytes.Length);
                buffer.Advance(bytes.Length);
                await buffer.FlushAsync();

                // Verify the block passed to WriteAsync is the same one incoming data was written into.
                Assert.Same(block.Array, await writeTcs.Task);

                writeTcs = new TaskCompletionSource<byte[]>();
                bytes = Encoding.ASCII.GetBytes(data[1]);
                buffer = pipe.Writer.Alloc(2048);
                Assert.True(buffer.Buffer.TryGetArray(out block));
                Buffer.BlockCopy(bytes, 0, block.Array, block.Offset, bytes.Length);
                buffer.Advance(bytes.Length);
                await buffer.FlushAsync();

                Assert.Same(block.Array, await writeTcs.Task);

                if (headers.HeaderConnection == "close")
                {
                    pipe.Writer.Complete();
                }

                await copyToAsyncTask;
                await readerTask;

                Assert.Equal(2, writeCount);
            }
        }

        [Theory]
        [MemberData(nameof(CombinedData))]
        public async Task CopyToAsyncAdvancesRequestStreamWhenDestinationWriteAsyncThrows(Stream writeStream, FrameRequestHeaders headers, string[] data)
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(HttpVersion.Http11, headers, input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var readerTask = reader.StartAsync(body);

                input.Add(data[0]);

                await Assert.ThrowsAsync<XunitException>(() => reader.CopyToAsync(writeStream));

                input.Add(data[1]);

                // "Hello " should have been consumed
                var readBuffer = new byte[6];
                var count = await reader.ReadAsync(new ArraySegment<byte>(readBuffer, 0, readBuffer.Length));
                Assert.Equal(6, count);
                AssertExtensions.Ascii("World!", new ArraySegment<byte>(readBuffer, 0, 6));

                await readerTask;

                count = await reader.ReadAsync(new ArraySegment<byte>(readBuffer, 0, readBuffer.Length));
                Assert.Equal(0, count);
            }
        }

        public static IEnumerable<object[]> StreamData => new[]
        {
            new object[] { new ThrowOnWriteSynchronousStream() },
            new object[] { new ThrowOnWriteAsynchronousStream() },
        };

        public static IEnumerable<object[]> RequestData => new[]
        {
            // Content-Length
            new object[] { new FrameRequestHeaders { HeaderContentLength = "12" }, new[] { "Hello ", "World!" } },
            // Chunked
            new object[] { new FrameRequestHeaders { HeaderTransferEncoding = "chunked" }, new[] { "6\r\nHello \r\n", "6\r\nWorld!\r\n0\r\n\r\n" } },
        };

        public static IEnumerable<object[]> CombinedData =>
            from stream in StreamData
            from request in RequestData
            select new[] { stream[0], request[0], request[1] };

        private class ThrowOnWriteSynchronousStream : Stream
        {
            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                throw new XunitException();
            }

            public override bool CanRead { get; }
            public override bool CanSeek { get; }
            public override bool CanWrite => true;
            public override long Length { get; }
            public override long Position { get; set; }
        }

        private class ThrowOnWriteAsynchronousStream : Stream
        {
            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await Task.Delay(1);
                throw new XunitException();
            }

            public override bool CanRead { get; }
            public override bool CanSeek { get; }
            public override bool CanWrite => true;
            public override long Length { get; }
            public override long Position { get; set; }
        }
    }
}
