﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Features
{
    public class MinimumDataRate
    {
        /// <summary>
        /// Creates a new instance of <see cref="MinimumDataRate"/>.
        /// </summary>
        /// <param name="bytesPerSecond">The minimum rate in bytes/second at which data should be processed.</param>
        /// <param name="gracePeriod">The amount of time to delay enforcement of <paramref name="bytesPerSecond"/>.</param>
        public MinimumDataRate(double bytesPerSecond, TimeSpan gracePeriod)
        {
            if (bytesPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesPerSecond), CoreStrings.PositiveNumberRequired);
            }

            if (gracePeriod < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(gracePeriod), CoreStrings.NonNegativeTimeSpanRequired);
            }

            BytesPerSecond = bytesPerSecond;
            GracePeriod = gracePeriod;
        }

        /// <summary>
        /// The minimum rate in bytes/second at which data should be processed.
        /// </summary>
        public double BytesPerSecond { get; }

        /// <summary>
        /// The amount of time to delay enforcement of <see cref="MinimumDataRate" />.
        /// </summary>
        public TimeSpan GracePeriod { get; }
    }
}
