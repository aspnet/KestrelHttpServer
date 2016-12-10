// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class ResponseHeaders
    {
        private const int InnerLoopCount = 512;

        private static FrameResponseHeaders HeadersDirect;
        private static DateHeaderValueManager DateHeaderValueManager = new DateHeaderValueManager();

        private MemoryPool MemoryPool;
        private SocketInput SocketInput;
        private HostingApplication Application;
        private HostingApplication.Context Context;

        [Params("ContentLengthNumeric", "ContentLengthString", "Plaintext", "Primary", "Common", "Unknown")]
        public string Type { get; set; }

        private RequestDelegate GetRequestDelegate(string type)
        {
            switch (type)
            {
                case "Plaintext":
                    return Plaintext;
                case "ContentLengthString":
                    return ContentLengthString;
                case "ContentLengthNumeric":
                    return ContentLengthNumeric;
                case "Primary":
                    return Primary;
                case "Common":
                    return Common;
                case "Unknown":
                    return Unknown;
            }

            return Plaintext;
        }

        [Benchmark(OperationsPerInvoke = InnerLoopCount)]
        public async Task BenchmarkAsync()
        {
            for (int i = 0; i < InnerLoopCount; i++)
            {
                await Application.ProcessRequestAsync(Context);
            }
        }

        private static readonly RequestDelegate Plaintext = (context) =>
        {
            HeadersDirect.Reset();

            var response = context.Response;
            response.StatusCode = 200;
            response.ContentType = "text/plain";
            response.Headers["Content-Length"] = "13";

            var dateHeaderValues = DateHeaderValueManager.GetDateHeaderValues();
            HeadersDirect.SetRawDate(dateHeaderValues.String, dateHeaderValues.Bytes);
            return Task.CompletedTask;
        };

        private static readonly RequestDelegate Primary = (context) =>
        {
            HeadersDirect.Reset();

            var response = context.Response;
            response.StatusCode = 200;
            response.ContentType = "text/plain";
            response.Headers["Connection"] = "Close";
            response.Headers["Content-Length"] = "13";
            response.Headers["Server"] = "Kestrel";

            var dateHeaderValues = DateHeaderValueManager.GetDateHeaderValues();
            HeadersDirect.SetRawDate(dateHeaderValues.String, dateHeaderValues.Bytes);
            return Task.CompletedTask;
        };

        private static readonly RequestDelegate Common = (context) =>
        {
            HeadersDirect.Reset();

            var response = context.Response;
            response.StatusCode = 200;
            response.ContentType = "text/plain";
            response.Headers["Connection"] = "Close";
            response.Headers["Content-Length"] = "13";
            response.Headers["Server"] = "Kestrel";
            response.Headers["Cache-Control"] = "Test Value";
            response.Headers["Keep-Alive"] = "Test Value";
            response.Headers["Pragma"] = "Test Value";
            response.Headers["Trailer"] = "Test Value";
            response.Headers["Transfer-Encoding"] = "Test Value";
            response.Headers["Upgrade"] = "Test Value";
            response.Headers["Via"] = "Test Value";
            response.Headers["Warning"] = "Test Value";
            response.Headers["Allow"] = "Test Value";
            response.Headers["Content-Encoding"] = "Test Value";
            response.Headers["Content-Language"] = "Test Value";
            response.Headers["Content-Location"] = "Test Value";
            response.Headers["Content-MD5"] = "Test Value";
            response.Headers["Content-Range"] = "Test Value";
            response.Headers["Expires"] = "Test Value";
            response.Headers["Last-Modified"] = "Test Value";
            var dateHeaderValues = DateHeaderValueManager.GetDateHeaderValues();
            HeadersDirect.SetRawDate(dateHeaderValues.String, dateHeaderValues.Bytes);
            return Task.CompletedTask;
        };

        private static readonly RequestDelegate Unknown = (context) =>
        {
            HeadersDirect.Reset();

            var response = context.Response;
            response.StatusCode = 200;
            response.ContentType = "text/plain";
            response.Headers["Content-Length"] = "13";

            response.Headers["Unknown"] = "Unknown";
            response.Headers["IUnknown"] = "Unknown";
            response.Headers["X-Unknown"] = "Unknown";

            var dateHeaderValues = DateHeaderValueManager.GetDateHeaderValues();
            HeadersDirect.SetRawDate(dateHeaderValues.String, dateHeaderValues.Bytes);
            return Task.CompletedTask;
        };

        private static readonly RequestDelegate ContentLengthString = (context) =>
        {
            var response = context.Response;
            response.Headers["Content-Length"] = "13";
            return Task.CompletedTask;
        };

        private static readonly RequestDelegate ContentLengthNumeric = (context) =>
        {
            var response = context.Response;
            response.ContentLength = 13;
            return Task.CompletedTask;
        };

        [Setup]
        public void Setup()
        {
            var trace = new KestrelTrace(new TestKestrelTrace());
            var threadPool = new LoggingThreadPool(trace);

            MemoryPool = new MemoryPool();
            SocketInput = new SocketInput(MemoryPool, threadPool);

            var connectionContext = new MockConnection(new KestrelServerOptions());
            connectionContext.Input = SocketInput;

            var httpContextFactory = new HttpContextFactory(new DefaultObjectPoolProvider(), Options.Create(new FormOptions()));

            var testType = GetRequestDelegate(Type);

            Application = new HostingApplication(testType, new NullScopeLogger(), new NoopDiagnosticSource(), httpContextFactory);

            var frame = new Frame<HostingApplication.Context>(application: Application, context: connectionContext);
            frame.Reset();
            frame.InitializeHeaders();
            HeadersDirect = (FrameResponseHeaders)frame.ResponseHeaders;
            Context = Application.CreateContext(frame);
        }

        [Cleanup]
        public void Cleanup()
        {
            Application.DisposeContext(Context, null);
            SocketInput.IncomingFin();
            SocketInput.Dispose();
            MemoryPool.Dispose();
        }

        private class NullScopeLogger : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => false;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
            }
        }

        private class NoopDiagnosticSource : DiagnosticSource
        {
            public override bool IsEnabled(string name) => false;

            public override void Write(string name, object value)
            {
            }
        }
    }
}
