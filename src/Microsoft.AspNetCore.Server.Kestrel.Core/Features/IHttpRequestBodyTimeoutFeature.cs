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
        /// The maximum amount of time in which the request body should be fully received.
        /// </summary>
        TimeSpan RequestBodyTimeout { get; set; }

        /// <summary>
        /// The minimum data rate in bytes/second at which the request body should be received.
        /// </summary>
        double MinimumDataRate { get; }

        /// <summary>
        /// The amount of time to delay enforcement of <see cref="MinimumDataRate" />.
        /// When set to <see cref="TimeSpan.Zero"/>, enforcement begins when the server starts reading the request body.
        /// </summary>
        TimeSpan MinimumDataRateGracePeriod { get; }

        /// <summary>
        /// Sets the minimum incoming data rate at which the request body should be received.
        /// </summary>
        /// <param name="minimumDataRate">The minimum data rate in bytes/second at which the request body should be received.</param>
        /// <param name="gracePeriod">
        /// The amount of time to delay enforcement of <paramref name="minimumDataRate"/>.
        /// When set to <see cref="TimeSpan.Zero"/>, enforcement begins when the server starts reading the request body.
        /// </param>
        void SetMinimumDataRate(double minimumDataRate, TimeSpan gracePeriod);
    }
}
