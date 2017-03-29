﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport
{
    public interface IConnectionContext
    {
        string ConnectionId { get; }
        IPipeWriter Input { get; }
        IPipeReader Output { get; }

        // TODO: Remove these (Use Pipes instead?)
        Task StopAsync();
        void Abort(Exception ex);
        void SetBadRequestState(RequestRejectionReason reason);
    }
}
