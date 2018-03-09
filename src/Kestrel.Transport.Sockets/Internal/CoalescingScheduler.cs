// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class CoalescingScheduler : PipeScheduler
    {
        private static readonly WaitCallback _doWorkCallback = s => ((CoalescingScheduler)s).DoWork();

        private readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<Work> _workItems = new ConcurrentQueue<Work>();

        private readonly object _workSync = new object();
        private bool _doingWork;

        public virtual void Schedule(Action action)
        {
            _actions.Enqueue(action);
            TriggerWork();
        }

        public override void Schedule<T>(Action<T> action, T state)
        {
            var work = new Work
            {
                CallbackAdapter = (c, s) => ((Action<T>)c)((T)s),
                Callback = action,
                State = state
            };

            _workItems.Enqueue(work);
            TriggerWork();
        }

        private void TriggerWork()
        {
            lock (_workSync)
            {
                if (!_doingWork)
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(_doWorkCallback, this);
                    _doingWork = true;
                }
            }
        }

        private void DoWork()
        {
            while (true)
            {
                while (_actions.TryDequeue(out Action action))
                {
                    action();
                }

                while (_workItems.TryDequeue(out Work item))
                {
                    item.CallbackAdapter(item.Callback, item.State);
                }

                lock (_workSync)
                {
                    if (_actions.IsEmpty && _workItems.IsEmpty)
                    {
                        _doingWork = false;
                        return;
                    }
                }
            }
        }

        private struct Work
        {
            public Action<object, object> CallbackAdapter;
            public object Callback;
            public object State;
        }
    }
}
