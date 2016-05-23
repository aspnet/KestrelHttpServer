// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class FrameResponseHeadersTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void InitialDictionaryContainsServerAndDate(bool addServerHeader)
        {
            var serverOptions = new KestrelServerOptions { AddServerHeader = addServerHeader };

            var connectionContext = new ConnectionContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000"),
                ServerOptions = serverOptions,
            };
            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();

            IDictionary<string, StringValues> headers = frame.ResponseHeaders;

            if (addServerHeader)
            {
                Assert.Equal(2, headers.Count);

                StringValues serverHeader;
                Assert.True(headers.TryGetValue("Server", out serverHeader));
                Assert.Equal(1, serverHeader.Count);
                Assert.Equal("Kestrel", serverHeader[0]);
            }
            else
            {
                Assert.Equal(1, headers.Count);

                StringValues serverHeader;
                Assert.False(headers.TryGetValue("Server", out serverHeader));
            }

            StringValues dateHeader;
            DateTime date;
            Assert.True(headers.TryGetValue("Date", out dateHeader));
            Assert.Equal(1, dateHeader.Count);
            Assert.True(DateTime.TryParse(dateHeader[0], out date));
            Assert.True(DateTime.Now - date <= TimeSpan.FromMinutes(1));

            Assert.False(headers.IsReadOnly);
        }

        [Fact]
        public void InitialEntriesCanBeCleared()
        {
            var serverOptions = new KestrelServerOptions();
            var connectionContext = new ConnectionContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerAddress = ServerAddress.FromUrl("http://localhost:5000"),
                ServerOptions = serverOptions,
            };
            var frame = new Frame<object>(application: null, context: connectionContext);
            frame.InitializeHeaders();

            Assert.True(frame.ResponseHeaders.Count > 0);

            frame.ResponseHeaders.Clear();

            Assert.Equal(0, frame.ResponseHeaders.Count);
            Assert.False(frame.ResponseHeaders.ContainsKey("Server"));
            Assert.False(frame.ResponseHeaders.ContainsKey("Date"));
        }

        [Theory]
        [InlineData("Server", "\r\nData")]
        [InlineData("Server", "\0Data")]
        [InlineData("Server", "Data\r")]
        [InlineData("Server", "Da\0ta")]
        [InlineData("Server", "Da\u001Fta")]
        [InlineData("Unknown-Header", "\r\nData")]
        [InlineData("Unknown-Header", "\0Data")]
        [InlineData("Unknown-Header", "Data\0")]
        [InlineData("Unknown-Header", "Da\nta")]
        [InlineData("\r\nServer", "Data")]
        [InlineData("Server\r", "Data")]
        [InlineData("Ser\0ver", "Data")]
        [InlineData("Server\r\n", "Data")]
        [InlineData("\u001FServer", "Data")]
        [InlineData("Unknown-Header\r\n", "Data")]
        [InlineData("\0Unknown-Header", "Data")]
        [InlineData("Unknown\r-Header", "Data")]
        [InlineData("Unk\nown-Header", "Data")]
        [InlineData("Cookie", "ZGVmYXVsdC1zcmM\0gJ25vbmUnOyBiYXNlLXVyaSAnc2VsZic7IGJsb2NrLWFsbC1taXhlZC1jb250ZW50OyBjaGlsZC1zcmMgcmVuZGVyLmdpdGh1YnVzZXJjb250ZW50LmNvbTsgY29ubmVjdC1zcmMgJ3NlbGYnIHVwbG9hZHMuZ2l0aHViLmNvbSBzdGF0dXMuZ2l0aHViLmNvbSBhcGkuZ2l0aHViLmNvbSB3d3cuZ29vZ2xlLWFuYWx5dGljcy5jb20gZ2l0aHViLWNsb3VkLnMzLmFtYXpvbmF3cy5jb20gYXBpLmJyYWludHJlZWdhdGV3YXkuY29tIGNsaWVudC1hbmFseXRpY3MuYnJhaW50cmVlZ2F0ZXdheS5jb20gd3NzOi8vbGl2ZS5naXRodWIuY29tOyBmb250LXNyYyBhc3NldHMtY2RuLmdpdGh1Yi5jb207IGZvcm0tYWN0aW9uICdzZWxmJyBnaXRodWIuY29tIGdpc3QuZ2l0aHViLmNvbTsgZnJhbWUtYW5jZXN0b3JzICdub25lJzsgZnJhbWUtc3JjIHJlbmRlci5naXRodWJ1c2VyY29udGVudC5jb207IGltZy1zcmMgJ3NlbGYnIGRhdGE6IGFzc2V0cy1jZG4uZ2l0aHViLmNvbSBpZGVudGljb25zLmdpdGh1Yi5jb20gd3d3Lmdvb2dsZS1hbmFseXRpY3MuY29tIGNvbGxlY3Rvci5naXRodWJhcHAuY29tICouZ3JhdmF0YXIuY29tICoud3AuY29tIGNoZWNrb3V0LnBheXBhbC5jb20gKi5naXRodWJ1c2VyY29udGVudC5jb207IG1lZGlhLXNyYyAnbm9uZSc7IG9iamVjdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBwbHVnaW4tdHlwZXMgYXBwbGljYXRpb24veC1zaG9ja3dhdmUtZmxhc2g7IHNjcmlwdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBzdHlsZS1zcmMgJ3Vuc2FmZS1pbmxpbmUnIGFzc2V0cy1jZG4uZ2l0aHViLmNvbTsgcmVwb3J0LXVyaSBodHRwczovL2FwaS5naXRodWIuY29tL19wcml2YXRlL2Jyb3dzZXIvZXJyb3Jz")]
        [InlineData("Cookie", "ZGVmYXVsdC1zcmMg\0J25vbmUnOyBiYXNlLXVyaSAnc2VsZic7IGJsb2NrLWFsbC1taXhlZC1jb250ZW50OyBjaGlsZC1zcmMgcmVuZGVyLmdpdGh1YnVzZXJjb250ZW50LmNvbTsgY29ubmVjdC1zcmMgJ3NlbGYnIHVwbG9hZHMuZ2l0aHViLmNvbSBzdGF0dXMuZ2l0aHViLmNvbSBhcGkuZ2l0aHViLmNvbSB3d3cuZ29vZ2xlLWFuYWx5dGljcy5jb20gZ2l0aHViLWNsb3VkLnMzLmFtYXpvbmF3cy5jb20gYXBpLmJyYWludHJlZWdhdGV3YXkuY29tIGNsaWVudC1hbmFseXRpY3MuYnJhaW50cmVlZ2F0ZXdheS5jb20gd3NzOi8vbGl2ZS5naXRodWIuY29tOyBmb250LXNyYyBhc3NldHMtY2RuLmdpdGh1Yi5jb207IGZvcm0tYWN0aW9uICdzZWxmJyBnaXRodWIuY29tIGdpc3QuZ2l0aHViLmNvbTsgZnJhbWUtYW5jZXN0b3JzICdub25lJzsgZnJhbWUtc3JjIHJlbmRlci5naXRodWJ1c2VyY29udGVudC5jb207IGltZy1zcmMgJ3NlbGYnIGRhdGE6IGFzc2V0cy1jZG4uZ2l0aHViLmNvbSBpZGVudGljb25zLmdpdGh1Yi5jb20gd3d3Lmdvb2dsZS1hbmFseXRpY3MuY29tIGNvbGxlY3Rvci5naXRodWJhcHAuY29tICouZ3JhdmF0YXIuY29tICoud3AuY29tIGNoZWNrb3V0LnBheXBhbC5jb20gKi5naXRodWJ1c2VyY29udGVudC5jb207IG1lZGlhLXNyYyAnbm9uZSc7IG9iamVjdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBwbHVnaW4tdHlwZXMgYXBwbGljYXRpb24veC1zaG9ja3dhdmUtZmxhc2g7IHNjcmlwdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBzdHlsZS1zcmMgJ3Vuc2FmZS1pbmxpbmUnIGFzc2V0cy1jZG4uZ2l0aHViLmNvbTsgcmVwb3J0LXVyaSBodHRwczovL2FwaS5naXRodWIuY29tL19wcml2YXRlL2Jyb3dzZXIvZXJyb3Jz")]
        [InlineData("Cookie", "ZGVmYXVsdC1zcmMgJ\025vbmUnOyBiYXNlLXVyaSAnc2VsZic7IGJsb2NrLWFsbC1taXhlZC1jb250ZW50OyBjaGlsZC1zcmMgcmVuZGVyLmdpdGh1YnVzZXJjb250ZW50LmNvbTsgY29ubmVjdC1zcmMgJ3NlbGYnIHVwbG9hZHMuZ2l0aHViLmNvbSBzdGF0dXMuZ2l0aHViLmNvbSBhcGkuZ2l0aHViLmNvbSB3d3cuZ29vZ2xlLWFuYWx5dGljcy5jb20gZ2l0aHViLWNsb3VkLnMzLmFtYXpvbmF3cy5jb20gYXBpLmJyYWludHJlZWdhdGV3YXkuY29tIGNsaWVudC1hbmFseXRpY3MuYnJhaW50cmVlZ2F0ZXdheS5jb20gd3NzOi8vbGl2ZS5naXRodWIuY29tOyBmb250LXNyYyBhc3NldHMtY2RuLmdpdGh1Yi5jb207IGZvcm0tYWN0aW9uICdzZWxmJyBnaXRodWIuY29tIGdpc3QuZ2l0aHViLmNvbTsgZnJhbWUtYW5jZXN0b3JzICdub25lJzsgZnJhbWUtc3JjIHJlbmRlci5naXRodWJ1c2VyY29udGVudC5jb207IGltZy1zcmMgJ3NlbGYnIGRhdGE6IGFzc2V0cy1jZG4uZ2l0aHViLmNvbSBpZGVudGljb25zLmdpdGh1Yi5jb20gd3d3Lmdvb2dsZS1hbmFseXRpY3MuY29tIGNvbGxlY3Rvci5naXRodWJhcHAuY29tICouZ3JhdmF0YXIuY29tICoud3AuY29tIGNoZWNrb3V0LnBheXBhbC5jb20gKi5naXRodWJ1c2VyY29udGVudC5jb207IG1lZGlhLXNyYyAnbm9uZSc7IG9iamVjdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBwbHVnaW4tdHlwZXMgYXBwbGljYXRpb24veC1zaG9ja3dhdmUtZmxhc2g7IHNjcmlwdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBzdHlsZS1zcmMgJ3Vuc2FmZS1pbmxpbmUnIGFzc2V0cy1jZG4uZ2l0aHViLmNvbTsgcmVwb3J0LXVyaSBodHRwczovL2FwaS5naXRodWIuY29tL19wcml2YXRlL2Jyb3dzZXIvZXJyb3Jz")]
        [InlineData("Cookie", "ZGVmYXVsdC1zcmMgJ25vbmUnOyBiYXNlLXVyaSAnc2VsZic7IGJsb2NrLWFsbC1taXhlZC1jb250ZW50OyBjaGlsZC1zcmMgcmVuZGVyLmdpdGh1YnVzZXJjb250ZW50LmNvbTsgY29ubmVjdC1zcmMgJ3NlbGYnIHVwbG9hZHMuZ2l0aHViLmNvbSBzdGF0dXMuZ2l0aHViLmNvbSBhcGkuZ2l0aHViLmNvbSB3d3cuZ29vZ2xlLWFuYWx5dGljcy5jb20gZ2l0aHViLWNsb3VkLnMzLmFtYXpvbmF3cy5jb20gYXBpLmJyYWludHJlZWdhdGV3YXkuY29tIGNsaWVudC1hbmFseXRpY3MuYnJhaW50cmVlZ2F0ZXdheS5jb20gd3NzOi8vbGl2ZS5naXRodWIuY29tOyBmb250LXNyYyBhc3NldHMtY2RuLmdpdGh1Yi5jb207IGZvcm0tYWN0aW9uICdzZWxmJyBnaXRodWIuY29tIGdpc3QuZ2l0aHViLmNvbTsgZnJhbWUtYW5jZXN0b3JzICdub25lJzsgZnJhbWUtc3JjIHJlbmRlci5naXRodWJ1c2VyY29udGVudC5jb207IGltZy1zcmMgJ3NlbGYnIGRhdGE6IGFzc2V0cy1jZG4uZ2l0aHViLmNvbSBpZGVudGljb25zLmdpdGh1Yi5jb20gd3d3Lmdvb2dsZS1hbmFseXRpY3MuY29tIGNvbGxlY3Rvci5naXRodWJhcHAuY29tICouZ3JhdmF0YXIuY29tICoud3AuY29tIGNoZWNrb3V0LnBheXBhbC5jb20gKi5naXRodWJ1c2VyY29udGVudC5jb207IG1lZGlhLXNyYyAnbm9uZSc7IG9iamVjdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBwbHVnaW4tdHlwZXMgYXBwbGljYXRpb24veC1zaG9ja3dhdmUtZmxhc2g7IHNjcmlwdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBzdHlsZS1zcmMgJ3Vuc2FmZS1pbmxpbmUnIGFzc2V0cy1jZG4uZ2l0aHViLmNvbTsgcmVwb3J0LXVyaSBodHRwczovL2FwaS5naXRodWIuY29tL19wcml2YXRlL2Jyb3dzZXIvZXJyb3Jz\0")]
        [InlineData("Content-Security-Polic\0y", "default-src 'none'; base-uri 'self'; block-all-mixed-content; child-src render.githubusercontent.com; connect-src 'self' uploads.github.com status.github.com api.github.com www.google-analytics.com github-cloud.s3.amazonaws.com api.braintreegateway.com client-analytics.braintreegateway.com wss://live.github.com; font-src assets-cdn.github.com; form-action 'self' github.com gist.github.com; frame-ancestors 'none'; frame-src render.githubusercontent.com; img-src 'self' data: assets-cdn.github.com identicons.github.com www.google-analytics.com collector.githubapp.com *.gravatar.com *.wp.com checkout.paypal.com *.githubusercontent.com; media-src 'none'; object-src assets-cdn.github.com; plugin-types application/x-shockwave-flash; script-src assets-cdn.github.com; style-src 'unsafe-inline' assets-cdn.github.com; report-uri https://api.github.com/_private/browser/errors")]
        [InlineData("Content-Securit\0y-Policy", "default-src 'none'; base-uri 'self'; block-all-mixed-content; child-src render.githubusercontent.com; connect-src 'self' uploads.github.com status.github.com api.github.com www.google-analytics.com github-cloud.s3.amazonaws.com api.braintreegateway.com client-analytics.braintreegateway.com wss://live.github.com; font-src assets-cdn.github.com; form-action 'self' github.com gist.github.com; frame-ancestors 'none'; frame-src render.githubusercontent.com; img-src 'self' data: assets-cdn.github.com identicons.github.com www.google-analytics.com collector.githubapp.com *.gravatar.com *.wp.com checkout.paypal.com *.githubusercontent.com; media-src 'none'; object-src assets-cdn.github.com; plugin-types application/x-shockwave-flash; script-src assets-cdn.github.com; style-src 'unsafe-inline' assets-cdn.github.com; report-uri https://api.github.com/_private/browser/errors")]
        [InlineData("Content-Security-Policy", "default-src 'none'; base-uri 'self'; \0block-all-mixed-content; child-src render.githubusercontent.com; connect-src 'self' uploads.github.com status.github.com api.github.com www.google-analytics.com github-cloud.s3.amazonaws.com api.braintreegateway.com client-analytics.braintreegateway.com wss://live.github.com; font-src assets-cdn.github.com; form-action 'self' github.com gist.github.com; frame-ancestors 'none'; frame-src render.githubusercontent.com; img-src 'self' data: assets-cdn.github.com identicons.github.com www.google-analytics.com collector.githubapp.com *.gravatar.com *.wp.com checkout.paypal.com *.githubusercontent.com; media-src 'none'; object-src assets-cdn.github.com; plugin-types application/x-shockwave-flash; script-src assets-cdn.github.com; style-src 'unsafe-inline' assets-cdn.github.com; report-uri https://api.github.com/_private/browser/errors")]
        [InlineData("Content-Security-Policy", "default-src 'none'; base-uri 'self'; block-all-mixed-content; child-src render.githubusercontent.com; connect-src 'self' uploads.github.com status.github.com api.github.com www.google-analytics.com github-cloud.s3.amazonaws.com api.braintreegateway.com client-analytics.braintreegateway.com wss://live.github.com; font-src assets-cdn.github.com; form-action 'self' github.com gist.github.com; frame-ancestors 'none'; frame-src render.githubusercontent.com; img-src 'self' data: assets-cdn.github.com identicons.github.com www.google-analytics.com collector.githubapp.com *.gravatar.com *.wp.com checkout.paypal.com *.githubusercontent.com; media-src 'none'; object-src assets-cdn.github.com; plugin-types application/x-shockwave-flash; script-src assets-cdn.github.com; style-src 'unsafe-inline' assets-cdn.github.com; report-uri https://api.github.com/_private/browser/errors\0")]
        public void AddingControlCharactersToHeadersThrows(string key, string value)
        {
            var responseHeaders = new FrameResponseHeaders();

            Assert.Throws<InvalidOperationException>(() => {
                ((IHeaderDictionary)responseHeaders)[key] = value;
            });

            Assert.Throws<InvalidOperationException>(() => {
                ((IHeaderDictionary)responseHeaders)[key] = new StringValues(new[] { "valid", value });
            });

            Assert.Throws<InvalidOperationException>(() => {
                ((IDictionary<string, StringValues>)responseHeaders)[key] = value;
            });

            Assert.Throws<InvalidOperationException>(() => {
                var kvp = new KeyValuePair<string, StringValues>(key, value);
                ((ICollection<KeyValuePair<string, StringValues>>)responseHeaders).Add(kvp);
            });

            Assert.Throws<InvalidOperationException>(() => {
                var kvp = new KeyValuePair<string, StringValues>(key, value);
                ((IDictionary<string, StringValues>)responseHeaders).Add(key, value);
            });
        }

        [Theory]
        [InlineData("Server", "Data")]
        [InlineData("Unknown-Header", "Data")]
        [InlineData("Cookie", "ZGVmYXVsdC1zcmMgJ25vbmUnOyBiYXNlLXVyaSAnc2VsZic7IGJsb2NrLWFsbC1taXhlZC1jb250ZW50OyBjaGlsZC1zcmMgcmVuZGVyLmdpdGh1YnVzZXJjb250ZW50LmNvbTsgY29ubmVjdC1zcmMgJ3NlbGYnIHVwbG9hZHMuZ2l0aHViLmNvbSBzdGF0dXMuZ2l0aHViLmNvbSBhcGkuZ2l0aHViLmNvbSB3d3cuZ29vZ2xlLWFuYWx5dGljcy5jb20gZ2l0aHViLWNsb3VkLnMzLmFtYXpvbmF3cy5jb20gYXBpLmJyYWludHJlZWdhdGV3YXkuY29tIGNsaWVudC1hbmFseXRpY3MuYnJhaW50cmVlZ2F0ZXdheS5jb20gd3NzOi8vbGl2ZS5naXRodWIuY29tOyBmb250LXNyYyBhc3NldHMtY2RuLmdpdGh1Yi5jb207IGZvcm0tYWN0aW9uICdzZWxmJyBnaXRodWIuY29tIGdpc3QuZ2l0aHViLmNvbTsgZnJhbWUtYW5jZXN0b3JzICdub25lJzsgZnJhbWUtc3JjIHJlbmRlci5naXRodWJ1c2VyY29udGVudC5jb207IGltZy1zcmMgJ3NlbGYnIGRhdGE6IGFzc2V0cy1jZG4uZ2l0aHViLmNvbSBpZGVudGljb25zLmdpdGh1Yi5jb20gd3d3Lmdvb2dsZS1hbmFseXRpY3MuY29tIGNvbGxlY3Rvci5naXRodWJhcHAuY29tICouZ3JhdmF0YXIuY29tICoud3AuY29tIGNoZWNrb3V0LnBheXBhbC5jb20gKi5naXRodWJ1c2VyY29udGVudC5jb207IG1lZGlhLXNyYyAnbm9uZSc7IG9iamVjdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBwbHVnaW4tdHlwZXMgYXBwbGljYXRpb24veC1zaG9ja3dhdmUtZmxhc2g7IHNjcmlwdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBzdHlsZS1zcmMgJ3Vuc2FmZS1pbmxpbmUnIGFzc2V0cy1jZG4uZ2l0aHViLmNvbTsgcmVwb3J0LXVyaSBodHRwczovL2FwaS5naXRodWIuY29tL19wcml2YXRlL2Jyb3dzZXIvZXJyb3Jz")]
        [InlineData("Content-Security-Policy", "default-src 'none'; base-uri 'self'; block-all-mixed-content; child-src render.githubusercontent.com; connect-src 'self' uploads.github.com status.github.com api.github.com www.google-analytics.com github-cloud.s3.amazonaws.com api.braintreegateway.com client-analytics.braintreegateway.com wss://live.github.com; font-src assets-cdn.github.com; form-action 'self' github.com gist.github.com; frame-ancestors 'none'; frame-src render.githubusercontent.com; img-src 'self' data: assets-cdn.github.com identicons.github.com www.google-analytics.com collector.githubapp.com *.gravatar.com *.wp.com checkout.paypal.com *.githubusercontent.com; media-src 'none'; object-src assets-cdn.github.com; plugin-types application/x-shockwave-flash; script-src assets-cdn.github.com; style-src 'unsafe-inline' assets-cdn.github.com; report-uri https://api.github.com/_private/browser/errors")]
        public void NoControlCharactersInHeadersDoesNotThrow(string key, string value)
        {
            var responseHeaders = new FrameResponseHeaders();

            ((IHeaderDictionary)responseHeaders)[key] = value;
            responseHeaders.Reset();
            ((IHeaderDictionary)responseHeaders)[key] = new StringValues(new[] { "valid", value });
            responseHeaders.Reset();

            ((IDictionary<string, StringValues>)responseHeaders)[key] = value;
            responseHeaders.Reset();

            {
                var kvp = new KeyValuePair<string, StringValues>(key, value);
                ((ICollection<KeyValuePair<string, StringValues>>)responseHeaders).Add(kvp);
                responseHeaders.Reset();
            }

            {
                var kvp = new KeyValuePair<string, StringValues>(key, value);
                ((IDictionary<string, StringValues>)responseHeaders).Add(key, value);
                responseHeaders.Reset();
            }
        }
    }
}
