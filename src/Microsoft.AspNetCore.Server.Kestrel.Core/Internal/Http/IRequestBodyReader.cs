// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public interface IRequestBodyReader
    {
        Task StartAsync(MessageBody messageBody, CancellationToken cancellationToken = default(CancellationToken));

        void Reset();

        Task<int> ReadAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken = default(CancellationToken));

        Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default(CancellationToken));

        Task ConsumeAsync(CancellationToken cancellationToken = default(CancellationToken));
    }
}
