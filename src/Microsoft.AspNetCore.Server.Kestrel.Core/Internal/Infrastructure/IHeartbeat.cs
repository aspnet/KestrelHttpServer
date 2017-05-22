// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public interface IHeartbeat : IDisposable
    {
        void Start();

        long AddHandler(IHeartbeatHandler handler);

        void RemoveHandler(long id);
    }
}
