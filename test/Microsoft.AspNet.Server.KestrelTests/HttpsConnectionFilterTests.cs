﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if DNX451
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Http.Features;
using Microsoft.AspNet.Server.Kestrel.Filter;
using Microsoft.AspNet.Server.Kestrel.Https;
using Microsoft.AspNet.Testing.xunit;
using Xunit;

namespace Microsoft.AspNet.Server.KestrelTests
{
    public class HttpsConnectionFilterTests
    {
        private async Task App(HttpContext httpContext)
        {
            var request = httpContext.Request;
            var response = httpContext.Response;
            response.Headers.Clear();
            while (true)
            {
                var buffer = new byte[8192];
                var count = await request.Body.ReadAsync(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    break;
                }
                await response.Body.WriteAsync(buffer, 0, count);
            }
        }

        // https://github.com/aspnet/KestrelHttpServer/issues/240
        // This test currently fails on mono because of an issue with SslStream.
        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Linux)]
        [OSSkipCondition(OperatingSystems.MacOSX)]
        public async Task CanReadAndWriteWithHttpsConnectionFilter()
        {
            RemoteCertificateValidationCallback validationCallback =
                    (sender, cert, chain, sslPolicyErrors) => true;

            try
            {
#if DNX451
                var handler = new HttpClientHandler();
                ServicePointManager.ServerCertificateValidationCallback += validationCallback;
#else
                var handler = new WinHttpHandler();
                handler.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
#endif

                var serverAddress = "https://localhost:54321/";
                var serviceContext = new TestServiceContext()
                {
                    ConnectionFilter = new HttpsConnectionFilter(
                        new HttpsConnectionFilterOptions
                        { ServerCertificate = new X509Certificate2(@"TestResources/testCert.pfx", "testPassword")},
                        new NoOpConnectionFilter())
                };

                using (var server = new TestServer(App, serviceContext, serverAddress))
                {
                    using (var client = new HttpClient(handler))
                    {
                        var result = await client.PostAsync(serverAddress, new FormUrlEncodedContent(new[] {
                            new KeyValuePair<string, string>("content", "Hello World?")
                        }));

                        Assert.Equal("content=Hello+World%3F", await result.Content.ReadAsStringAsync());
                    }
                }
            }
            finally
            {
#if DNX451
                ServicePointManager.ServerCertificateValidationCallback -= validationCallback;
#endif
            }
        }

        // https://github.com/aspnet/KestrelHttpServer/issues/240
        // This test currently fails on mono because of an issue with SslStream.
        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Linux)]
        [OSSkipCondition(OperatingSystems.MacOSX)]
        public async Task RequireCertificateFailsWhenNoCertificate()
        {
            RemoteCertificateValidationCallback validationCallback =
                    (sender, cert, chain, sslPolicyErrors) => true;

            try
            {
#if DNX451
                var handler = new HttpClientHandler();
                ServicePointManager.ServerCertificateValidationCallback += validationCallback;
#else
                var handler = new WinHttpHandler();
                handler.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
#endif

                var serverAddress = "https://localhost:54321/";
                var serviceContext = new TestServiceContext()
                {
                    ConnectionFilter = new HttpsConnectionFilter(
                        new HttpsConnectionFilterOptions
                        {
                            ServerCertificate = new X509Certificate2(@"TestResources/testCert.pfx", "testPassword"),
                            ClientCertificateMode = ClientCertificateMode.RequireCertificate
                        },
                        new NoOpConnectionFilter())
                };

                using (var server = new TestServer(App, serviceContext, serverAddress))
                {
                    using (var client = new HttpClient())
                    {
                        await Assert.ThrowsAnyAsync<Exception>(
                            () => client.GetAsync(serverAddress));
                    }
                }
            }
            finally
            {
#if DNX451
                ServicePointManager.ServerCertificateValidationCallback -= validationCallback;
#endif
            }
        }

        // https://github.com/aspnet/KestrelHttpServer/issues/240
        // This test currently fails on mono because of an issue with SslStream.
        [ConditionalFact]
        [OSSkipCondition(OperatingSystems.Linux)]
        [OSSkipCondition(OperatingSystems.MacOSX)]
        public async Task AllowCertificateContinuesWhenNoCertificate()
        {
            RemoteCertificateValidationCallback validationCallback =
                    (sender, cert, chain, sslPolicyErrors) => true;

            try
            {
#if DNX451
                var handler = new HttpClientHandler();
                ServicePointManager.ServerCertificateValidationCallback += validationCallback;
#else
                var handler = new WinHttpHandler();
                handler.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
#endif

                var serverAddress = "https://localhost:54321/";
                var serviceContext = new TestServiceContext()
                {
                    ConnectionFilter = new HttpsConnectionFilter(
                        new HttpsConnectionFilterOptions
                        {
                            ServerCertificate = new X509Certificate2(@"TestResources/testCert.pfx", "testPassword"),
                            ClientCertificateMode = ClientCertificateMode.AllowCertificate
                        },
                        new NoOpConnectionFilter())
                };

                RequestDelegate app = context =>
                {
                    Assert.Equal(context.Features.Get<ITlsConnectionFeature>(), null);
                    return context.Response.WriteAsync("hello world");
                };

                using (var server = new TestServer(app, serviceContext, serverAddress))
                {
                    using (var client = new HttpClient())
                    {
                        var result = await client.GetAsync(serverAddress);

                        Assert.Equal("hello world", await result.Content.ReadAsStringAsync());
                    }
                }
            }
            finally
            {
#if DNX451
                ServicePointManager.ServerCertificateValidationCallback -= validationCallback;
#endif
            }
        }
    }
}
