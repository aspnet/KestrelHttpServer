// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public class FrameConnectionManager : IHeartbeatHandler
    {
        private readonly ConcurrentDictionary<long, FrameConnection> _connections
            = new ConcurrentDictionary<long, FrameConnection>();

        public void AddConnection(long id, FrameConnection connection)
        {
            if (!_connections.TryAdd(id, connection))
            {
                throw new ArgumentException(nameof(id));
            }
        }

        public void RemoveConnection(long id)
        {
            if (!_connections.TryRemove(id, out _))
            {
                throw new ArgumentException(nameof(id));
            }
        }

        public void OnHeartbeat(DateTimeOffset now)
        {
            foreach (var kvp in _connections)
            {
                kvp.Value.Tick(now);
            }
        }

        public async Task<bool> CloseAllConnectionsAsync(CancellationToken token)
        {
            var allStoppedTask = Task.WhenAll(_connections.Select(kvp => kvp.Value.StopAsync()).ToArray());
            return await Task.WhenAny(allStoppedTask, CancellationTokenAsTask(token)).ConfigureAwait(false) == allStoppedTask;
        }

        public async Task<bool> AbortAllConnectionsAsync()
        {
            var timeoutEx = new TimeoutException("Request processing didn't complete within configured timeout.");

            var allAbortedTask = Task.WhenAll(_connections.Select(kvp => kvp.Value.AbortAsync(timeoutEx)).ToArray());
            return await Task.WhenAny(allAbortedTask, Task.Delay(1000)).ConfigureAwait(false) == allAbortedTask;
        }

        private Task CancellationTokenAsTask(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.SetResult(null));
            return tcs.Task;
        }
    }
}
