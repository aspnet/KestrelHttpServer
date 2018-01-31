// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal.Coalescing
{
    public class ThreadBasedCoalescingScheduler : CoalescingScheduler, IDisposable
    {
        private readonly TimeSpan _writeInterval;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _disposeWh = new ManualResetEventSlim();
        private bool _disposing;

        public ThreadBasedCoalescingScheduler(TimeSpan writeInterval, PipeScheduler innerScheduler)
            : base(innerScheduler)
        {
            _writeInterval = writeInterval;

            _thread = new Thread(ThreadStart);
            _thread.Name = nameof(ThreadBasedCoalescingScheduler);
#if !DEBUG
            // Mark the thread as being as unimportant to keeping the process alive.
            // Don't do this for debug builds, so we know if the thread isn't terminating.
            _thread.IsBackground = true;
#endif

            _thread.Start();
        }

        private void ThreadStart()
        {
            while (!_disposing)
            {
                var loopStartTimeStamp = DateTime.UtcNow;
                DoWork();
                var elapsedTime = DateTime.UtcNow - loopStartTimeStamp;

                if (elapsedTime < _writeInterval)
                {
                    Thread.Sleep(_writeInterval - elapsedTime);
                }
            }

            _disposeWh.Set();
        }

        public void Dispose()
        {
            _disposing = true;
            _disposeWh.Wait();
        }
    }
}