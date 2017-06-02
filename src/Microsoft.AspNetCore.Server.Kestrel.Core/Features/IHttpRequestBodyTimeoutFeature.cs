// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Features
{
    /// <summary>
    /// Represents a timeout for the request body in an HTTP request.
    /// </summary>
    public interface IHttpRequestBodyTimeoutFeature
    {
        /// <summary>
        /// The minimum amount of time allowed for the request body to be entirely read.
        /// </summary>
        TimeSpan Timeout { get; }

        /// <summary>
        /// The maximum amount of time allowed for the request body to be entirely read.
        /// Only effective when <see cref="MinimumDataRate"/> is also set.
        /// </summary>
        TimeSpan? ExtendedTimeout { get; }

        /// <summary>
        /// The minimum incoming data rate in bytes/second that the request should be read at after
        /// <see cref="Timeout"/> has elapsed.
        /// </summary>
        double? MinimumDataRate { get; }

        /// <summary>
        /// Configures a simple timeout for the request body.
        /// </summary>
        /// <param name="timeout">The time within which the request body should be fully read.</param>
        void Configure(TimeSpan timeout);

        /// <summary>
        /// Configures a timeout for the request body which can be extended based on the incoming data rate.
        /// </summary>
        /// <param name="timeout">The minimum amount of time within which the request body should be fully read.</param>
        /// <param name="extendedTimeout">The extended amount of time within which the request body should be fully read,
        /// as long as it is being received at the specified minimum data rate.</param>
        /// <param name="minimumDataRate">The minimum incoming data rate the request body should be received after
        /// the initial timeout period.</param>
        void Configure(TimeSpan timeout, TimeSpan extendedTimeout, double minimumDataRate);
    }
}
