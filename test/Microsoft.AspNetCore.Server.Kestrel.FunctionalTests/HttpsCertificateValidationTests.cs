// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class HttpsCertificateValidationTests
    {
        private readonly ITestOutputHelper _output;

        public HttpsCertificateValidationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData("no_extensions.pfx")]
        public void AcceptsCertificateWithoutExtensions(string testCertName)
        {
            var cert = GetCert(testCertName);
            Assert.Empty(cert.Extensions.OfType<X509EnhancedKeyUsageExtension>());

            new HttpsConnectionAdapter(new HttpsConnectionAdapterOptions
            {
                ServerCertificate = cert,
            });
        }

        [Theory]
        [InlineData("eku.server.pfx")]
        [InlineData("eku.multiple_usages.pfx")]
        public void AllowsValidEnhancedKeyUsages(string testCertName)
        {
            var cert = GetCert(testCertName);

            Assert.NotEmpty(cert.Extensions);
            var eku = Assert.Single(cert.Extensions.OfType<X509EnhancedKeyUsageExtension>());
            Assert.NotEmpty(eku.EnhancedKeyUsages);

            new HttpsConnectionAdapter(new HttpsConnectionAdapterOptions
            {
                ServerCertificate = cert,
            });
        }

        [Theory]
        [InlineData("ku.valid.pfx")]
        [InlineData("ku.valid_with_addtl.pfx")]
        public void AllowsValidKeyUsages(string testCertName)
        {
            var cert = GetCert(testCertName);

            Assert.NotEmpty(cert.Extensions);
            var eku = Assert.Single(cert.Extensions.OfType<X509KeyUsageExtension>());

            new HttpsConnectionAdapter(new HttpsConnectionAdapterOptions
            {
                ServerCertificate = cert,
            });
        }

        [Theory]
        [InlineData("eku.code_signing.pfx")]
        [InlineData("eku.client.pfx")]
        public void ThrowsForCertificatesMissingServerEku(string testCertName)
        {
            var cert = GetCert(testCertName);

            Assert.NotEmpty(cert.Extensions);
            var eku = Assert.Single(cert.Extensions.OfType<X509EnhancedKeyUsageExtension>());
            Assert.NotEmpty(eku.EnhancedKeyUsages);

            var ex = Assert.Throws<CryptographicException>(() =>
                new HttpsConnectionAdapter(new HttpsConnectionAdapterOptions
                {
                    ServerCertificate = cert,
                }));

            Assert.Equal(HttpsStrings.FormatInvalidServerCertificateEku(cert.Thumbprint), ex.Message);
        }

        [Theory]
        [InlineData("ku.missing_digital_sig.pfx")]
        [InlineData("ku.missing_key_enc.pfx")]
        [InlineData("ku.missing_both.pfx")]
        public void ThrowsWhenMissingRequiredKeyUsage(string testCertName)
        {
            var cert = GetCert(testCertName);

            Assert.NotEmpty(cert.Extensions);
            Assert.Single(cert.Extensions.OfType<X509KeyUsageExtension>());

            var ex = Assert.Throws<CryptographicException>(() =>
                new HttpsConnectionAdapter(new HttpsConnectionAdapterOptions
                {
                    ServerCertificate = cert,
                }));

            Assert.Equal(HttpsStrings.FormatInvalidServerCertificateKeyUsages(cert.Thumbprint), ex.Message);
        }

        private X509Certificate2 GetCert(string testCertName)
        {
            var certPath = TestResources.GetCertPath(testCertName);
            _output.WriteLine("Loading " + certPath);
            var cert = new X509Certificate2(certPath, "testPassword");
            return cert;
        }
    }
}
