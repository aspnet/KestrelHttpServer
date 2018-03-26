// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Testing
{
    public class TestConstants
    {
        public const int EOF = -4095;
        public static TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
        public static LogLevel DefaultFunctionalTestLogLevel = LogLevel.Information;
    }
}
