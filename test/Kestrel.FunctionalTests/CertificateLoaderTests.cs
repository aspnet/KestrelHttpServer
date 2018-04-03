// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https.Internal;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using Xunit.Abstractions;

namespace FunctionalTests
{
    public class CertificateLoaderTests : LoggedTest
    {
        public CertificateLoaderTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData("no_extensions.pfx")]
        public void IsCertificateAllowedForServerAuth_AllowWithNoExtensions(string testCertName)
        {
            using (StartLog(out var loggerFactory, TestConstants.DefaultFunctionalTestLogLevel))
            {
                var logger = loggerFactory.CreateLogger(nameof(CertificateLoaderTests));

                var certPath = TestResources.GetCertPath(testCertName);
                logger.Log(LogLevel.Information, "Loading " + certPath);
                var cert = new X509Certificate2(certPath, "testPassword");
                Assert.Empty(cert.Extensions.OfType<X509EnhancedKeyUsageExtension>());

                Assert.True(CertificateLoader.IsCertificateAllowedForServerAuth(cert));
            }
        }

        [Theory]
        [InlineData("eku.server.pfx")]
        [InlineData("eku.multiple_usages.pfx")]
        public void IsCertificateAllowedForServerAuth_ValidatesEnhancedKeyUsageOnCertificate(string testCertName)
        {
            using (StartLog(out var loggerFactory, TestConstants.DefaultFunctionalTestLogLevel, $"{nameof(IsCertificateAllowedForServerAuth_ValidatesEnhancedKeyUsageOnCertificate)}_{testCertName}"))
            {
                var logger = loggerFactory.CreateLogger(nameof(CertificateLoaderTests));

                var certPath = TestResources.GetCertPath(testCertName);
                logger.Log(LogLevel.Information, "Loading " + certPath);
                var cert = new X509Certificate2(certPath, "testPassword");
                Assert.NotEmpty(cert.Extensions);
                var eku = Assert.Single(cert.Extensions.OfType<X509EnhancedKeyUsageExtension>());
                Assert.NotEmpty(eku.EnhancedKeyUsages);

                Assert.True(CertificateLoader.IsCertificateAllowedForServerAuth(cert));
            }
        }

        [Theory]
        [InlineData("eku.code_signing.pfx")]
        [InlineData("eku.client.pfx")]
        public void IsCertificateAllowedForServerAuth_RejectsCertificatesMissingServerEku(string testCertName)
        {
            using (StartLog(out var loggerFactory, TestConstants.DefaultFunctionalTestLogLevel, $"{nameof(IsCertificateAllowedForServerAuth_RejectsCertificatesMissingServerEku)}_{testCertName}"))
            {
                var logger = loggerFactory.CreateLogger(nameof(CertificateLoaderTests));
                var certPath = TestResources.GetCertPath(testCertName);
                logger.Log(LogLevel.Information, "Loading " + certPath);
                var cert = new X509Certificate2(certPath, "testPassword");
                Assert.NotEmpty(cert.Extensions);
                var eku = Assert.Single(cert.Extensions.OfType<X509EnhancedKeyUsageExtension>());
                Assert.NotEmpty(eku.EnhancedKeyUsages);

                Assert.False(CertificateLoader.IsCertificateAllowedForServerAuth(cert));
            }
        }
    }
}
