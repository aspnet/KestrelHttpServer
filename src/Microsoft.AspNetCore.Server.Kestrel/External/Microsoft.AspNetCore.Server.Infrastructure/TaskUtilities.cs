// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Infrastructure
{
    public static class TaskUtilities
    {
#if NETSTANDARD1_3
        public static Task CompletedTask = Task.CompletedTask;
#else
        public static Task CompletedTask = Task.FromResult<object>(null);
#endif
        public static Task<int> ZeroTask = Task.FromResult(0);

        public static Task GetCancelledTask(CancellationToken cancellationToken)
        {
#if NETSTANDARD1_3
            return Task.FromCanceled(cancellationToken);
#else
            var tcs = new TaskCompletionSource<object>();
            tcs.TrySetCanceled();
            return tcs.Task;
#endif
        }

        public static Task<int> GetCancelledZeroTask(CancellationToken cancellationToken = default(CancellationToken))
        {
#if NETSTANDARD1_3
            return Task.FromCanceled<int>(cancellationToken);
#else
            var tcs = new TaskCompletionSource<int>();
            tcs.TrySetCanceled();
            return tcs.Task;
#endif
        }
    }
}