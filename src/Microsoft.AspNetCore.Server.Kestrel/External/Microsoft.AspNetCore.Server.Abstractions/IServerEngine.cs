// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Abstractions
{

    public interface IServerEngine : IDisposable
    {
        void Start(ServiceContext context);

        IDisposable CreateServer(ServerAddress address);
    }
}