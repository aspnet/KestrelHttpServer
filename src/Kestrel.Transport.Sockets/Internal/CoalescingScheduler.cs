// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class CoalescingScheduler
    {
        private static readonly WaitCallback _doWorkCallback = s => ((CoalescingScheduler)s).DoWork();

        private readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();

        private readonly object _workSync = new object();
        private bool _doingWork;

        public void Schedule(Action action)
        {
            _actions.Enqueue(action);

            lock (_workSync)
            {
                if (!_doingWork)
                {
                    ThreadPool.QueueUserWorkItem(_doWorkCallback, this);
                    _doingWork = true;
                }
            }
        }

        private void DoWork()
        {
            while (_actions.TryDequeue(out Action item))
            {
                item();
            }

            lock (_workSync)
            {
                if (!_actions.IsEmpty)
                {
                    ThreadPool.QueueUserWorkItem(_doWorkCallback, this);
                }
                else
                {
                    _doingWork = false;
                }
            }
        }
    }
}
