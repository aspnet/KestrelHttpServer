// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Libuv.Internal;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Testing
{
    public class MockConnection : Connection, IDisposable
    {
        private readonly TaskCompletionSource<object> _socketClosedTcs = new TaskCompletionSource<object>();

        public MockConnection()
        {
            TimeoutControl = this;
            RequestAbortedSource = new CancellationTokenSource();
            ListenerContext = new ListenerContext(new LibuvTransportContext());
        }

        public override Task AbortAsync(Exception error = null)
        {
            RequestAbortedSource?.Cancel();
            return TaskCache.CompletedTask;
        }

        public override void OnSocketClosed()
        {
            _socketClosedTcs.SetResult(null);
        }

        public CancellationTokenSource RequestAbortedSource { get; }

        public Task SocketClosed => _socketClosedTcs.Task;

        public void Dispose()
        {
            RequestAbortedSource.Dispose();
        }
    }
}
