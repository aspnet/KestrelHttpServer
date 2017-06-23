// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Features
{
    /// <summary>
    /// Represents a minimum data rate for the request body of an HTTP request.
    /// </summary>
    public interface IHttpMinRequestBodyDataRateFeature
    {
        /// <summary>
        /// The minimum data rate in bytes/second at which the request body should be received.
        /// Setting this property to zero effectively means no minimum data rate should be enforced.
        /// This limit has no effect on upgraded connections which are always unlimited.
        /// </summary>
        double BytesPerSecond { get; set; }

        /// <summary>
        /// The amount of time to delay enforcement of the minimum data rate.
        /// </summary>
        TimeSpan GracePeriod { get; set; }
    }
}
