// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Abstractions;
using Microsoft.AspNetCore.Server.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Infrastructure
{
    public class LoggingThreadPool : IThreadPool
    {
        private readonly IConnectionTrace _log;

        private readonly WaitCallback _runAction;
        private readonly WaitCallback _cancelTcs;
        private readonly WaitCallback _completeTcs;

        public LoggingThreadPool(IConnectionTrace log)
        {
            _log = log;

            // Curry and capture log in closures once
            _runAction = (o) =>
            {
                try
                {
                    ((Action)o)();
                }
                catch (Exception e)
                {
                    _log.LogError(0, e, "LoggingThreadPool.Run");
                }
            };

            _completeTcs = (o) =>
            {
                try
                {
                    ((TaskCompletionSource<object>)o).TrySetResult(null);
                }
                catch (Exception e)
                {
                    _log.LogError(0, e, "LoggingThreadPool.Complete");
                }
            };

            _cancelTcs = (o) =>
            {
                try
                {
                    ((TaskCompletionSource<object>)o).TrySetCanceled();
                }
                catch (Exception e)
                {
                    _log.LogError(0, e, "LoggingThreadPool.Cancel");
                }
            };
        }

        public void Run(Action action)
        {
            ThreadPool.QueueUserWorkItem(_runAction, action);
        }

        public void Complete(TaskCompletionSource<object> tcs)
        {
            ThreadPool.QueueUserWorkItem(_completeTcs, tcs);
        }

        public void Cancel(TaskCompletionSource<object> tcs)
        {
            ThreadPool.QueueUserWorkItem(_cancelTcs, tcs);
        }

        public void Error(TaskCompletionSource<object> tcs, Exception ex)
        {
            // ex and _log are closure captured 
            ThreadPool.QueueUserWorkItem((o) =>
            {
                try
                {
                    ((TaskCompletionSource<object>)o).TrySetException(ex);
                }
                catch (Exception e)
                {
                    _log.LogError(0, e, "LoggingThreadPool.Error");
                }
            }, tcs);
        }
    }
}