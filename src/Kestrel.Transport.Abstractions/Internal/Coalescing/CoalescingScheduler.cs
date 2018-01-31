// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal.Coalescing
{
    public abstract class CoalescingScheduler : PipeScheduler
    {
        // Maximum times the work queues swapped and are processed in a single pass
        private const int _maxLoops = 8;

        private readonly PipeScheduler _innerScheduler;
        private readonly object _workSync = new object();
        private Queue<Work> _workAdding = new Queue<Work>(1024);
        private Queue<Work> _workRunning = new Queue<Work>(1024);

        protected CoalescingScheduler(PipeScheduler innerScheduler)
        {
            _innerScheduler = innerScheduler;
        }

        public override void Schedule(Action action)
        {
            Schedule(state => ((Action)state).Invoke(), action);
        }

        public override void Schedule(Action<object> action, object state)
        {
            var work = new Work
            {
                Callback = action,
                State = state
            };

            lock (_workSync)
            {
                _workAdding.Enqueue(work);
            }

            // TODO: Trip event to reduce latency in low-IO scenarios
        }

        protected void DoWork()
        {
            var loopsRemaining = _maxLoops;
            bool wasWork;
            do
            {
                Queue<Work> queue;
                lock (_workSync)
                {
                    queue = _workAdding;
                    _workAdding = _workRunning;
                    _workRunning = queue;
                }

                wasWork = queue.Count > 0;

                while (queue.Count != 0)
                {
                    var work = queue.Dequeue();
                    _innerScheduler.Schedule(work.Callback, work.State);
                }
            } while (wasWork && loopsRemaining > 0);
        }

        private struct Work
        {
            public Action<object> Callback;
            public object State;
        }
    }
}