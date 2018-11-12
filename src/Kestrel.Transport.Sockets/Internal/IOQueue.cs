// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class IOQueue : PipeScheduler
#if NETCOREAPP3_0
        , IThreadPoolWorkItem
#endif
    {
#if !NETCOREAPP3_0
        private static readonly WaitCallback _doWorkCallback = s => ((IOQueue)s).Execute();
#endif

        private readonly object _workSync = new object();
        private readonly ConcurrentQueue<Work> _workItems = new ConcurrentQueue<Work>();
        private bool _isExecuting;

        public override void Schedule(Action<object> action, object state)
        {
            var work = new Work
            {
                Callback = action,
                State = state
            };

            // Order is important here with DoWork.
            // Enqueue prior to checking _doingWork.
            _workItems.Enqueue(work);

            if (!Volatile.Read(ref _isExecuting))
            {
                // Not scheduled or working.
                var submitWork = false;
                // We re-check under lock here to prevent double schedule or missed schedule.
                lock (_workSync)
                {
                    // Don't schedule if already scheduled or doing work.
                    if (!_isExecuting)
                    {
                        submitWork = true;
                        _isExecuting = true;
                    }
                }

                // Wasn't scheduled or active, schedule outside lock.
                if (submitWork)
                {
#if NETCOREAPP3_0
                    System.Threading.ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
#else
                    System.Threading.ThreadPool.UnsafeQueueUserWorkItem(_doWorkCallback, this);
#endif
                }
            }
        }

#if NETCOREAPP3_0
        void IThreadPoolWorkItem.Execute()
#else
        private void Execute()
#endif
        {
            while (true)
            {
                while (_workItems.TryDequeue(out Work item))
                {
                    item.Callback(item.State);
                }

                // Order is important here with Schedule.
                // Set _doingWork prior to checking .IsEmpty
                Volatile.Write(ref _isExecuting, false);

                // We check under lock here to prevent double schedule or missed schedule.
                lock (_workSync)
                {
                    if (_workItems.IsEmpty)
                    {
                        // Nothing to do, exit.
                        break;
                    }

                    if (!_isExecuting)
                    {
                        // Something to do, but DoWork has been rescheduled already, exit.
                        break;
                    }
                    else
                    {
                        // Something to do, and not rescheduled yet, reactivate. 
                        // As we are already running and can do the work.
                        _isExecuting = true;
                    }
                }
            }
        }

        private struct Work
        {
            public Action<object> Callback;
            public object State;
        }
    }
}
