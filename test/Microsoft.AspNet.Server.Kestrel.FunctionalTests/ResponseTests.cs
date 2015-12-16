﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Testing.xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Microsoft.AspNet.Server.Kestrel.FunctionalTests
{
    public class ResponseTests
    {
        [ConditionalFact]
        [FrameworkSkipCondition(RuntimeFrameworks.Mono, SkipReason = "Test hangs after execution on mono.")]
        public async Task LargeDownload()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "server.urls", "http://localhost:8792/" }
                })
                .Build();

            var hostBuilder = new WebHostBuilder(config);
            hostBuilder.UseServerFactory("Microsoft.AspNet.Server.Kestrel");
            hostBuilder.UseStartup(app =>
            {
                app.Run(async context =>
                {
                    var bytes = new byte[1024];
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        bytes[i] = (byte)i;
                    }

                    context.Response.ContentLength = bytes.Length * 1024;

                    for (int i = 0; i < 1024; i++)
                    {
                        await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
                    }
                });
            });

            using (var app = hostBuilder.Build().Start())
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync("http://localhost:8792/");
                    response.EnsureSuccessStatusCode();
                    var responseBody = await response.Content.ReadAsStreamAsync();

                    // Read the full response body
                    var total = 0;
                    var bytes = new byte[1024];
                    var count = await responseBody.ReadAsync(bytes, 0, bytes.Length);
                    while (count > 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            Assert.Equal(total % 256, bytes[i]);
                            total++;
                        }
                        count = await responseBody.ReadAsync(bytes, 0, bytes.Length);
                    }
                }
            }
        }

        [ConditionalTheory, MemberData(nameof(NullHeaderData))]
        [FrameworkSkipCondition(RuntimeFrameworks.Mono, SkipReason = "Test hangs after execution on mono.")]
        public async Task IgnoreNullHeaderValues(string headerName, StringValues headerValue, string expectedValue)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "server.urls", "http://localhost:8793/" }
                })
                .Build();

            var hostBuilder = new WebHostBuilder(config)
                .UseServerFactory("Microsoft.AspNet.Server.Kestrel")
                .UseStartup(app =>
                {
                    app.Run(async context =>
                    {
                        context.Response.Headers.Add(headerName, headerValue);
                        
                        await context.Response.WriteAsync("");
                    });
                });

            using (var app = hostBuilder.Build().Start())
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync("http://localhost:8793/");
                    response.EnsureSuccessStatusCode();

                    var headers = response.Headers;

                    if (expectedValue == null)
                    {
                        Assert.False(headers.Contains(headerName));
                    }
                    else
                    {
                        Assert.True(headers.Contains(headerName));
                        Assert.Equal(headers.GetValues(headerName).Single(), expectedValue);
                    }
                }
            }
        }

        public static TheoryData<string, StringValues, string> NullHeaderData
        {
            get
            {
                var dataset = new TheoryData<string, StringValues, string>();

                // Unknown headers
                dataset.Add("NullString", (string)null, null);
                dataset.Add("EmptyString", "", "");
                dataset.Add("NullStringArray", new string[] { null }, null);
                dataset.Add("EmptyStringArray", new string[] { "" }, "");
                dataset.Add("MixedStringArray", new string[] { null, "" }, "");
                // Known headers
                dataset.Add("Location", (string)null, null);
                dataset.Add("Location", "", "");
                dataset.Add("Location", new string[] { null }, null);
                dataset.Add("Location", new string[] { "" }, "");
                dataset.Add("Location", new string[] { null, "" }, "");

                return dataset;
            }
        }

        [ConditionalFact]
        [FrameworkSkipCondition(RuntimeFrameworks.Mono, SkipReason = "Test hangs after execution on mono.")]
        public async Task PreEncodedHeaders()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "server.urls", "http://localhost:8794/" }
                })
                .Build();
            var knownHeaderName = "Location";
            var encodedKnownHeader = StringValues.CreatePreEncoded("somewhere", Encoding.ASCII);
            var unknownHeaderName = "foo";
            var encodedUnknownHeader = StringValues.CreatePreEncoded("bar", Encoding.ASCII);

            var hostBuilder = new WebHostBuilder(config);
            hostBuilder.UseServerFactory("Microsoft.AspNet.Server.Kestrel");
            hostBuilder.UseStartup(app =>
            {
                app.Run(async context =>
                {
                    context.Response.Headers[knownHeaderName] = encodedKnownHeader;
                    context.Response.Headers[unknownHeaderName] = encodedUnknownHeader;

                    await context.Response.WriteAsync("");
                });
            });

            using (var app = hostBuilder.Build().Start())
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync("http://localhost:8794/");
                    response.EnsureSuccessStatusCode();
                    var headers = response.Headers;

                    Assert.True(headers.Contains(knownHeaderName));
                    Assert.Equal(headers.GetValues(knownHeaderName).Single(), encodedKnownHeader);
                    Assert.True(headers.Contains(unknownHeaderName));
                    Assert.Equal(headers.GetValues(unknownHeaderName).Single(), encodedUnknownHeader);
                }
            }
        }
    }
}
