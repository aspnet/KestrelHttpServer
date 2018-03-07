// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class CoalescingScheduler
    {
        // Maximum times the work queues swapped and are processed in a single pass
        private const int _maxLoops = 8;

        private readonly object _workSync = new object();
        private bool _doingWork;
        private Queue<Action> _workAdding = new Queue<Action>(1024);
        private Queue<Action> _workRunning = new Queue<Action>(1024);

        public void Schedule(Action action)
        {
            bool scheduleWork;

            lock (_workSync)
            {
                _workAdding.Enqueue(action);
                scheduleWork = !_doingWork;
                _doingWork = true;
            }

            if (scheduleWork)
            {
                ThreadPool.QueueUserWorkItem(s => ((CoalescingScheduler)s).DoWork(), this);
            }
        }

        private void DoWork()
        {
            for (var i = 0; i < _maxLoops; i++)
            {
                Queue<Action> queue;
                lock (_workSync)
                {
                    queue = _workAdding;
                    _workAdding = _workRunning;
                    _workRunning = queue;

                    if (queue.Count == 0)
                    {
                        _doingWork = false;
                        return;
                    }
                }

                while (queue.Count != 0)
                {
                    queue.Dequeue()();
                }
            }

            ThreadPool.QueueUserWorkItem(s => ((CoalescingScheduler)s).DoWork(), this);
        }
    }
}
