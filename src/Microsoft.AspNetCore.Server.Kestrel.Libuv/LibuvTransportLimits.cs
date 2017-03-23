// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Libuv
{
    // REVIEW: Do we really want to configure these limits at the transport level?
    public class LibuvTransportLimits
    {
        // Matches the non-configurable default response buffer size for Kestrel in 1.0.0
        private long? _maxResponseBufferSize = 64 * 1024;

        // Matches the default client_max_body_size in nginx.  Also large enough that most requests
        // should be under the limit.
        private long? _maxRequestBufferSize = 1024 * 1024;

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
            get
            {
                return _maxResponseBufferSize;
            }
            set
            {
                if (value.HasValue && value.Value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Value must be null or a non-negative integer.");
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
            get
            {
                return _maxRequestBufferSize;
            }
            set
            {
                if (value.HasValue && value.Value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Value must be null or a positive integer.");
                }
                _maxRequestBufferSize = value;
            }
        }
    }
}
