﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class RequestTargetProcessingTests
    {
        [Fact]
        public async Task RequestPathIsNotNormalized()
        {
            var testContext = new TestServiceContext();
            var listenOptions = new ListenOptions(new IPEndPoint(IPAddress.Loopback, 0));

            using (var server = new TestServer(async context =>
            {
                Assert.Equal("/\u0041\u030A/B/\u0041\u030A", context.Request.Path.Value);

                context.Response.Headers.ContentLength = 11;
                await context.Response.WriteAsync("Hello World");
            }, testContext, listenOptions))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        "GET /%41%CC%8A/A/../B/%41%CC%8A HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 11",
                        "",
                        "Hello World");
                }
            }
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/.")]
        [InlineData("/..")]
        [InlineData("/./.")]
        [InlineData("/./..")]
        [InlineData("/../.")]
        [InlineData("/../..")]
        [InlineData("/path")]
        [InlineData("/path?foo=1&bar=2")]
        [InlineData("/hello%20world")]
        [InlineData("/hello%20world?foo=1&bar=2")]
        [InlineData("/base/path")]
        [InlineData("/base/path?foo=1&bar=2")]
        [InlineData("/base/hello%20world")]
        [InlineData("/base/hello%20world?foo=1&bar=2")]
        public async Task RequestFeatureContainsRawTarget(string requestTarget)
        {
            var testContext = new TestServiceContext();

            using (var server = new TestServer(async context =>
            {
                Assert.Equal(requestTarget, context.Features.Get<IHttpRequestFeature>().RawTarget);

                context.Response.Headers["Content-Length"] = new[] { "11" };
                await context.Response.WriteAsync("Hello World");
            }, testContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        $"GET {requestTarget} HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 11",
                        "",
                        "Hello World");
                }
            }
        }

        [Theory]
        [InlineData(HttpMethod.Options, "*")]
        [InlineData(HttpMethod.Connect, "host")]
        public async Task NonPathRequestTargetSetInRawTarget(HttpMethod method, string requestTarget)
        {
            var testContext = new TestServiceContext();

            using (var server = new TestServer(async context =>
            {
                Assert.Equal(requestTarget, context.Features.Get<IHttpRequestFeature>().RawTarget);
                Assert.Empty(context.Request.Path.Value);
                Assert.Empty(context.Request.PathBase.Value);
                Assert.Empty(context.Request.QueryString.Value);

                context.Response.Headers["Content-Length"] = new[] { "11" };
                await context.Response.WriteAsync("Hello World");
            }, testContext))
            {
                using (var connection = server.CreateConnection())
                {
                    await connection.Send(
                        $"{HttpUtilities.MethodToString(method)} {requestTarget} HTTP/1.1",
                        "",
                        "");
                    await connection.ReceiveEnd(
                        "HTTP/1.1 200 OK",
                        $"Date: {testContext.DateHeaderValue}",
                        "Content-Length: 11",
                        "",
                        "Hello World");
                }
            }
        }
    }
}
