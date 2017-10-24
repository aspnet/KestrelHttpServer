// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Certificates.Generation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal
{
    public class DefaultHttpsProvider : IDefaultHttpsProvider
    {
        private static readonly CertificateManager _certificateManager = new CertificateManager();

        private readonly ILogger<DefaultHttpsProvider> _logger;

        public Func<X509Certificate2> DefaultCertificateResolver { get; set; }

        public DefaultHttpsProvider(ILogger<DefaultHttpsProvider> logger)
        {
            _logger = logger;
            DefaultCertificateResolver = FindDevelopmentCertificate;
        }

        public void ConfigureHttps(ListenOptions listenOptions)
        {
            var cert = DefaultCertificateResolver();
            if (cert == null)
            {
                throw new InvalidOperationException(KestrelStrings.HttpsUrlProvidedButNoDevelopmentCertificateFound);
            }
            listenOptions.UseHttps(cert);
        }

        private X509Certificate2 FindDevelopmentCertificate()
        {
            var certificate = _certificateManager.ListCertificates(CertificatePurpose.HTTPS, StoreName.My, StoreLocation.CurrentUser, isValid: true)
                .FirstOrDefault();
            if (certificate != null)
            {
                _logger.LogDebug("Using development certificate: {certificateSubjectName} (Thumbprint: {certificateThumbprint})", certificate.Subject, certificate.Thumbprint);
                return certificate;
            }
            else
            {
                _logger.LogDebug("Development certificate could not be found");
                return null;
            }
        }

        private void DisposeCertificates(IEnumerable<X509Certificate2> certificates)
        {
            foreach (var certificate in certificates)
            {
                try
                {
                    certificate.Dispose();
                }
                catch (Exception ex)
                {
                    // Accessing certificate may cause additional exceptions.
                    _logger.LogError(ex, "Error disposing of certficate.");
                }
            }
        }
    }
}
