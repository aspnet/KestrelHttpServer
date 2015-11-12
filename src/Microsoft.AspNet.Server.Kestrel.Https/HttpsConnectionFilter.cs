// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Filter;
using System.Security.Authentication;
using Microsoft.AspNet.Http.Features.Internal;
using System.Diagnostics;

namespace Microsoft.AspNet.Server.Kestrel.Https
{
    public class HttpsConnectionFilter : IConnectionFilter
    {
        private readonly X509Certificate2 _cert;
        private readonly ClientCertificateMode _clientCertMode;
        private readonly IConnectionFilter _previous;

        public HttpsConnectionFilter(HttpsConnectionFilterOptions options, IConnectionFilter previous)
        {
            if (options.ServerCertificate == null)
            {
                throw new ArgumentNullException(nameof(options.ServerCertificate));
            }
            if (previous == null)
            {
                throw new ArgumentNullException(nameof(previous));
            }

            _cert = options.ServerCertificate;
            _clientCertMode = options.ClientCertificateMode;
            _previous = previous;
        }

        public async Task OnConnection(ConnectionFilterContext context)
        {
            await _previous.OnConnection(context);

            if (string.Equals(context.Address.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                SslStream sslStream;
                if (_clientCertMode == ClientCertificateMode.NoCertificate)
                {
                    sslStream = new SslStream(context.Connection);
                    await sslStream.AuthenticateAsServerAsync(_cert);
                }
                else
                {
                    sslStream = new SslStream(context.Connection, leaveInnerStreamOpen: false,
                        userCertificateValidationCallback: (sender, certificate, chain, sslPolicyErrors) =>
                        {
                            if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
                            {
                                return _clientCertMode != ClientCertificateMode.RequireCertificate;
                            }

                            if (sslPolicyErrors != SslPolicyErrors.None)
                            {
                                return false;
                            }

                            context.TlsConnectionFeature = new TlsConnectionFeature()
                            {
                                ClientCertificate = certificate as X509Certificate2 ?? new X509Certificate2(certificate)
                            };
                            return true;
                        });
                    await sslStream.AuthenticateAsServerAsync(_cert, clientCertificateRequired: true,
                        enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls,
                        checkCertificateRevocation: false);
                }
                context.Connection = sslStream;
            }
        }
    }
}
