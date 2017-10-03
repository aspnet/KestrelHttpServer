// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel
{
    internal class DefaultHttpsProvider : IDefaultHttpsProvider
    {
        private const string AspNetHttpsOid = "1.3.6.1.4.1.311.84.1.1";

        public void ConfigureHttps(ListenOptions listenOptions)
        {
            listenOptions.UseHttps(FindDevelopmentCertificate());
        }

        private static X509Certificate2 FindDevelopmentCertificate()
        {
            // TODO: replace this with call to CertificateManager.FindCertificates(CertificatePurpose.HTTPS, StoreName.My, StoreLocation.CurrentUser, isValid: true)
            // when that becomes available.
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);

                var certificates = store.Certificates.OfType<X509Certificate2>();
                var certificate = certificates
                    .FirstOrDefault(c => HasOid(c, AspNetHttpsOid) && !IsExpired(c) /*&& HasPrivateKey(c)*/);

                if (certificate == null)
                {
                    throw new InvalidOperationException("Unable to find ASP.NET Core development certificate.");
                }

                DisposeCertificates(certificates.Except(new[] { certificate }));

                return certificate;
            }
        }

        private static bool HasOid(X509Certificate2 certificate, string oid) =>
            certificate.Extensions
                .OfType<X509Extension>()
                .Any(e => string.Equals(oid, e.Oid.Value, StringComparison.Ordinal));

        private static bool IsExpired(X509Certificate2 certificate)
        {
            var now = DateTimeOffset.Now;
            return now < certificate.NotBefore || now > certificate.NotAfter;
        }

        private static bool HasPrivateKey(X509Certificate2 certificate)
            => (certificate.GetRSAPrivateKey() is RSACryptoServiceProvider rsaPrivateKey && rsaPrivateKey.CspKeyContainerInfo.Exportable)/* ||
               (certificate.GetRSAPrivateKey() is RSACng cngPrivateKey && cngPrivateKey.CspKeyContainerInfo.Exportable)*/;

        private static void DisposeCertificates(IEnumerable<X509Certificate2> certificates)
        {
            foreach (var certificate in certificates)
            {
                try
                {
                    certificate.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}
