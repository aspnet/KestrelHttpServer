// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public class LoggingPipeSchedulerWrapper : PipeScheduler
    {
        private readonly PipeScheduler _innerScheduler;
        private readonly IKestrelTrace _log;
        private Action<object> _runAction;

        public LoggingPipeSchedulerWrapper(PipeScheduler innerScheduler, IKestrelTrace log)
        {
            _innerScheduler = innerScheduler;
            _log = log;

            // Curry and capture log in closure once
            // The currying is done in functions of the same name to improve the
            // call stack for exceptions and profiling else it shows up as LoggingThreadPool.ctor>b__4_0
            // and you aren't sure which of the 3 functions was called.
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

        public override void Schedule(Action action)
        {
            _innerScheduler.Schedule(_runAction, action);
        }

        // REVIEW: This allocates a closure per call like the old LoggingThreadPool but unlike
        // the old InilneLoggingThreadPool. I'm pretty sure Pipes only uses this overload to
        // invoke completion callbacks which is pretty rare.
        public override void Schedule(Action<object> action, object state)
        {
            Schedule(() => action(state));
        }
    }
}
