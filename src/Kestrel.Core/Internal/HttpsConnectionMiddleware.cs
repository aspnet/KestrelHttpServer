// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Https.Internal
{
    public class HttpsConnectionMiddleware
    {
        private readonly ConnectionDelegate _next;
        private readonly HttpsConnectionAdapterOptions _options;
        private readonly X509Certificate2 _serverCertificate;
        private readonly Func<ConnectionContext, string, X509Certificate2> _serverCertificateSelector;

        private readonly ILogger _logger;

        public HttpsConnectionMiddleware(ConnectionDelegate next, HttpsConnectionAdapterOptions options)
            : this(next, options, loggerFactory: null)
        {
        }

        public HttpsConnectionMiddleware(ConnectionDelegate next, HttpsConnectionAdapterOptions options, ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _next = next;
            // capture the certificate now so it can't be switched after validation
            _serverCertificate = options.ServerCertificate;
            _serverCertificateSelector = options.ServerCertificateSelector;
            if (_serverCertificate == null && _serverCertificateSelector == null)
            {
                throw new ArgumentException(CoreStrings.ServerCertificateRequired, nameof(options));
            }

            // If a selector is provided then ignore the cert, it may be a default cert.
            if (_serverCertificateSelector != null)
            {
                // SslStream doesn't allow both.
                _serverCertificate = null;
            }
            else
            {
                EnsureCertificateIsAllowedForServerAuth(_serverCertificate);
            }

            _options = options;
            _logger = loggerFactory?.CreateLogger(nameof(HttpsConnectionMiddleware));
        }

        public async Task OnConnectionAsync(ConnectionContext connectionContext)
        {
            SslStream sslStream;
            bool certificateRequired;
            var feature = new TlsConnectionFeature();
            connectionContext.Features.Set<ITlsConnectionFeature>(feature);
            connectionContext.Features.Set<ITlsHandshakeFeature>(feature);

            var transportStream = new RawStream(connectionContext.Transport.Input, connectionContext.Transport.Output);

            if (_options.ClientCertificateMode == ClientCertificateMode.NoCertificate)
            {
                sslStream = new SslStream(transportStream);
                certificateRequired = false;
            }
            else
            {
                sslStream = new SslStream(transportStream,
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

            using (var cancellationTokeSource = new CancellationTokenSource(_options.HandshakeTimeout))
            using (cancellationTokeSource.Token.Register(state => ((ConnectionContext)state).Abort(), connectionContext))
            {
                _options.OnHandshakeStarted?.Invoke();

                try
                {
#if NETCOREAPP2_1
                // Adapt to the SslStream signature
                ServerCertificateSelectionCallback selector = null;
                if (_serverCertificateSelector != null)
                {
                    selector = (sender, name) =>
                    {
                        connectionContext.Features.Set(sslStream);
                        var cert = _serverCertificateSelector(connectionContext, name);
                        if (cert != null)
                        {
                            EnsureCertificateIsAllowedForServerAuth(cert);
                        }
                        return cert;
                    };
                }

                var sslOptions = new SslServerAuthenticationOptions()
                {
                    ServerCertificate = _serverCertificate,
                    ServerCertificateSelectionCallback = selector,
                    ClientCertificateRequired = certificateRequired,
                    EnabledSslProtocols = _options.SslProtocols,
                    CertificateRevocationCheckMode = _options.CheckCertificateRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck,
                    ApplicationProtocols = new List<SslApplicationProtocol>()
                };

                // This is order sensitive
                if ((_options.HttpProtocols & HttpProtocols.Http2) != 0)
                {
                    sslOptions.ApplicationProtocols.Add(SslApplicationProtocol.Http2);
                    // https://tools.ietf.org/html/rfc7540#section-9.2.1
                    sslOptions.AllowRenegotiation = false;
                }

                if ((_options.HttpProtocols & HttpProtocols.Http1) != 0)
                {
                    sslOptions.ApplicationProtocols.Add(SslApplicationProtocol.Http11);
                }

                await sslStream.AuthenticateAsServerAsync(sslOptions, CancellationToken.None);
#elif NETSTANDARD2_0 // No ALPN support
                    var serverCert = _serverCertificate;
                    if (_serverCertificateSelector != null)
                    {
                        connectionContext.Features.Set(sslStream);
                        serverCert = _serverCertificateSelector(connectionContext, null);
                        if (serverCert != null)
                        {
                            EnsureCertificateIsAllowedForServerAuth(serverCert);
                        }
                    }
                    await sslStream.AuthenticateAsServerAsync(serverCert, certificateRequired,
                            _options.SslProtocols, _options.CheckCertificateRevocation);
#else
#error TFMs need to be updated
#endif
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogDebug(2, CoreStrings.AuthenticationTimedOut);
                    sslStream.Dispose();
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is AuthenticationException)
                {
                    _logger?.LogDebug(1, ex, CoreStrings.AuthenticationFailed);
                    sslStream.Dispose();
                    return;
                }
            }

#if NETCOREAPP2_1
            feature.ApplicationProtocol = sslStream.NegotiatedApplicationProtocol.Protocol;
            connectionContext.Features.Set<ITlsApplicationProtocolFeature>(feature);
#elif NETSTANDARD2_0 // No ALPN support
#else
#error TFMs need to be updated
#endif
            feature.ClientCertificate = ConvertToX509Certificate2(sslStream.RemoteCertificate);
            feature.CipherAlgorithm = sslStream.CipherAlgorithm;
            feature.CipherStrength = sslStream.CipherStrength;
            feature.HashAlgorithm = sslStream.HashAlgorithm;
            feature.HashStrength = sslStream.HashStrength;
            feature.KeyExchangeAlgorithm = sslStream.KeyExchangeAlgorithm;
            feature.KeyExchangeStrength = sslStream.KeyExchangeStrength;
            feature.Protocol = sslStream.SslProtocol;

            var memoryPoolFeature = connectionContext.Features.Get<IMemoryPoolFeature>();

            var inputPipeOptions = new PipeOptions
            (
                pool: memoryPoolFeature.MemoryPool,
                readerScheduler: _options.Scheduler,
                writerScheduler: PipeScheduler.Inline,
                pauseWriterThreshold: _options.MaxInputBufferSize ?? 0,
                resumeWriterThreshold: _options.MaxInputBufferSize ?? 0,
                useSynchronizationContext: false,
                minimumSegmentSize: KestrelMemoryPool.MinimumSegmentSize
            );

            var outputPipeOptions = new PipeOptions
            (
                pool: memoryPoolFeature.MemoryPool,
                readerScheduler: PipeScheduler.Inline,
                writerScheduler: PipeScheduler.Inline,
                pauseWriterThreshold: _options.MaxOutputBufferSize ?? 0,
                resumeWriterThreshold: _options.MaxOutputBufferSize ?? 0,
                useSynchronizationContext: false,
                minimumSegmentSize: KestrelMemoryPool.MinimumSegmentSize
            );

            var original = connectionContext.Transport;

            try
            {
                var adaptedPipeline = new AdaptedPipeline(new Pipe(inputPipeOptions), new Pipe(outputPipeOptions), _logger, original);
                connectionContext.Transport = adaptedPipeline;

                using (adaptedPipeline)
                using (sslStream)
                {
                    var task = adaptedPipeline.RunAsync(sslStream);

                    await _next(connectionContext);

                    await task;
                }
            }
            finally
            {
                // Restore the original so that it gets closed appropriately
                connectionContext.Transport = original;
            }
        }

        private static void EnsureCertificateIsAllowedForServerAuth(X509Certificate2 certificate)
        {
            if (!CertificateLoader.IsCertificateAllowedForServerAuth(certificate))
            {
                throw new InvalidOperationException(CoreStrings.FormatInvalidServerCertificateEku(certificate.Thumbprint));
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
