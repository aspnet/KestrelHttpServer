// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.AspNetCore.Connections.Features
{
    public interface IConnectionLifetimeFeature
    {
        CancellationToken ConnectionClosed { get; set; }

        void Abort();

        // REVIEW: This is technically a breaking change for implementors of this interface,
        // but realistically this only affects transport implementations which today must
        // implement other pubternal interfaces or facades which I doubt exist.
        void Abort(ConnectionAbortedException abortReason);
    }
}
