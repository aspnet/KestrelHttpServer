// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Https.Internal
{
    public class HttpsConnectionAdapter : IConnectionAdapter
    {
        private static readonly ClosedAdaptedConnection _closedAdaptedConnection = new ClosedAdaptedConnection();

        private readonly HttpsConnectionAdapterOptions _options;
        private readonly X509Certificate2 _serverCertificate;
        private readonly Func<ConnectionContext, string, X509Certificate2> _serverCertificateSelector;

        private readonly ILogger _logger;

        public HttpsConnectionAdapter(HttpsConnectionAdapterOptions options)
            : this(options, loggerFactory: null)
        {
        }

        public HttpsConnectionAdapter(HttpsConnectionAdapterOptions options, ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

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
            _logger = loggerFactory?.CreateLogger(nameof(HttpsConnectionAdapter));
        }

        public bool IsHttps => true;

        public Task<IAdaptedConnection> OnConnectionAsync(ConnectionAdapterContext context)
        {
            // Don't trust SslStream not to block.
            return Task.Run(() => InnerOnConnectionAsync(context));
        }

        private async Task<IAdaptedConnection> InnerOnConnectionAsync(ConnectionAdapterContext context)
        {
            var timeoutFeature = context.Features.Get<IConnectionTimeoutFeature>();
            timeoutFeature.SetTimeout(_options.HandshakeTimeout);

            var prefix = new byte[1]; // TODO: Consider buffering the size of an expected TLS frame.
            var read = 0;
            try
            {
                // Wait for data to become available.
                read = await context.ConnectionStream.ReadAsync(prefix, 0, prefix.Length);
            }
            catch (Exception)
            {
            }

            if (read == 0)
            {
                _logger?.LogDebug(3, "New connection closed without sending data.");
                timeoutFeature.CancelTimeout();
                context.ConnectionStream.Dispose();
                return _closedAdaptedConnection;
            }

            var innerStream = new PrefixedStream(context.ConnectionStream, new ArraySegment<byte>(prefix, 0, read));

            SslStream sslStream;
            bool certificateRequired;
            var feature = new TlsConnectionFeature();
            context.Features.Set<ITlsConnectionFeature>(feature);

            if (_options.ClientCertificateMode == ClientCertificateMode.NoCertificate)
            {
                sslStream = new SslStream(innerStream);
                certificateRequired = false;
            }
            else
            {
                sslStream = new SslStream(innerStream,
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
#if NETCOREAPP2_1
                // Adapt to the SslStream signature
                ServerCertificateSelectionCallback selector = null;
                if (_serverCertificateSelector != null)
                {
                    selector = (sender, name) =>
                    {
                        context.Features.Set(sslStream);
                        var cert = _serverCertificateSelector(context.ConnectionContext, name);
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
                }

                if ((_options.HttpProtocols & HttpProtocols.Http1) != 0)
                {
                    sslOptions.ApplicationProtocols.Add(SslApplicationProtocol.Http11);
                }

                await sslStream.AuthenticateAsServerAsync(sslOptions, CancellationToken.None);
#else
                var serverCert = _serverCertificate;
                if (_serverCertificateSelector != null)
                {
                    context.Features.Set(sslStream);
                    serverCert = _serverCertificateSelector(context.ConnectionContext, null);
                    if (serverCert != null)
                    {
                        EnsureCertificateIsAllowedForServerAuth(serverCert);
                    }
                }
                await sslStream.AuthenticateAsServerAsync(serverCert, certificateRequired,
                        _options.SslProtocols, _options.CheckCertificateRevocation);
#endif
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation(2, CoreStrings.AuthenticationTimedOut);
                sslStream.Dispose();
                return _closedAdaptedConnection;
            }
            catch (IOException ex)
            {
                _logger?.LogInformation(1, ex, CoreStrings.AuthenticationFailed);
                sslStream.Dispose();
                return _closedAdaptedConnection;
            }
            finally
            {
                timeoutFeature.CancelTimeout();
            }

#if NETCOREAPP2_1
            feature.ApplicationProtocol = sslStream.NegotiatedApplicationProtocol.Protocol;
            context.Features.Set<ITlsApplicationProtocolFeature>(feature);
#endif
            feature.ClientCertificate = ConvertToX509Certificate2(sslStream.RemoteCertificate);

            return new HttpsAdaptedConnection(sslStream);
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

        private class HttpsAdaptedConnection : IAdaptedConnection
        {
            private readonly SslStream _sslStream;

            public HttpsAdaptedConnection(SslStream sslStream)
            {
                _sslStream = sslStream;
            }

            public Stream ConnectionStream => _sslStream;

            public void Dispose()
            {
                _sslStream.Dispose();
            }
        }

        private class ClosedAdaptedConnection : IAdaptedConnection
        {
            public Stream ConnectionStream { get; } = new ClosedStream();

            public void Dispose()
            {
            }
        }

        private class PrefixedStream : Stream
        {
            private readonly Stream _innerStream;
            private Memory<byte> _prefix;

            public PrefixedStream(Stream innerStream, Memory<byte> prefix)
            {
                _innerStream = innerStream;
                _prefix = prefix;
            }

            public override bool CanRead => _innerStream.CanRead;
            public override bool CanSeek => _innerStream.CanSeek;
            public override bool CanWrite => _innerStream.CanWrite;
            public override long Length => _innerStream.Length;
            public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }

            public override void SetLength(long value) => _innerStream.SetLength(value);
            public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);

            public override void Flush() => _innerStream.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => _innerStream.FlushAsync(cancellationToken);

            public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                _innerStream.WriteAsync(buffer, offset, count, cancellationToken);

            public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
                => _innerStream.BeginWrite(buffer, offset, count, callback, state);

            public override void EndWrite(IAsyncResult asyncResult) => _innerStream.EndWrite(asyncResult);

            protected override void Dispose(bool disposing) => _innerStream.Dispose();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (!_prefix.IsEmpty)
                {
                    // Validates args
                    var destination = new Span<byte>(buffer, offset, count);
                    return ReadPrefix(destination);
                }

                return _innerStream.Read(buffer, offset, count);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (!_prefix.IsEmpty)
                {
                    // Validates args
                    var destination = new Span<byte>(buffer, offset, count);
                    return Task.FromResult(ReadPrefix(destination));
                }

                return _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
            }

            private int ReadPrefix(Span<byte> destination)
            {
                if (destination.Length == 0)
                {
                    // Data is available
                    return 0;
                }

                var read = Math.Min(destination.Length, _prefix.Length);
                var source = _prefix.Span.Slice(0, read);
                source.CopyTo(destination);
                _prefix = _prefix.Slice(read);
                if (_prefix.IsEmpty)
                {
                    _prefix = new Memory<byte>(Array.Empty<byte>());
                }
                return read;
            }
        }
    }
}
