// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal.Coalescing
{
    public class TimerBasedCoalescingScheduler : CoalescingScheduler, IDisposable
    {
        private readonly Timer _timer;
        private int _doingWork;

        public TimerBasedCoalescingScheduler(TimeSpan writeInterval, PipeScheduler innerScheduler)
            : base(innerScheduler)
        {
            _timer = new Timer(state => ((TimerBasedCoalescingScheduler)state).DoWork(),
                state: this, dueTime: TimeSpan.Zero, period: writeInterval);
        }

        private void DoWorkInterlocked()
        {
            if (Interlocked.Exchange(ref _doingWork, 1) == 0)
            {
                DoWork();
                Interlocked.Exchange(ref _doingWork, 0);
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}