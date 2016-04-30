// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Abstractions;
using System;

namespace Microsoft.AspNetCore.Server.Kestrel
{

    public interface IKestrelEngine : IDisposable
    {
        void Start(ServiceContext context);

        IDisposable CreateServer(ServerAddress address);
    }
}