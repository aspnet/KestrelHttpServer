using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure
{
    public class WritableBufferFlusher
    {
        // https://github.com/dotnet/corefxlab/issues/1334 
        // Pipelines don't support multiple awaiters on flush
        // this is temporary until it does
        private TaskCompletionSource<object> _flushTcs;
        private readonly object _flushLock = new object();
        private readonly Action _onFlushCallback;

        public WritableBufferFlusher()
        {
            _onFlushCallback = OnFlush;
        }

        public Task FlushAsync(WritableBuffer writableBuffer)
        {
            var awaitable = writableBuffer.FlushAsync();
            if (awaitable.IsCompleted)
            {
                // The flush task can't fail today
                return TaskCache.CompletedTask;
            }
            return FlushAsyncAwaited(awaitable);
        }

        private Task FlushAsyncAwaited(WritableBufferAwaitable awaitable)
        {
            // https://github.com/dotnet/corefxlab/issues/1334 
            // Since the flush awaitable doesn't currently support multiple awaiters
            // we need to use a task to track the callbacks.
            // All awaiters get the same task
            lock (_flushLock)
            {
                if (_flushTcs == null || _flushTcs.Task.IsCompleted)
                {
                    _flushTcs = new TaskCompletionSource<object>();

                    awaitable.OnCompleted(_onFlushCallback);
                }
            }

            return _flushTcs.Task;
        }

        private void OnFlush()
        {
            _flushTcs.TrySetResult(null);
        }
    }
}
