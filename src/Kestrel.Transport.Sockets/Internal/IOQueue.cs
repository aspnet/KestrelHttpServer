// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class IOQueue : PipeScheduler
    {
        private static readonly WaitCallback _doWorkCallback = s => ((IOQueue)s).Execute();

        private readonly ConcurrentQueue<Work> _workItems = new ConcurrentQueue<Work>();
        private int _doingWork;

        public override void Schedule(Action<object> action, object state)
        {
            var work = new Work
            {
                Callback = action,
                State = state
            };

            // Order is important here with Execute.
            // Enqueue prior to checking _doingWork.
            _workItems.Enqueue(work);

            Thread.MemoryBarrier(); // Ensure ordering of write + check on net45x

            if (Volatile.Read(ref _doingWork) == 0)
            {
                // Set as working, and check if it was already working.
                var submitWork = Interlocked.Exchange(ref _doingWork, 1) == 0;

                if (submitWork)
                {
                    // Wasn't scheduled or active, schedule.
                    System.Threading.ThreadPool.UnsafeQueueUserWorkItem(_doWorkCallback, this);
                }
            }
        }

        private void Execute()
        {
            while (true)
            {
                var workItems = _workItems;
                while (workItems.TryDequeue(out Work item))
                {
                    item.Callback(item.State);
                }

                // Order is important here with Schedule.
                // Set _doingWork prior to checking .IsEmpty
                Volatile.Write(ref _doingWork, 0);

                Thread.MemoryBarrier(); // Ensure ordering of write + check on net45x

                if (workItems.IsEmpty)
                {
                    // Nothing to do, exit.
                    break;
                }

                // Is work, can we set it as active again, prior to it being scheduled?
                var alreadyScheduled = Interlocked.Exchange(ref _doingWork, 1) == 1;

                if (alreadyScheduled)
                {
                    // Something to do, but DoWork has been rescheduled already, exit.
                    break;
                }

                // Is work, wasn't already scheduled so continue loop
            }
        }

        private struct Work
        {
            public Action<object> Callback;
            public object State;
        }
    }
}
