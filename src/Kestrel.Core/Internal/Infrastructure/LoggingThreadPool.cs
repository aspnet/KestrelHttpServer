// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public class LoggingThreadPool : KestrelThreadPool
    {
        [ThreadStatic]
        private static ActionCache _actionCache;
        private static ActionCache Cache => _actionCache ?? InitializeActionCache();

        private readonly IKestrelTrace _log;

        private WaitCallback _runAction;
        private WaitCallback _runActionState;

        public LoggingThreadPool(IKestrelTrace log)
        {
            _log = log;

            // Curry and capture log in closures once
            // The currying is done in functions of the same name to improve the
            // call stack for exceptions and profiling else it shows up as LoggingThreadPool.ctor>b__4_0
            // and you aren't sure which of the 2 functions was called.
            RunAction();
            RunActionState();
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

        private void RunActionState()
        {
            // Capture _log in a singleton closure
            _runActionState = (o) =>
            {
                var actionObject = (ActionObject)o;
                try
                {
                    actionObject.Run();
                }
                catch (Exception e)
                {
                    _log.LogError(0, e, "LoggingThreadPool.RunActionState");
                }
                finally
                {
                    actionObject.Return();
                }
            };
        }

        public override void Run(Action action) => QueueToThreadPool(_runAction, action);

        public override void Schedule(Action action) => QueueToThreadPool(_runAction, action);

        public override void UnsafeRun(WaitCallback action, object state) => QueueToThreadPool(action, state);

        public override void Schedule(Action<object> action, object state)
            => QueueToThreadPool(_runActionState, Cache.Rent(action, state));

        // Non-virtual
        private static void QueueToThreadPool(WaitCallback action, object state)
        {
#if NETCOREAPP2_1
            // Queue to low contention local ThreadPool queue; rather than global queue as per Task
            ThreadPool.QueueUserWorkItem(action, state, preferLocal: true);
#else
            ThreadPool.QueueUserWorkItem(action, state);
#endif
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ActionCache InitializeActionCache()
        {
            var actionCache = new ActionCache();
            _actionCache = actionCache;
            return actionCache;
        }

        private class ActionCache
        {
            private volatile ActionObject _actionObject;

            public ActionObject Rent(Action<object> action, object state)
            {
                // Note: ThreadStatic value
                var actionObject = _actionObject;
                if (actionObject != null)
                {
                    // Clear so next ThreadStatic access doesn't pick up same
                    _actionObject = null;
                }
                else
                {
                    actionObject = new ActionObject(this);
                }

                actionObject.Initialize(action, state);
                return actionObject;
            }

            public void Return(ActionObject action)
            {
                // Note: Return happens on different thread
                _actionObject = action;
            }
        }

        private class ActionObject
        {
            private readonly ActionCache _actionCache;
            private Action<object> _action;
            private object _state;

            public ActionObject(ActionCache actionCache)
            {
                _actionCache = actionCache;
            }

            public void Initialize(Action<object> action, object state)
            {
                _action = action;
                _state = state;
            }

            public void Return()
            {
                _action = null;
                _state = null;
                _actionCache.Return(this);
            }

            public void Run() => _action(_state);
        }
    }
}