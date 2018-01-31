// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal.Coalescing
{
    public class TimerBasedCoalescingScheduler : CoalescingScheduler, IDisposable
    {
        private readonly Timer _timer;

        public TimerBasedCoalescingScheduler(TimeSpan writeInterval, PipeScheduler innerScheduler)
            : base(innerScheduler)
        {
            _timer = new Timer(state => ((TimerBasedCoalescingScheduler)state).DoWork(),
                state: this, dueTime: TimeSpan.Zero, period: writeInterval);
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}