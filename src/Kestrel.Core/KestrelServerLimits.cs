﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace Microsoft.AspNetCore.Server.Kestrel.Core
{
    public class KestrelServerLimits
    {
        // Matches the non-configurable default response buffer size for Kestrel in 1.0.0
        private long? _maxResponseBufferSize = 64 * 1024;

        // Matches the default client_max_body_size in nginx.
        // Also large enough that most requests should be under the limit.
        private long? _maxRequestBufferSize = 1024 * 1024;

        // Matches the default large_client_header_buffers in nginx.
        private int _maxRequestLineSize = 8 * 1024;

        // Matches the default large_client_header_buffers in nginx.
        private int _maxRequestHeadersTotalSize = 32 * 1024;

        // Matches the default maxAllowedContentLength in IIS (~28.6 MB)
        // https://www.iis.net/configreference/system.webserver/security/requestfiltering/requestlimits#005
        private long? _maxRequestBodySize = 30000000;

        // Matches the default LimitRequestFields in Apache httpd.
        private int _maxRequestHeaderCount = 100;

        // Matches the default http.sys connectionTimeout.
        private TimeSpan _keepAliveTimeout = TimeSpan.FromMinutes(2);

        private TimeSpan _requestHeadersTimeout = TimeSpan.FromSeconds(30);

        // Unlimited connections are allowed by default.
        private long? _maxConcurrentConnections = null;
        private long? _maxConcurrentUpgradedConnections = null;

        /// <summary>
        /// Gets or sets the maximum size of the response buffer before write
        /// calls begin to block or return tasks that don't complete until the
        /// buffer size drops below the configured limit.
        /// </summary>
        /// <remarks>
        /// When set to null, the size of the response buffer is unlimited.
        /// When set to zero, all write calls will block or return tasks that
        /// don't complete until the entire response buffer is flushed.
        /// Defaults to 65,536 bytes (64 KB).
        /// </remarks>
        public long? MaxResponseBufferSize
        {
            get => _maxResponseBufferSize;
            set
            {
                if (value.HasValue && value.Value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.NonNegativeNumberOrNullRequired);
                }
                _maxResponseBufferSize = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum size of the request buffer.
        /// </summary>
        /// <remarks>
        /// When set to null, the size of the request buffer is unlimited.
        /// Defaults to 1,048,576 bytes (1 MB).
        /// </remarks>
        public long? MaxRequestBufferSize
        {
            get => _maxRequestBufferSize;
            set
            {
                if (value.HasValue && value.Value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.PositiveNumberOrNullRequired);
                }
                _maxRequestBufferSize = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum allowed size for the HTTP request line.
        /// </summary>
        /// <remarks>
        /// Defaults to 8,192 bytes (8 KB).
        /// </remarks>
        public int MaxRequestLineSize
        {
            get => _maxRequestLineSize;
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.PositiveNumberRequired);
                }
                _maxRequestLineSize = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum allowed size for the HTTP request headers.
        /// </summary>
        /// <remarks>
        /// Defaults to 32,768 bytes (32 KB).
        /// </remarks>
        public int MaxRequestHeadersTotalSize
        {
            get => _maxRequestHeadersTotalSize;
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.PositiveNumberRequired);
                }
                _maxRequestHeadersTotalSize = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum allowed number of headers per HTTP request.
        /// </summary>
        /// <remarks>
        /// Defaults to 100.
        /// </remarks>
        public int MaxRequestHeaderCount
        {
            get => _maxRequestHeaderCount;
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.PositiveNumberRequired);
                }
                _maxRequestHeaderCount = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum allowed size of any request body in bytes.
        /// When set to null, the maximum request body size is unlimited.
        /// This limit has no effect on upgraded connections which are always unlimited.
        /// This can be overridden per-request via <see cref="IHttpMaxRequestBodySizeFeature"/>.
        /// </summary>
        /// <remarks>
        /// Defaults to 30,000,000 bytes, which is approximately 28.6MB.
        /// </remarks>
        public long? MaxRequestBodySize
        {
            get => _maxRequestBodySize;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.NonNegativeNumberOrNullRequired);
                }
                _maxRequestBodySize = value;
            }
        }

        /// <summary>
        /// Gets or sets the keep-alive timeout.
        /// </summary>
        /// <remarks>
        /// Defaults to 2 minutes.
        /// </remarks>
        public TimeSpan KeepAliveTimeout
        {
            get => _keepAliveTimeout;
            set
            {
                if (value <= TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.PositiveTimeSpanRequired);
                }
                _keepAliveTimeout = value != Timeout.InfiniteTimeSpan ? value : TimeSpan.MaxValue;
            }
        }

        /// <summary>
        /// Gets or sets the maximum amount of time the server will spend receiving request headers.
        /// </summary>
        /// <remarks>
        /// Defaults to 30 seconds.
        /// </remarks>
        public TimeSpan RequestHeadersTimeout
        {
            get => _requestHeadersTimeout;
            set
            {
                if (value <= TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.PositiveTimeSpanRequired);
                }
                _requestHeadersTimeout = value != Timeout.InfiniteTimeSpan ? value : TimeSpan.MaxValue;
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of open connections. When set to null, the number of connections is unlimited.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Defaults to null.
        /// </para>
        /// <para>
        /// When a connection is upgraded to another protocol, such as WebSockets, its connection is counted against the
        /// <see cref="MaxConcurrentUpgradedConnections" /> limit instead of <see cref="MaxConcurrentConnections" />.
        /// </para>
        /// </remarks>
        public long? MaxConcurrentConnections
        {
            get => _maxConcurrentConnections;
            set
            {
                if (value.HasValue && value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.PositiveNumberOrNullRequired);
                }
                _maxConcurrentConnections = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of open, upgraded connections. When set to null, the number of upgraded connections is unlimited.
        /// An upgraded connection is one that has been switched from HTTP to another protocol, such as WebSockets.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Defaults to null.
        /// </para>
        /// <para>
        /// When a connection is upgraded to another protocol, such as WebSockets, its connection is counted against the
        /// <see cref="MaxConcurrentUpgradedConnections" /> limit instead of <see cref="MaxConcurrentConnections" />.
        /// </para>
        /// </remarks>
        public long? MaxConcurrentUpgradedConnections
        {
            get => _maxConcurrentUpgradedConnections;
            set
            {
                if (value.HasValue && value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), CoreStrings.NonNegativeNumberOrNullRequired);
                }
                _maxConcurrentUpgradedConnections = value;
            }
        }

        /// <summary>
        /// Gets or sets the request body minimum data rate in bytes/second.
        /// Setting this property to null indicates no minimum data rate should be enforced.
        /// This limit has no effect on upgraded connections which are always unlimited.
        /// This can be overridden per-request via <see cref="IHttpMinRequestBodyDataRateFeature"/>.
        /// </summary>
        /// <remarks>
        /// Defaults to 240 bytes/second with a 5 second grace period.
        /// </remarks>
        public MinDataRate MinRequestBodyDataRate { get; set; } =
            // Matches the default IIS minBytesPerSecond
            new MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5));

        /// <summary>
        /// Gets or sets the response minimum data rate in bytes/second.
        /// Setting this property to null indicates no minimum data rate should be enforced.
        /// This limit has no effect on upgraded connections which are always unlimited.
        /// This can be overridden per-request via <see cref="IHttpMinResponseDataRateFeature"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Defaults to 240 bytes/second with a 5 second grace period.
        /// </para>
        /// <para>
        /// Contrary to the request body minimum data rate, this rate applies to the response status line and headers as well.
        /// </para>
        /// <para>
        /// This rate is enforced per write operation instead of being averaged over the life of the response. Whenever the server
        /// writes a chunk of data, a timer is set to the maximum of the grace period set in this property or the length of the write in
        /// bytes divided by the data rate (i.e. the maximum amount of time that write should take to complete with the specified data rate).
        /// The connection is aborted if the write has not completed by the time that timer expires.
        /// </para>
        /// </remarks>
        public MinDataRate MinResponseDataRate { get; set; } =
            // Matches the default IIS minBytesPerSecond
            new MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5));
    }
}
