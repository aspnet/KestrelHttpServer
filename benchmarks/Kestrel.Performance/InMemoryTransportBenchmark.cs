﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    public class InMemoryTransportBenchmark
    {
        private const int _plaintextExpectedResponseLength = 132;
        private const int _plaintextPipelinedExpectedResponseLength = _plaintextExpectedResponseLength * RequestParsingData.Pipelining;

        private IWebHost _host;
        private InMemoryConnection _connection;

        [GlobalSetup(Target = nameof(Plaintext) + "," + nameof(PlaintextPipelined))]
        public void GlobalSetupPlaintext()
        {
            var transportFactory = new InMemoryTransportFactory(connectionsPerEndPoint: 1);

            _host = new WebHostBuilder()
                // Prevent VS from attaching to hosting startup which could impact results
                .UseSetting("preventHostingStartup", "true")
                .UseKestrel()
                // Bind to a single non-HTTPS endpoint
                .UseUrls("http://127.0.0.1:5000")
                .ConfigureServices(services => services.AddSingleton<ITransportFactory>(transportFactory))
                .Configure(app => app.UseMiddleware<PlaintextMiddleware>())
                .Build();

            _host.Start();

            // Ensure there is a single endpoint and single connection
            _connection = transportFactory.Connections.Values.Single().Single();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _host.Dispose();
        }

        [Benchmark]
        public async Task Plaintext()
        {
            await _connection.SendRequestAsync(RequestParsingData.PlaintextTechEmpowerRequest);
            await _connection.GetResponseAsync(_plaintextExpectedResponseLength);
        }

        [Benchmark(OperationsPerInvoke = RequestParsingData.Pipelining)]
        public async Task PlaintextPipelined()
        {
            await _connection.SendRequestAsync(RequestParsingData.PlaintextTechEmpowerPipelinedRequests);
            await _connection.GetResponseAsync(_plaintextPipelinedExpectedResponseLength);
        }

        public class InMemoryTransportFactory : ITransportFactory
        {
            private readonly int _connectionsPerEndPoint;

            private readonly Dictionary<IEndPointInformation, IReadOnlyList<InMemoryConnection>> _connections =
                new Dictionary<IEndPointInformation, IReadOnlyList<InMemoryConnection>>();

            public IReadOnlyDictionary<IEndPointInformation, IReadOnlyList<InMemoryConnection>> Connections => _connections;

            public InMemoryTransportFactory(int connectionsPerEndPoint)
            {
                _connectionsPerEndPoint = connectionsPerEndPoint;
            }

            public ITransport Create(IEndPointInformation endPointInformation, IConnectionHandler handler)
            {
                var connections = new InMemoryConnection[_connectionsPerEndPoint];
                for (var i = 0; i < _connectionsPerEndPoint; i++)
                {
                    connections[i] = new InMemoryConnection();
                }

                _connections.Add(endPointInformation, connections);

                return new InMemoryTransport(handler, connections);
            }
        }

        public class InMemoryTransport : ITransport
        {
            private readonly IConnectionHandler _handler;
            private readonly IReadOnlyList<InMemoryConnection> _connections;

            public InMemoryTransport(IConnectionHandler handler, IReadOnlyList<InMemoryConnection> connections)
            {
                _handler = handler;
                _connections = connections;
            }

            public Task BindAsync()
            {
                foreach (var connection in _connections)
                {
                    _handler.OnConnection(connection);
                }

                return Task.CompletedTask;
            }

            public Task StopAsync()
            {
                return Task.CompletedTask;
            }

            public Task UnbindAsync()
            {
                return Task.CompletedTask;
            }
        }

        public class InMemoryConnection : TransportConnection
        {
            public Task SendRequestAsync(byte[] request)
            {
                return Input.WriteAsync(request);
            }

            public async Task<byte[]> GetResponseAsync(int length)
            {
                while (true)
                {
                    var result = await Output.ReadAsync();
                    var buffer = result.Buffer;
                    var consumed = buffer.Start;
                    var examined = buffer.End;

                    try
                    {
                        if (buffer.Length >= length)
                        {
                            var response = buffer.Slice(0, length);
                            consumed = response.End;
                            examined = response.End;
                            return response.ToArray();
                        }
                    }
                    finally
                    {
                        Output.AdvanceTo(consumed, examined);
                    }
                }
            }
        }

        // Copied from https://github.com/aspnet/benchmarks/blob/dev/src/Benchmarks/Middleware/PlaintextMiddleware.cs
        public class PlaintextMiddleware
        {
            private static readonly PathString _path = new PathString("/plaintext");
            private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");

            private readonly RequestDelegate _next;

            public PlaintextMiddleware(RequestDelegate next)
            {
                _next = next;
            }

            public Task Invoke(HttpContext httpContext)
            {
                if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
                {
                    return WriteResponse(httpContext.Response);
                }

                return _next(httpContext);
            }

            public static Task WriteResponse(HttpResponse response)
            {
                var payloadLength = _helloWorldPayload.Length;
                response.StatusCode = 200;
                response.ContentType = "text/plain";
                response.ContentLength = payloadLength;
                return response.Body.WriteAsync(_helloWorldPayload, 0, payloadLength);
            }
        }

    }
}
