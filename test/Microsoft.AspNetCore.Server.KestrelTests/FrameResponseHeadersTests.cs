// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Primitives;
using Xunit;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Server.KestrelTests
{
    public class FrameResponseHeadersTests
    {
        [Fact]
        public void InitialDictionaryIsEmpty()
        {
            var serverOptions = new KestrelServerOptions();

            var serviceContext = new ServiceContext
            {
                DateHeaderValueManager = new DateHeaderValueManager(),
                ServerOptions = serverOptions
            };
            var listenerContext = new ListenerContext(serviceContext)
            {
                ListenOptions = new ListenOptions(new IPEndPoint(IPAddress.Loopback, 5000))
            };
            var connectionContext = new ConnectionContext(listenerContext);

            var frame = new Frame<object>(application: null, context: connectionContext);

            frame.InitializeHeaders();

            IDictionary<string, StringValues> headers = frame.ResponseHeaders;

            Assert.Equal(0, headers.Count);
            Assert.False(headers.IsReadOnly);
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
        [InlineData("\u0000Server", "Data")]
        [InlineData("Server", "Data\u0000")]
        [InlineData("\u001FServer", "Data")]
        [InlineData("Unknown-Header\r\n", "Data")]
        [InlineData("\0Unknown-Header", "Data")]
        [InlineData("Unknown\r-Header", "Data")]
        [InlineData("Unk\nown-Header", "Data")]
        [InlineData("Server", "Da\u007Fta")]
        [InlineData("Unknown\u007F-Header", "Data")]
        [InlineData("Ser\u0080ver", "Data")]
        [InlineData("Server", "Da\u0080ta")]
        [InlineData("Unknown\u0080-Header", "Data")]
        [InlineData("Ser™ver", "Data")]
        [InlineData("Server", "Da™ta")]
        [InlineData("Unknown™-Header", "Data")]
        [InlineData("Ser™ver", "Data")]
        [InlineData("šerver", "Data")]
        [InlineData("Server", "Dašta")]
        [InlineData("Unknownš-Header", "Data")]
        [InlineData("Seršver", "Data")]
        [InlineData("Cookie", "ZGVmYXVsdC1zcmM\0gJ25vbmUnOyBiYXNlLXVyaSAnc2VsZic7IGJsb2NrLWFsbC1taXhlZC1jb250ZW50OyBjaGlsZC1zcmMgcmVuZGVyLmdpdGh1YnVzZXJjb250ZW50LmNvbTsgY29ubmVjdC1zcmMgJ3NlbGYnIHVwbG9hZHMuZ2l0aHViLmNvbSBzdGF0dXMuZ2l0aHViLmNvbSBhcGkuZ2l0aHViLmNvbSB3d3cuZ29vZ2xlLWFuYWx5dGljcy5jb20gZ2l0aHViLWNsb3VkLnMzLmFtYXpvbmF3cy5jb20gYXBpLmJyYWludHJlZWdhdGV3YXkuY29tIGNsaWVudC1hbmFseXRpY3MuYnJhaW50cmVlZ2F0ZXdheS5jb20gd3NzOi8vbGl2ZS5naXRodWIuY29tOyBmb250LXNyYyBhc3NldHMtY2RuLmdpdGh1Yi5jb207IGZvcm0tYWN0aW9uICdzZWxmJyBnaXRodWIuY29tIGdpc3QuZ2l0aHViLmNvbTsgZnJhbWUtYW5jZXN0b3JzICdub25lJzsgZnJhbWUtc3JjIHJlbmRlci5naXRodWJ1c2VyY29udGVudC5jb207IGltZy1zcmMgJ3NlbGYnIGRhdGE6IGFzc2V0cy1jZG4uZ2l0aHViLmNvbSBpZGVudGljb25zLmdpdGh1Yi5jb20gd3d3Lmdvb2dsZS1hbmFseXRpY3MuY29tIGNvbGxlY3Rvci5naXRodWJhcHAuY29tICouZ3JhdmF0YXIuY29tICoud3AuY29tIGNoZWNrb3V0LnBheXBhbC5jb20gKi5naXRodWJ1c2VyY29udGVudC5jb207IG1lZGlhLXNyYyAnbm9uZSc7IG9iamVjdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBwbHVnaW4tdHlwZXMgYXBwbGljYXRpb24veC1zaG9ja3dhdmUtZmxhc2g7IHNjcmlwdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBzdHlsZS1zcmMgJ3Vuc2FmZS1pbmxpbmUnIGFzc2V0cy1jZG4uZ2l0aHViLmNvbTsgcmVwb3J0LXVyaSBodHRwczovL2FwaS5naXRodWIuY29tL19wcml2YXRlL2Jyb3dzZXIvZXJyb3Jz")]
        [InlineData("Cookie", "ZGVmYXVsdC1zcmMg\0J25vbmUnOyBiYXNlLXVyaSAnc2VsZic7IGJsb2NrLWFsbC1taXhlZC1jb250ZW50OyBjaGlsZC1zcmMgcmVuZGVyLmdpdGh1YnVzZXJjb250ZW50LmNvbTsgY29ubmVjdC1zcmMgJ3NlbGYnIHVwbG9hZHMuZ2l0aHViLmNvbSBzdGF0dXMuZ2l0aHViLmNvbSBhcGkuZ2l0aHViLmNvbSB3d3cuZ29vZ2xlLWFuYWx5dGljcy5jb20gZ2l0aHViLWNsb3VkLnMzLmFtYXpvbmF3cy5jb20gYXBpLmJyYWludHJlZWdhdGV3YXkuY29tIGNsaWVudC1hbmFseXRpY3MuYnJhaW50cmVlZ2F0ZXdheS5jb20gd3NzOi8vbGl2ZS5naXRodWIuY29tOyBmb250LXNyYyBhc3NldHMtY2RuLmdpdGh1Yi5jb207IGZvcm0tYWN0aW9uICdzZWxmJyBnaXRodWIuY29tIGdpc3QuZ2l0aHViLmNvbTsgZnJhbWUtYW5jZXN0b3JzICdub25lJzsgZnJhbWUtc3JjIHJlbmRlci5naXRodWJ1c2VyY29udGVudC5jb207IGltZy1zcmMgJ3NlbGYnIGRhdGE6IGFzc2V0cy1jZG4uZ2l0aHViLmNvbSBpZGVudGljb25zLmdpdGh1Yi5jb20gd3d3Lmdvb2dsZS1hbmFseXRpY3MuY29tIGNvbGxlY3Rvci5naXRodWJhcHAuY29tICouZ3JhdmF0YXIuY29tICoud3AuY29tIGNoZWNrb3V0LnBheXBhbC5jb20gKi5naXRodWJ1c2VyY29udGVudC5jb207IG1lZGlhLXNyYyAnbm9uZSc7IG9iamVjdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBwbHVnaW4tdHlwZXMgYXBwbGljYXRpb24veC1zaG9ja3dhdmUtZmxhc2g7IHNjcmlwdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBzdHlsZS1zcmMgJ3Vuc2FmZS1pbmxpbmUnIGFzc2V0cy1jZG4uZ2l0aHViLmNvbTsgcmVwb3J0LXVyaSBodHRwczovL2FwaS5naXRodWIuY29tL19wcml2YXRlL2Jyb3dzZXIvZXJyb3Jz")]
        [InlineData("Cookie", "ZGVmYXVsdC1zcmMgJ\025vbmUnOyBiYXNlLXVyaSAnc2VsZic7IGJsb2NrLWFsbC1taXhlZC1jb250ZW50OyBjaGlsZC1zcmMgcmVuZGVyLmdpdGh1YnVzZXJjb250ZW50LmNvbTsgY29ubmVjdC1zcmMgJ3NlbGYnIHVwbG9hZHMuZ2l0aHViLmNvbSBzdGF0dXMuZ2l0aHViLmNvbSBhcGkuZ2l0aHViLmNvbSB3d3cuZ29vZ2xlLWFuYWx5dGljcy5jb20gZ2l0aHViLWNsb3VkLnMzLmFtYXpvbmF3cy5jb20gYXBpLmJyYWludHJlZWdhdGV3YXkuY29tIGNsaWVudC1hbmFseXRpY3MuYnJhaW50cmVlZ2F0ZXdheS5jb20gd3NzOi8vbGl2ZS5naXRodWIuY29tOyBmb250LXNyYyBhc3NldHMtY2RuLmdpdGh1Yi5jb207IGZvcm0tYWN0aW9uICdzZWxmJyBnaXRodWIuY29tIGdpc3QuZ2l0aHViLmNvbTsgZnJhbWUtYW5jZXN0b3JzICdub25lJzsgZnJhbWUtc3JjIHJlbmRlci5naXRodWJ1c2VyY29udGVudC5jb207IGltZy1zcmMgJ3NlbGYnIGRhdGE6IGFzc2V0cy1jZG4uZ2l0aHViLmNvbSBpZGVudGljb25zLmdpdGh1Yi5jb20gd3d3Lmdvb2dsZS1hbmFseXRpY3MuY29tIGNvbGxlY3Rvci5naXRodWJhcHAuY29tICouZ3JhdmF0YXIuY29tICoud3AuY29tIGNoZWNrb3V0LnBheXBhbC5jb20gKi5naXRodWJ1c2VyY29udGVudC5jb207IG1lZGlhLXNyYyAnbm9uZSc7IG9iamVjdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBwbHVnaW4tdHlwZXMgYXBwbGljYXRpb24veC1zaG9ja3dhdmUtZmxhc2g7IHNjcmlwdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBzdHlsZS1zcmMgJ3Vuc2FmZS1pbmxpbmUnIGFzc2V0cy1jZG4uZ2l0aHViLmNvbTsgcmVwb3J0LXVyaSBodHRwczovL2FwaS5naXRodWIuY29tL19wcml2YXRlL2Jyb3dzZXIvZXJyb3Jz")]
        [InlineData("Cookie", "ZGVmYXVsdC1zcmMgJ25vbmUnOyBiYXNlLXVyaSAnc2VsZic7IGJsb2NrLWFsbC1taXhlZC1jb250ZW50OyBjaGlsZC1zcmMgcmVuZGVyLmdpdGh1YnVzZXJjb250ZW50LmNvbTsgY29ubmVjdC1zcmMgJ3NlbGYnIHVwbG9hZHMuZ2l0aHViLmNvbSBzdGF0dXMuZ2l0aHViLmNvbSBhcGkuZ2l0aHViLmNvbSB3d3cuZ29vZ2xlLWFuYWx5dGljcy5jb20gZ2l0aHViLWNsb3VkLnMzLmFtYXpvbmF3cy5jb20gYXBpLmJyYWludHJlZWdhdGV3YXkuY29tIGNsaWVudC1hbmFseXRpY3MuYnJhaW50cmVlZ2F0ZXdheS5jb20gd3NzOi8vbGl2ZS5naXRodWIuY29tOyBmb250LXNyYyBhc3NldHMtY2RuLmdpdGh1Yi5jb207IGZvcm0tYWN0aW9uICdzZWxmJyBnaXRodWIuY29tIGdpc3QuZ2l0aHViLmNvbTsgZnJhbWUtYW5jZXN0b3JzICdub25lJzsgZnJhbWUtc3JjIHJlbmRlci5naXRodWJ1c2VyY29udGVudC5jb207IGltZy1zcmMgJ3NlbGYnIGRhdGE6IGFzc2V0cy1jZG4uZ2l0aHViLmNvbSBpZGVudGljb25zLmdpdGh1Yi5jb20gd3d3Lmdvb2dsZS1hbmFseXRpY3MuY29tIGNvbGxlY3Rvci5naXRodWJhcHAuY29tICouZ3JhdmF0YXIuY29tICoud3AuY29tIGNoZWNrb3V0LnBheXBhbC5jb20gKi5naXRodWJ1c2VyY29udGVudC5jb207IG1lZGlhLXNyYyAnbm9uZSc7IG9iamVjdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBwbHVnaW4tdHlwZXMgYXBwbGljYXRpb24veC1zaG9ja3dhdmUtZmxhc2g7IHNjcmlwdC1zcmMgYXNzZXRzLWNkbi5naXRodWIuY29tOyBzdHlsZS1zcmMgJ3Vuc2FmZS1pbmxpbmUnIGFzc2V0cy1jZG4uZ2l0aHViLmNvbTsgcmVwb3J0LXVyaSBodHRwczovL2FwaS5naXRodWIuY29tL19wcml2YXRlL2Jyb3dzZXIvZXJyb3Jz\0")]
        [InlineData("Content-Security-Polic\0y", "default-src 'none'; base-uri 'self'; block-all-mixed-content; child-src render.githubusercontent.com; connect-src 'self' uploads.github.com status.github.com api.github.com www.google-analytics.com github-cloud.s3.amazonaws.com api.braintreegateway.com client-analytics.braintreegateway.com wss://live.github.com; font-src assets-cdn.github.com; form-action 'self' github.com gist.github.com; frame-ancestors 'none'; frame-src render.githubusercontent.com; img-src 'self' data: assets-cdn.github.com identicons.github.com www.google-analytics.com collector.githubapp.com *.gravatar.com *.wp.com checkout.paypal.com *.githubusercontent.com; media-src 'none'; object-src assets-cdn.github.com; plugin-types application/x-shockwave-flash; script-src assets-cdn.github.com; style-src 'unsafe-inline' assets-cdn.github.com; report-uri https://api.github.com/_private/browser/errors")]
        [InlineData("Content-Securit\0y-Policy", "default-src 'none'; base-uri 'self'; block-all-mixed-content; child-src render.githubusercontent.com; connect-src 'self' uploads.github.com status.github.com api.github.com www.google-analytics.com github-cloud.s3.amazonaws.com api.braintreegateway.com client-analytics.braintreegateway.com wss://live.github.com; font-src assets-cdn.github.com; form-action 'self' github.com gist.github.com; frame-ancestors 'none'; frame-src render.githubusercontent.com; img-src 'self' data: assets-cdn.github.com identicons.github.com www.google-analytics.com collector.githubapp.com *.gravatar.com *.wp.com checkout.paypal.com *.githubusercontent.com; media-src 'none'; object-src assets-cdn.github.com; plugin-types application/x-shockwave-flash; script-src assets-cdn.github.com; style-src 'unsafe-inline' assets-cdn.github.com; report-uri https://api.github.com/_private/browser/errors")]
        [InlineData("Content-Security-Policy", "default-src 'none'; base-uri 'self'; \0block-all-mixed-content; child-src render.githubusercontent.com; connect-src 'self' uploads.github.com status.github.com api.github.com www.google-analytics.com github-cloud.s3.amazonaws.com api.braintreegateway.com client-analytics.braintreegateway.com wss://live.github.com; font-src assets-cdn.github.com; form-action 'self' github.com gist.github.com; frame-ancestors 'none'; frame-src render.githubusercontent.com; img-src 'self' data: assets-cdn.github.com identicons.github.com www.google-analytics.com collector.githubapp.com *.gravatar.com *.wp.com checkout.paypal.com *.githubusercontent.com; media-src 'none'; object-src assets-cdn.github.com; plugin-types application/x-shockwave-flash; script-src assets-cdn.github.com; style-src 'unsafe-inline' assets-cdn.github.com; report-uri https://api.github.com/_private/browser/errors")]
        [InlineData("Content-Security-Policy", "default-src 'none'; base-uri 'self'; šblock-all-mixed-content; child-src render.githubusercontent.com; connect-src 'self' uploads.github.com status.github.com api.github.com www.google-analytics.com github-cloud.s3.amazonaws.com api.braintreegateway.com client-analytics.braintreegateway.com wss://live.github.com; font-src assets-cdn.github.com; form-action 'self' github.com gist.github.com; frame-ancestors 'none'; frame-src render.githubusercontent.com; img-src 'self' data: assets-cdn.github.com identicons.github.com www.google-analytics.com collector.githubapp.com *.gravatar.com *.wp.com checkout.paypal.com *.githubusercontent.com; media-src 'none'; object-src assets-cdn.github.com; plugin-types application/x-shockwave-flash; script-src assets-cdn.github.com; style-src 'unsafe-inline' assets-cdn.github.com; report-uri https://api.github.com/_private/browser/errors")]
        [InlineData("Content-Security-Policy", "default-src 'none'; base-uri 'self'; \u0080block-all-mixed-content; child-src render.githubusercontent.com; connect-src 'self' uploads.github.com status.github.com api.github.com www.google-analytics.com github-cloud.s3.amazonaws.com api.braintreegateway.com client-analytics.braintreegateway.com wss://live.github.com; font-src assets-cdn.github.com; form-action 'self' github.com gist.github.com; frame-ancestors 'none'; frame-src render.githubusercontent.com; img-src 'self' data: assets-cdn.github.com identicons.github.com www.google-analytics.com collector.githubapp.com *.gravatar.com *.wp.com checkout.paypal.com *.githubusercontent.com; media-src 'none'; object-src assets-cdn.github.com; plugin-types application/x-shockwave-flash; script-src assets-cdn.github.com; style-src 'unsafe-inline' assets-cdn.github.com; report-uri https://api.github.com/_private/browser/errors")]
        [InlineData("Content-Security-Policy", "default-src 'none'; base-uri 'self'; block-all-mixed-content; child-src render.githubusercontent.com; connect-src 'self' uploads.github.com status.github.com api.github.com www.google-analytics.com github-cloud.s3.amazonaws.com api.braintreegateway.com client-analytics.braintreegateway.com wss://live.github.com; font-src assets-cdn.github.com; form-action 'self' github.com gist.github.com; frame-ancestors 'none'; frame-src render.githubusercontent.com; img-src 'self' data: assets-cdn.github.com identicons.github.com www.google-analytics.com collector.githubapp.com *.gravatar.com *.wp.com checkout.paypal.com *.githubusercontent.com; media-src 'none'; object-src assets-cdn.github.com; plugin-types application/x-shockwave-flash; script-src assets-cdn.github.com; style-src 'unsafe-inline' assets-cdn.github.com; report-uri https://api.github.com/_private/browser/errorsš")]
        public void AddingControlOrNonAsciiCharactersToHeadersThrows(string key, string value)
        {
            var responseHeaders = new FrameResponseHeaders();

            Assert.Throws<InvalidOperationException>(() =>
            {
                ((IHeaderDictionary)responseHeaders)[key] = value;
            });

            Assert.Throws<InvalidOperationException>(() =>
            {
                ((IHeaderDictionary)responseHeaders)[key] = new StringValues(new[] { "valid", value });
            });

            Assert.Throws<InvalidOperationException>(() =>
            {
                ((IDictionary<string, StringValues>)responseHeaders)[key] = value;
            });

            Assert.Throws<InvalidOperationException>(() =>
            {
                var kvp = new KeyValuePair<string, StringValues>(key, value);
                ((ICollection<KeyValuePair<string, StringValues>>)responseHeaders).Add(kvp);
            });

            Assert.Throws<InvalidOperationException>(() =>
            {
                var kvp = new KeyValuePair<string, StringValues>(key, value);
                ((IDictionary<string, StringValues>)responseHeaders).Add(key, value);
            });
        }

        [Fact]
        public void ThrowsWhenAddingHeaderAfterReadOnlyIsSet()
        {
            var headers = new FrameResponseHeaders();
            headers.SetReadOnly();

            Assert.Throws<InvalidOperationException>(() => ((IDictionary<string, StringValues>)headers).Add("my-header", new[] { "value" }));
        }

        [Fact]
        public void ThrowsWhenChangingHeaderAfterReadOnlyIsSet()
        {
            var headers = new FrameResponseHeaders();
            var dictionary = (IDictionary<string, StringValues>)headers;
            dictionary.Add("my-header", new[] { "value" });
            headers.SetReadOnly();

            Assert.Throws<InvalidOperationException>(() => dictionary["my-header"] = "other-value");
        }

        [Fact]
        public void ThrowsWhenRemovingHeaderAfterReadOnlyIsSet()
        {
            var headers = new FrameResponseHeaders();
            var dictionary = (IDictionary<string, StringValues>)headers;
            dictionary.Add("my-header", new[] { "value" });
            headers.SetReadOnly();

            Assert.Throws<InvalidOperationException>(() => dictionary.Remove("my-header"));
        }

        [Fact]
        public void ThrowsWhenClearingHeadersAfterReadOnlyIsSet()
        {
            var headers = new FrameResponseHeaders();
            var dictionary = (IDictionary<string, StringValues>)headers;
            dictionary.Add("my-header", new[] { "value" });
            headers.SetReadOnly();

            Assert.Throws<InvalidOperationException>(() => dictionary.Clear());
        }

        [Theory]
        [MemberData(nameof(BadContentLengths))]
        public void ThrowsWhenAddingContentLengthWithNonNumericValue(string contentLength)
        {
            var headers = new FrameResponseHeaders();
            var dictionary = (IDictionary<string, StringValues>)headers;

            var exception = Assert.Throws<InvalidOperationException>(() => dictionary.Add("Content-Length", new[] { contentLength }));
            Assert.Equal($"Invalid Content-Length: \"{contentLength}\". Value must be a positive integral number.", exception.Message);
        }

        [Theory]
        [MemberData(nameof(BadContentLengths))]
        public void ThrowsWhenSettingContentLengthToNonNumericValue(string contentLength)
        {
            var headers = new FrameResponseHeaders();
            var dictionary = (IDictionary<string, StringValues>)headers;

            var exception = Assert.Throws<InvalidOperationException>(() => ((IHeaderDictionary)headers)["Content-Length"] = contentLength);
            Assert.Equal($"Invalid Content-Length: \"{contentLength}\". Value must be a positive integral number.", exception.Message);
        }

        [Theory]
        [MemberData(nameof(BadContentLengths))]
        public void ThrowsWhenAssigningHeaderContentLengthToNonNumericValue(string contentLength)
        {
            var headers = new FrameResponseHeaders();

            var exception = Assert.Throws<InvalidOperationException>(() => headers.HeaderContentLength = contentLength);
            Assert.Equal($"Invalid Content-Length: \"{contentLength}\". Value must be a positive integral number.", exception.Message);
        }

        [Theory]
        [MemberData(nameof(GoodContentLengths))]
        public void ContentLengthValueCanBeReadAsLongAfterAddingHeader(string contentLength)
        {
            var headers = new FrameResponseHeaders();
            var dictionary = (IDictionary<string, StringValues>)headers;
            dictionary.Add("Content-Length", contentLength);

            Assert.Equal(ParseLong(contentLength), headers.ContentLength);
        }

        [Theory]
        [MemberData(nameof(GoodContentLengths))]
        public void ContentLengthValueCanBeReadAsLongAfterSettingHeader(string contentLength)
        {
            var headers = new FrameResponseHeaders();
            var dictionary = (IDictionary<string, StringValues>)headers;
            dictionary["Content-Length"] = contentLength;

            Assert.Equal(ParseLong(contentLength), headers.ContentLength);
        }

        [Theory]
        [MemberData(nameof(GoodContentLengths))]
        public void ContentLengthValueCanBeReadAsLongAfterAssigningHeader(string contentLength)
        {
            var headers = new FrameResponseHeaders();
            headers.HeaderContentLength = contentLength;

            Assert.Equal(ParseLong(contentLength), headers.ContentLength);
        }

        [Fact]
        public void ContentLengthValueClearedWhenHeaderIsRemoved()
        {
            var headers = new FrameResponseHeaders();
            headers.HeaderContentLength = "42";
            var dictionary = (IDictionary<string, StringValues>)headers;

            dictionary.Remove("Content-Length");

            Assert.Equal(null, headers.ContentLength);
        }

        [Fact]
        public void ContentLengthValueClearedWhenHeadersCleared()
        {
            var headers = new FrameResponseHeaders();
            headers.HeaderContentLength = "42";
            var dictionary = (IDictionary<string, StringValues>)headers;

            dictionary.Clear();

            Assert.Equal(null, headers.ContentLength);
        }

        private static long ParseLong(string value)
        {
            return long.Parse(value, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture);
        }

        public static TheoryData<string> GoodContentLengths => new TheoryData<string>
        {
            "0",
            "00",
            "042",
            "42",
            long.MaxValue.ToString(CultureInfo.InvariantCulture)
        };

        public static TheoryData<string> BadContentLengths => new TheoryData<string>
        {
            "",
            " ",
            " 42",
            "42 ",
            "bad",
            "!",
            "!42",
            "42!",
            "42,000",
            "42.000",
        };
    }
}