// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Protocols.Abstractions;
using Microsoft.AspNetCore.Protocols.Abstractions.Features;
using Microsoft.AspNetCore.Protocols.Tls.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Protocols.Tls
{
    public class TlsConnectionMiddleware
    {
        // See http://oid-info.com/get/1.3.6.1.5.5.7.3.1
        // Indicates that a certificate can be used as a SSL server certificate
        private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

        private readonly ConnectionDelegate _next;
        private readonly TlsConnectionOptions _options;
        private readonly X509Certificate2 _serverCertificate;
        private readonly ILogger _logger;

        public TlsConnectionMiddleware(ConnectionDelegate next, TlsConnectionOptions options)
            : this(next, options, loggerFactory: null)
        {
        }

        public TlsConnectionMiddleware(ConnectionDelegate next, TlsConnectionOptions options, ILoggerFactory loggerFactory)
        {
            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.ServerCertificate == null)
            {
                throw new ArgumentException(TlsStrings.ServiceCertificateRequired, nameof(options));
            }

            _next = next;
            // capture the certificate now so it can be switched after validation
            _serverCertificate = options.ServerCertificate;

            EnsureCertificateIsAllowedForServerAuth(_serverCertificate);

            _options = options;
            _logger = loggerFactory?.CreateLogger(nameof(TlsConnectionMiddleware));
        }

        public async Task OnConnectionAsync(ConnectionContext context)
        {
            SslStream sslStream;
            bool certificateRequired;

            var pipeStream = new PipeStream(context.Transport.Reader, context.Transport.Writer);

            if (_options.ClientCertificateMode == ClientCertificateMode.NoCertificate)
            {
                sslStream = new SslStream(pipeStream);
                certificateRequired = false;
            }
            else
            {
                sslStream = new SslStream(pipeStream,
                    leaveInnerStreamOpen: false,
                    userCertificateValidationCallback: (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        if (certificate == null)
                        {
                            return _options.ClientCertificateMode != ClientCertificateMode.RequireCertificate;
                        }

                        if (_options.ClientCertificateValidation == null)
                        {
                            if (sslPolicyErrors != SslPolicyErrors.None)
                            {
                                return false;
                            }
                        }

                        var certificate2 = ConvertToX509Certificate2(certificate);
                        if (certificate2 == null)
                        {
                            return false;
                        }

                        if (_options.ClientCertificateValidation != null)
                        {
                            if (!_options.ClientCertificateValidation(certificate2, chain, sslPolicyErrors))
                            {
                                return false;
                            }
                        }

                        return true;
                    });

                certificateRequired = true;
            }

            try
            {
                // Don't trust SslStream not to block.
                await Task.Yield();

                await sslStream.AuthenticateAsServerAsync(_serverCertificate, certificateRequired,
                        _options.SslProtocols, _options.CheckCertificateRevocation);
            }
            catch (IOException ex)
            {
                _logger?.LogInformation(1, ex, TlsStrings.AuthenticationFailed);
                sslStream.Dispose();

                // REVIEW: Do we need to call next here?
                return;
            }

            var transportFeature = context.Features.Get<IConnectionTransportFeature>();

            var streamPipe = new StreamPipe(transportFeature.PipeFactory);
            context.Transport = streamPipe;

            // Always set the feature even though the cert might be null
            context.Features.Set<ITlsConnectionFeature>(new TlsConnectionFeature
            {
                ClientCertificate = ConvertToX509Certificate2(sslStream.RemoteCertificate)
            });
            
            // Start pumping bytes through the SSL stream
            var task = streamPipe.CopyFromAsync(sslStream);

            // Call the next middleware with TLS information
            await _next(context);

            // Make sure we unwind
            await task;
        }

        private static void EnsureCertificateIsAllowedForServerAuth(X509Certificate2 certificate)
        {
            /* If the Extended Key Usage extension is included, then we check that the serverAuth usage is included. (http://oid-info.com/get/1.3.6.1.5.5.7.3.1)
             * If the Extended Key Usage extension is not included, then we assume the certificate is allowed for all usages.
             * 
             * See also https://blogs.msdn.microsoft.com/kaushal/2012/02/17/client-certificates-vs-server-certificates/
             * 
             * From https://tools.ietf.org/html/rfc3280#section-4.2.1.13 "Certificate Extensions: Extended Key Usage"
             * 
             * If the (Extended Key Usage) extension is present, then the certificate MUST only be used
             * for one of the purposes indicated.  If multiple purposes are
             * indicated the application need not recognize all purposes indicated,
             * as long as the intended purpose is present.  Certificate using
             * applications MAY require that a particular purpose be indicated in
             * order for the certificate to be acceptable to that application.
             */

            var hasEkuExtension = false;

            foreach (var extension in certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>())
            {
                hasEkuExtension = true;
                foreach (var oid in extension.EnhancedKeyUsages)
                {
                    if (oid.Value.Equals(ServerAuthenticationOid, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
            }

            if (hasEkuExtension)
            {
                throw new InvalidOperationException(TlsStrings.FormatInvalidServerCertificateEku(certificate.Thumbprint));
            }
        }

        private static X509Certificate2 ConvertToX509Certificate2(X509Certificate certificate)
        {
            if (certificate == null)
            {
                return null;
            }

            if (certificate is X509Certificate2 cert2)
            {
                return cert2;
            }

            return new X509Certificate2(certificate);
        }
    }
}
