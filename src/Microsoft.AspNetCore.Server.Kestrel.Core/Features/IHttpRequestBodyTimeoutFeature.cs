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
        /// The minimum data rate at which the request body should be received.
        /// </summary>
        double MinimumDataRate { get; }

        /// <summary>
        /// The period of time during which the minimum data rate will not be enforced.
        /// </summary>
        TimeSpan MinimumDataRateGracePeriod { get; }

        /// <summary>
        /// Sets the minimum incoming data rate at which the request body should be received.
        /// </summary>
        /// <param name="minimumDataRate">The minimum incoming data rate in bytes/second.</param>
        /// <param name="gracePeriod">The period of time during which the minimum data rate will not be enforced.</param>
        void SetMinimumDataRate(double minimumDataRate, TimeSpan gracePeriod);
    }
}
