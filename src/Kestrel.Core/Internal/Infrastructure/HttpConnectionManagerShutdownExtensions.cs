﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public static class HttpConnectionManagerShutdownExtensions
    {
        public static async Task<bool> CloseAllConnectionsAsync(this HttpConnectionManager connectionManager, CancellationToken token)
        {
            var closeTasks = new List<Task>();

            connectionManager.Walk(connection =>
            {
                closeTasks.Add(connection.StopProcessingNextRequestAsync());
            });

            var allClosedTask = Task.WhenAll(closeTasks.ToArray());
            return await Task.WhenAny(allClosedTask, CancellationTokenAsTask(token)).ConfigureAwait(false) == allClosedTask;
        }

        public static async Task<bool> AbortAllConnectionsAsync(this HttpConnectionManager connectionManager)
        {
            var abortTasks = new List<Task>();
            var canceledException = new ConnectionAbortedException();

            connectionManager.Walk(connection =>
            {
                abortTasks.Add(connection.AbortAsync(canceledException));
            });

            var allAbortedTask = Task.WhenAll(abortTasks.ToArray());
            return await Task.WhenAny(allAbortedTask, Task.Delay(1000)).ConfigureAwait(false) == allAbortedTask;
        }

        private static Task CancellationTokenAsTask(CancellationToken token)
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
