// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class FrameRequestStreamTests
    {
        [Fact]
        public void CanReadReturnsTrue()
        {
            var stream = new FrameRequestStream();
            Assert.True(stream.CanRead);
        }

        [Fact]
        public void CanSeekReturnsFalse()
        {
            var stream = new FrameRequestStream();
            Assert.False(stream.CanSeek);
        }

        [Fact]
        public void CanWriteReturnsFalse()
        {
            var stream = new FrameRequestStream();
            Assert.False(stream.CanWrite);
        }

        [Fact]
        public void SeekThrows()
        {
            var stream = new FrameRequestStream();
            Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        }

        [Fact]
        public void LengthThrows()
        {
            var stream = new FrameRequestStream();
            Assert.Throws<NotSupportedException>(() => stream.Length);
        }

        [Fact]
        public void SetLengthThrows()
        {
            var stream = new FrameRequestStream();
            Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        }

        [Fact]
        public void PositionThrows()
        {
            var stream = new FrameRequestStream();
            Assert.Throws<NotSupportedException>(() => stream.Position);
            Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        }

        [Fact]
        public void WriteThrows()
        {
            var stream = new FrameRequestStream();
            Assert.Throws<NotSupportedException>(() => stream.Write(new byte[1], 0, 1));
        }

        [Fact]
        public void WriteByteThrows()
        {
            var stream = new FrameRequestStream();
            Assert.Throws<NotSupportedException>(() => stream.WriteByte(0));
        }

        [Fact]
        public async Task WriteAsyncThrows()
        {
            var stream = new FrameRequestStream();
            await Assert.ThrowsAsync<NotSupportedException>(() => stream.WriteAsync(new byte[1], 0, 1));
        }

#if NET46
        [Fact]
        public void BeginWriteThrows()
        {
            var stream = new FrameRequestStream();
            Assert.Throws<NotSupportedException>(() => stream.BeginWrite(new byte[1], 0, 1, null, null));
        }
#elif NETCOREAPP2_0
#else
#error Target framework needs to be updated
#endif

        [Fact]
        public void FlushDoesNotThrow()
        {
            var stream = new FrameRequestStream();
            stream.Flush();
        }

        [Fact]
        public async Task FlushAsyncDoesNotThrow()
        {
            var stream = new FrameRequestStream();
            await stream.FlushAsync();
        }

        [Fact]
        public void AbortCausesReadToCancel()
        {
            var stream = new FrameRequestStream();
            stream.StartAcceptingReads(null);
            stream.Abort();
            var task = stream.ReadAsync(new byte[1], 0, 1);
            Assert.True(task.IsCanceled);
        }

        [Fact]
        public void AbortWithErrorCausesReadToCancel()
        {
            var stream = new FrameRequestStream();
            stream.StartAcceptingReads(null);
            var error = new Exception();
            stream.Abort(error);
            var task = stream.ReadAsync(new byte[1], 0, 1);
            Assert.True(task.IsFaulted);
            Assert.Same(error, task.Exception.InnerException);
        }

        [Fact]
        public void StopAcceptingReadsCausesReadToThrowObjectDisposedException()
        {
            var stream = new FrameRequestStream();
            stream.StartAcceptingReads(null);
            stream.StopAcceptingReads();
            Assert.Throws<ObjectDisposedException>(() => { stream.ReadAsync(new byte[1], 0, 1); });
        }

        [Fact]
        public void AbortCausesCopyToAsyncToCancel()
        {
            var stream = new FrameRequestStream();
            stream.StartAcceptingReads(null);
            stream.Abort();
            var task = stream.CopyToAsync(Mock.Of<Stream>());
            Assert.True(task.IsCanceled);
        }

        [Fact]
        public void AbortWithErrorCausesCopyToAsyncToCancel()
        {
            var stream = new FrameRequestStream();
            stream.StartAcceptingReads(null);
            var error = new Exception();
            stream.Abort(error);
            var task = stream.CopyToAsync(Mock.Of<Stream>());
            Assert.True(task.IsFaulted);
            Assert.Same(error, task.Exception.InnerException);
        }

        [Fact]
        public void StopAcceptingReadsCausesCopyToAsyncToThrowObjectDisposedException()
        {
            var stream = new FrameRequestStream();
            stream.StartAcceptingReads(null);
            stream.StopAcceptingReads();
            Assert.Throws<ObjectDisposedException>(() => { stream.CopyToAsync(Mock.Of<Stream>()); });
        }

        [Fact]
        public void NullDestinationCausesCopyToAsyncToThrowArgumentNullException()
        {
            var stream = new FrameRequestStream();
            stream.StartAcceptingReads(null);
            Assert.Throws<ArgumentNullException>(() => { stream.CopyToAsync(null); });
        }

        [Fact]
        public void ZeroBufferSizeCausesCopyToAsyncToThrowArgumentException()
        {
            var stream = new FrameRequestStream();
            stream.StartAcceptingReads(null);
            Assert.Throws<ArgumentException>(() => { stream.CopyToAsync(Mock.Of<Stream>(), 0); });
        }

        [Theory]
        [InlineData(HttpVersion.Http10)]
        [InlineData(HttpVersion.Http11)]
        public async Task CanReadFromContentLength(HttpVersion httpVersion)
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(httpVersion, new FrameRequestHeaders { HeaderContentLength = "5" }, input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var stream = new FrameRequestStream();

                stream.StartAcceptingReads(reader);
                var readerTask = reader.StartAsync(body);

                input.Add("Hello");

                var buffer = new byte[1024];

                var count = stream.Read(buffer, 0, buffer.Length);
                Assert.Equal(5, count);
                AssertExtensions.Ascii("Hello", new ArraySegment<byte>(buffer, 0, count));

                await readerTask;

                count = stream.Read(buffer, 0, buffer.Length);
                Assert.Equal(0, count);
            }
        }

        [Theory]
        [InlineData(HttpVersion.Http10)]
        [InlineData(HttpVersion.Http11)]
        public async Task CanReadAsyncFromContentLength(HttpVersion httpVersion)
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(httpVersion, new FrameRequestHeaders { HeaderContentLength = "5" }, input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var stream = new FrameRequestStream();

                stream.StartAcceptingReads(reader);
                var readerTask = reader.StartAsync(body);

                input.Add("Hello");

                var buffer = new byte[1024];

                var count = await stream.ReadAsync(buffer, 0, buffer.Length);
                Assert.Equal(5, count);
                AssertExtensions.Ascii("Hello", new ArraySegment<byte>(buffer, 0, count));

                await readerTask;

                count = await stream.ReadAsync(buffer, 0, buffer.Length);
                Assert.Equal(0, count);
            }
        }

        [Fact]
        public async Task CanReadFromChunkedEncoding()
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(HttpVersion.Http11, new FrameRequestHeaders { HeaderTransferEncoding = "chunked" }, input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var stream = new FrameRequestStream();

                stream.StartAcceptingReads(reader);
                var readerTask = reader.StartAsync(body);

                input.Add("5\r\nHello\r\n");

                var buffer = new byte[1024];

                var count = stream.Read(buffer, 0, buffer.Length);
                Assert.Equal(5, count);
                AssertExtensions.Ascii("Hello", new ArraySegment<byte>(buffer, 0, count));

                input.Add("0\r\n\r\n");

                await readerTask;

                count = stream.Read(buffer, 0, buffer.Length);
                Assert.Equal(0, count);
            }
        }

        [Fact]
        public async Task CanReadAsyncFromChunkedEncoding()
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(HttpVersion.Http11, new FrameRequestHeaders { HeaderTransferEncoding = "chunked" }, input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var stream = new FrameRequestStream();

                stream.StartAcceptingReads(reader);
                var readerTask = reader.StartAsync(body);

                input.Add("5\r\nHello\r\n");

                var buffer = new byte[1024];

                var count = await stream.ReadAsync(buffer, 0, buffer.Length);
                Assert.Equal(5, count);
                AssertExtensions.Ascii("Hello", new ArraySegment<byte>(buffer, 0, count));

                input.Add("0\r\n\r\n");

                await readerTask;

                count = await stream.ReadAsync(buffer, 0, buffer.Length);
                Assert.Equal(0, count);
            }
        }

        [Theory]
        [InlineData(HttpVersion.Http10)]
        [InlineData(HttpVersion.Http11)]
        public void CanReadFromRemainingData(HttpVersion httpVersion)
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(httpVersion, new FrameRequestHeaders { HeaderConnection = "upgrade" }, input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var stream = new FrameRequestStream();

                stream.StartAcceptingReads(reader);
                var readerTask = reader.StartAsync(body);

                input.Add("Hello");

                var buffer = new byte[1024];

                var count = stream.Read(buffer, 0, buffer.Length);
                Assert.Equal(5, count);
                AssertExtensions.Ascii("Hello", new ArraySegment<byte>(buffer, 0, count));

                Assert.False(readerTask.IsCompleted);
            }
        }

        [Theory]
        [InlineData(HttpVersion.Http10)]
        [InlineData(HttpVersion.Http11)]
        public async Task CanReadAsyncFromRemainingData(HttpVersion httpVersion)
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(httpVersion, new FrameRequestHeaders { HeaderConnection = "upgrade" }, input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var stream = new FrameRequestStream();

                stream.StartAcceptingReads(reader);
                var readerTask  = reader.StartAsync(body);

                input.Add("Hello");

                var buffer = new byte[1024];

                var count = await stream.ReadAsync(buffer, 0, buffer.Length);
                Assert.Equal(5, count);
                AssertExtensions.Ascii("Hello", new ArraySegment<byte>(buffer, 0, count));

                Assert.False(readerTask.IsCompleted);
            }
        }

        [Theory]
        [InlineData("keep-alive, upgrade")]
        [InlineData("Keep-Alive, Upgrade")]
        [InlineData("upgrade, keep-alive")]
        [InlineData("Upgrade, Keep-Alive")]
        public void CanReadFromConnectionUpgradeKeepAlive(string headerConnection)
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(HttpVersion.Http11, new FrameRequestHeaders { HeaderConnection = headerConnection }, input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var readerTask = reader.StartAsync(body);

                var stream = new FrameRequestStream();
                stream.StartAcceptingReads(reader);

                input.Add("Hello");

                var buffer = new byte[1024];
                Assert.Equal(5, stream.Read(buffer, 0, buffer.Length));
                AssertExtensions.Ascii("Hello", new ArraySegment<byte>(buffer, 0, 5));
            }
        }

        [Theory]
        [InlineData("keep-alive, upgrade")]
        [InlineData("Keep-Alive, Upgrade")]
        [InlineData("upgrade, keep-alive")]
        [InlineData("Upgrade, Keep-Alive")]
        public async Task CanReadAsyncFromConnectionUpgradeKeepAlive(string headerConnection)
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(HttpVersion.Http11, new FrameRequestHeaders { HeaderConnection = headerConnection }, input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var readerTask = reader.StartAsync(body);

                var stream = new FrameRequestStream();
                stream.StartAcceptingReads(reader);

                input.Add("Hello");

                var buffer = new byte[1024];
                Assert.Equal(5, await stream.ReadAsync(buffer, 0, buffer.Length));
                AssertExtensions.Ascii("Hello", new ArraySegment<byte>(buffer, 0, 5));
            }
        }

        [Theory]
        [InlineData(HttpVersion.Http10)]
        [InlineData(HttpVersion.Http11)]
        public async Task ReadFromNoContentLengthReturnsZero(HttpVersion httpVersion)
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(httpVersion, new FrameRequestHeaders(), input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var stream = new FrameRequestStream();

                stream.StartAcceptingReads(reader);
                var readerTask = reader.StartAsync(body);

                input.Add("Hello");

                await readerTask;

                var buffer = new byte[1024];
                Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));
            }
        }

        [Theory]
        [InlineData(HttpVersion.Http10)]
        [InlineData(HttpVersion.Http11)]
        public async Task ReadAsyncFromNoContentLengthReturnsZero(HttpVersion httpVersion)
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(httpVersion, new FrameRequestHeaders(), input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var stream = new FrameRequestStream();

                stream.StartAcceptingReads(reader);
                var readerTask = reader.StartAsync(body);

                input.Add("Hello");

                await readerTask;

                var buffer = new byte[1024];
                Assert.Equal(0, await stream.ReadAsync(buffer, 0, buffer.Length));
            }
        }

        [Fact]
        public async Task CanHandleLargeBlocks()
        {
            using (var input = new TestInput())
            {
                var body = MessageBody.For(HttpVersion.Http10, new FrameRequestHeaders { HeaderContentLength = "8197" }, input.FrameContext);
                var reader = new RequestBodyReader(input.PipeFactory.Create());
                var stream = new FrameRequestStream();

                stream.StartAcceptingReads(reader);
                var readerTask = reader.StartAsync(body);

                // Input needs to be greater than 4032 bytes to allocate a block not backed by a slab.
                var largeInput = new string('a', 8192);

                input.Add(largeInput);
                // Add a smaller block to the end so that SocketInput attempts to return the large
                // block to the memory pool.
                input.Add("Hello");

                var ms = new MemoryStream();

                await stream.CopyToAsync(ms);
                var requestArray = ms.ToArray();
                Assert.Equal(8197, requestArray.Length);
                AssertExtensions.Ascii(largeInput + "Hello", new ArraySegment<byte>(requestArray, 0, requestArray.Length));

                await readerTask;

                var count = await stream.ReadAsync(new byte[1], 0, 1);
                Assert.Equal(0, count);
            }
        }
    }
}
