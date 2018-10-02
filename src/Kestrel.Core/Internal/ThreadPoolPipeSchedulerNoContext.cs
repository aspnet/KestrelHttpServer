using System;
using System.IO.Pipelines;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    /// <summary>
    /// A PipeScheduler that uses the thread pool but doesn't capture the execution context
    /// </summary>
    internal sealed class ThreadPoolPipeSchedulerNoContext : PipeScheduler
    {
        public static readonly ThreadPoolPipeSchedulerNoContext Instance = new ThreadPoolPipeSchedulerNoContext();

        public override void Schedule(Action<object> action, object state)
        {
#if NETCOREAPP2_1
            using (ExecutionContext.SuppressFlow())
            {
                System.Threading.ThreadPool.QueueUserWorkItem(action, state, preferLocal: false);
            }
#else
            System.Threading.ThreadPool.UnsafeQueueUserWorkItem(s =>
            {
                var tuple = (Tuple<Action<object>, object>)s;
                tuple.Item1(tuple.Item2);
            },
            Tuple.Create(action, state));
#endif

        }
    }
}
