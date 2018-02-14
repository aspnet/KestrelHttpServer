// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public class LoggingThreadPool : KestrelThreadPool
    {
        private readonly IKestrelTrace _log;

        private WaitCallback _runAction;

        public LoggingThreadPool(IKestrelTrace log)
        {
            _log = log;

            // Curry and capture log in closure once
            // The currying is done in function of the same name to improve the
            // call stack for exceptions and profiling else it shows up as LoggingThreadPool.ctor>b__4_0
            RunAction();
        }

        private void RunAction()
        {
            // Capture _log in a singleton closure
            _runAction = (o) =>
            {
                try
                {
                    ((Action)o)();
                }
                catch (Exception e)
                {
                    _log.LogError(0, e, "LoggingThreadPool.RunAction");
                }
            };
        }

        public override void Run(Action action)
        {
            if (Thread.CurrentThread.IsThreadPoolThread)
            {
                RunIsOnThreadPool(action);
            }
            else
            {
                RunOnThreadPool(action);
            }
        }

        public override void UnsafeRun(WaitCallback action, object state)
        {
            if (Thread.CurrentThread.IsThreadPoolThread)
            {
                action(state);
            }
            else
            {
                System.Threading.ThreadPool.QueueUserWorkItem(action, state);
            }
        }

        public override void Schedule(Action action)
        {
            if (Thread.CurrentThread.IsThreadPoolThread)
            {
                RunIsOnThreadPool(action);
            }
            else
            {
                RunOnThreadPool(action);
            }
        }

        public override void Schedule(Action<object> action, object state)
        {
            if (Thread.CurrentThread.IsThreadPoolThread)
            {
                RunIsOnThreadPool(action, state);
            }
            else
            {
                // Closure outside of method as C# compiler will make it anyway if not used
                // https://github.com/dotnet/roslyn/issues/22589
                RunOnThreadPoolWithClosure(action, state);
            }
        }

        // Non-virtual methods as common point to call through to

        private void RunOnThreadPoolWithClosure(Action<object> action, object state)
        {
            Action closure = () => action(state);
            System.Threading.ThreadPool.QueueUserWorkItem(_runAction, closure);
        }

        private void RunOnThreadPool(Action action)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_runAction, action);
        }

        private void RunIsOnThreadPool(Action<object> action, object state)
        {
            try
            {
                action(state);
            }
            catch (Exception e)
            {
                _log.LogError(0, e, "LoggingThreadPool.RunIsOnThreadPool");
            }
        }

        private void RunIsOnThreadPool(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                _log.LogError(0, e, "LoggingThreadPool.RunIsOnThreadPool");
            }
        }
    }
}