// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Filter;
using Microsoft.AspNetCore.Server.Kestrel.Https.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Https
{
    public class HttpsConnectionFilter : IConnectionFilter
    {
        private static readonly ClosedStream _closedStream = new ClosedStream();

        private readonly HttpsConnectionFilterOptions _options;
        private readonly IConnectionFilter _previous;
        private readonly ILogger _logger;

        public HttpsConnectionFilter(HttpsConnectionFilterOptions options, IConnectionFilter previous)
            : this(options, previous, loggerFactory: null)
        {
        }

        public HttpsConnectionFilter(HttpsConnectionFilterOptions options, IConnectionFilter previous, ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (previous == null)
            {
                throw new ArgumentNullException(nameof(previous));
            }
            if (options.ServerCertificate == null)
            {
                throw new ArgumentException("The server certificate parameter is required.");
            }

            _options = options;
            _previous = previous;
            _logger = loggerFactory?.CreateLogger(nameof(HttpsConnectionFilter));
        }

        public async Task OnConnectionAsync(ConnectionFilterContext context)
        {
            await _previous.OnConnectionAsync(context);

            if (string.Equals(context.Address.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                SslStream sslStream;
                bool certificateRequired;

                if (_options.ClientCertificateMode == ClientCertificateMode.NoCertificate)
                {
                    sslStream = new SslStream(context.Connection);
                    certificateRequired = false;
                }
                else
                {
                    sslStream = new SslStream(context.Connection, leaveInnerStreamOpen: false,
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
                    await sslStream.AuthenticateAsServerAsync(_options.ServerCertificate, certificateRequired,
                            _options.SslProtocols, _options.CheckCertificateRevocation);
                }
                catch (IOException ex)
                {
                    _logger?.LogInformation(1, ex, "Failed to authenticate HTTPS connection.");

                    sslStream.Dispose();
                    context.Connection = _closedStream;

                    return;
                }

                var previousPrepareRequest = context.PrepareRequest;
                context.PrepareRequest = features =>
                {
                    previousPrepareRequest?.Invoke(features);

                    var clientCertificate = ConvertToX509Certificate2(sslStream.RemoteCertificate);
                    if (clientCertificate != null)
                    {
                        features.Set<ITlsConnectionFeature>(new TlsConnectionFeature { ClientCertificate = clientCertificate });
                    }

                    features.Get<IHttpRequestFeature>().Scheme = "https";
                };

                context.Connection = new HttpsConnectionFilterStream(sslStream);
            }
        }

        private X509Certificate2 ConvertToX509Certificate2(X509Certificate certificate)
        {
            if (certificate == null)
            {
                return null;
            }

            X509Certificate2 certificate2 = certificate as X509Certificate2;
            if (certificate2 != null)
            {
                return certificate2;
            }

#if NETSTANDARD1_3
            // conversion X509Certificate to X509Certificate2 not supported
            // https://github.com/dotnet/corefx/issues/4510
            return null;
#else
            return new X509Certificate2(certificate);
#endif
        }

        private class HttpsConnectionFilterStream : Stream
        {
            private readonly Stream _inner;

            public HttpsConnectionFilterStream(Stream inner)
            {
                _inner = inner;
            }

            public override bool CanRead => _inner.CanRead;

            public override bool CanSeek => _inner.CanSeek;

            public override bool CanWrite => _inner.CanWrite;

            public override long Length => _inner.Length;

            public override long Position
            {
                get
                {
                    return _inner.Position;
                }
                set
                {
                    _inner.Position = value;
                }
            }

            public override void Flush() => _inner.Flush();

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }
    }
}
