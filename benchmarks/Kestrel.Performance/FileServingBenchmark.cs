// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Testing;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    public class FileServingBenchmark
    {
        // Standard completed task
        private static readonly Func<object, Task> _syncTaskFunc = (obj) => Task.CompletedTask;
        // Non-standard completed task
        private static readonly Task _psuedoAsyncTask = Task.FromResult(27);
        private static readonly Func<object, Task> _psuedoAsyncTaskFunc = (obj) => _psuedoAsyncTask;

        private TestHttp1Connection _http1Connection;
        private IFeatureCollection _features;
        private DuplexPipe.DuplexPipePair _pair;
        private Task _reader;
        private CancellationTokenSource _cts;
        private MemoryPool<byte> _memoryPool;

        private string AbsoluteFilePath;

        [GlobalSetup]
        public void GlobalSetup()
        {
            AbsoluteFilePath = Path.Combine(Directory.GetCurrentDirectory(), "temp.txt");
            File.Delete(AbsoluteFilePath);

            var bytes = Encoding.ASCII.GetBytes(new string('a', 4096));
            using (var file = File.Create(AbsoluteFilePath, 4096, FileOptions.WriteThrough))
            {
                var remaining = Size;
                while (remaining > 0)
                {
                    file.Write(bytes, 0, bytes.Length);
                    remaining -= bytes.Length;
                }
                file.Flush();
            }

            _cts = new CancellationTokenSource();
            _memoryPool = KestrelMemoryPool.Create();
            _http1Connection = MakeHttp1Connection(_cts.Token);
            _features = _http1Connection;
        }

        [Params(128, 4096, 4096 * 1024)]
        public long Size { get; set; }

        [Params(false)]
        public bool Chunked { get; set; }

        [IterationSetup]
        public void Setup()
        {
            _http1Connection.Reset();
            if (Chunked)
            {
                _http1Connection.RequestHeaders.Add("Transfer-Encoding", "chunked");
            }
            else
            {
                _http1Connection.RequestHeaders.ContentLength = Size;
            }
        }

        private void ResetState()
        {
            _http1Connection.ResetState();
        }


        [Benchmark]
        public Task SendFile()
        {
            ResetState();

            var sendFile = _features.Get<IHttpSendFileFeature>();
            return sendFile.SendFileAsync(AbsoluteFilePath, 0, Size, CancellationToken.None);
        }

        [Benchmark]
        public async Task WriteAsync()
        {
            ResetState();

            byte[] buffer = new byte[4096];
            using (var file = new FileStream(AbsoluteFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                var read = 0;
                while ((read = await file.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    await _http1Connection.ResponseBody.WriteAsync(buffer, 0, read);
                }
            }
        }

        [Benchmark]
        public async Task CopyToAsync()
        {
            ResetState();

            using (var file = new FileStream(AbsoluteFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                await file.CopyToAsync(_http1Connection.ResponseBody);
            }
        }

        private TestHttp1Connection MakeHttp1Connection(CancellationToken token)
        {
            var options = new PipeOptions(_memoryPool, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false);
            var pair = DuplexPipe.CreateConnectionPair(options, options);
            _pair = pair;

            _reader = ReadAsync(token);

            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = new KestrelServerOptions(),
                Log = new MockTrace(),
                HttpParser = new HttpParser<Http1ParsingHandler>()
            };

            var http1Connection = new TestHttp1Connection(new Http1ConnectionContext
            {
                ServiceContext = serviceContext,
                ConnectionFeatures = new FeatureCollection(),
                MemoryPool = _memoryPool,
                Application = pair.Application,
                Transport = pair.Transport,
                TimeoutControl = new TimeoutControl()
            });

            http1Connection.Reset();
            http1Connection.InitializeStreams(MessageBody.ZeroContentLengthKeepAlive);

            return http1Connection;
        }

        public async Task ReadAsync(CancellationToken token)
        {
            var reader = _pair.Application.Input;
            while (!token.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(token);
                reader.AdvanceTo(result.Buffer.End);
            }
            _pair.Application.Input.Complete();
            _pair.Application.Output.Complete();
        }

        [GlobalCleanup]
        public void Dispose()
        {
            _cts.Cancel();
            _memoryPool?.Dispose();
        }

        public class TimeoutControl : ITimeoutControl
        {
            public void BytesRead(long count) { }
            public void CancelTimeout() { }
            public void PauseTimingReads() { }
            public void ResetTimeout(long ticks, TimeoutAction timeoutAction) { }
            public void ResumeTimingReads() { }
            public void SetTimeout(long ticks, TimeoutAction timeoutAction) { }
            public void StartTimingReads() { }
            public void StartTimingWrite(long size) { }
            public void StopTimingReads() { }
            public void StopTimingWrite() { }
        }
    }
}
