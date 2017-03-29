﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport
{
    public interface IConnectionHandler
    {
        IConnectionContext OnConnection(IConnectionInformation connectionInfo, IScheduler inputWriterScheduler, IScheduler outputReaderScheduler);
    }
}
