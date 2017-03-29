﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class ConnectionManager
    {
        private readonly KestrelThread _thread;

        public ConnectionManager(KestrelThread thread)
        {
            _thread = thread;
        }

        public async Task<bool> WalkConnectionsAndCloseAsync(TimeSpan timeout)
        {
            return await WalkConnectionsAsync((connectionManager, tcs) => connectionManager.WalkConnectionsAndCloseCore(tcs), timeout).ConfigureAwait(false);
        }

        public async Task<bool> WalkConnectionsAndAbortAsync(TimeSpan timeout)
        {
            return await WalkConnectionsAsync((connectionManager, tcs) => connectionManager.WalkConnectionsAndAbortCore(tcs), timeout).ConfigureAwait(false);
        }

        private async Task<bool> WalkConnectionsAsync(Action<ConnectionManager, TaskCompletionSource<object>> action, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<object>();

            _thread.Post(state => action(state, tcs), this);

            return await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(false) == tcs.Task;
        }

        private void WalkConnectionsAndCloseCore(TaskCompletionSource<object> tcs)
        {
            WalkConnectionsCore(connection => connection.StopAsync(), tcs);
        }

        private void WalkConnectionsAndAbortCore(TaskCompletionSource<object> tcs)
        {
            WalkConnectionsCore(connection => connection.AbortAsync(), tcs);
        }

        private void WalkConnectionsCore(Func<Connection, Task> action, TaskCompletionSource<object> tcs)
        {
            var tasks = new List<Task>();

            _thread.Walk(ptr =>
            {
                var handle = UvMemory.FromIntPtr<UvHandle>(ptr);
                var connection = (handle as UvStreamHandle)?.Connection;

                if (connection != null)
                {
                    tasks.Add(action(connection));
                }
            });

            Task.Run(() =>
            {
                try
                {
                    Task.WaitAll(tasks.ToArray());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    return;
                }

                tcs.SetResult(null);
            });
        }
    }
}
