// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public interface IHttpOutputProducer : IDisposable
    {
        void Abort(Exception error);
        void WriteResponseHeaders(int statusCode, string ReasonPhrase, FrameResponseHeaders responseHeaders);
        Task FlushAsync(CancellationToken cancellationToken);
        Task Write100ContinueAsync(CancellationToken cancellationToken);
        Task WriteDataAsync(ArraySegment<byte> data, bool chunk, CancellationToken cancellationToken);
        Task WriteStreamSuffixAsync(CancellationToken cancellationToken);
    }
}
