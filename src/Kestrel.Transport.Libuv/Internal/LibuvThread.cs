// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal
{
    public class LibuvThread : PipeScheduler
    {
        // maximum times the work queues swapped and are processed in a single pass
        // as completing a task may immediately have write data to put on the network
        // otherwise it needs to wait till the next pass of the libuv loop
        private readonly int _maxWorkBatch = 1024 * 8; // previous was queue size 1024 * 8 loops == 8192 work items
        private readonly int _maxCloseHandleBatch = 256 * 8; // previous was queue size 1024 * 8 loops == 2048 work items

        private readonly LibuvTransport _transport;
        private readonly IApplicationLifetime _appLifetime;
        private readonly Thread _thread;
        private readonly TaskCompletionSource<VoidResult> _threadTcs = new TaskCompletionSource<VoidResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly UvLoopHandle _loop;
        private readonly UvAsyncHandle _post;

        private CachelinePaddedInt _workAlerted;
        private ConcurrentQueue<Work> _work = new ConcurrentQueue<Work>();
        private CachelinePaddedInt _closeHandleAlerted;
        private ConcurrentQueue<CloseHandle> _closeHandles = new ConcurrentQueue<CloseHandle>();
        
        private readonly object _startSync = new object();
        private bool _stopImmediate = false;
        private bool _initCompleted = false;
        private ExceptionDispatchInfo _closeError;
        private readonly ILibuvTrace _log;

        public LibuvThread(LibuvTransport transport)
        {
            _transport = transport;
            _appLifetime = transport.AppLifetime;
            _log = transport.Log;
            _loop = new UvLoopHandle(_log);
            _post = new UvAsyncHandle(_log);
            _thread = new Thread(ThreadStart);
            _thread.Name = nameof(LibuvThread);
#if !DEBUG
            // Mark the thread as being as unimportant to keeping the process alive.
            // Don't do this for debug builds, so we know if the thread isn't terminating.
            _thread.IsBackground = true;
#endif
            QueueCloseHandle = PostCloseHandle;
            QueueCloseAsyncHandle = EnqueueCloseHandle;
            MemoryPool = new MemoryPool();
            WriteReqPool = new WriteReqPool(this, _log);
        }

        public UvLoopHandle Loop { get { return _loop; } }

        public MemoryPool MemoryPool { get; }

        public WriteReqPool WriteReqPool { get; }

#if DEBUG
        public List<WeakReference> Requests { get; } = new List<WeakReference>();
#endif

        public ExceptionDispatchInfo FatalError { get { return _closeError; } }

        public Action<Action<IntPtr>, IntPtr> QueueCloseHandle { get; }

        private Action<Action<IntPtr>, IntPtr> QueueCloseAsyncHandle { get; }

        public Task StartAsync()
        {
            var tcs = new TaskCompletionSource<VoidResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread.Start(tcs);
            return tcs.Task;
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            lock (_startSync)
            {
                if (!_initCompleted)
                {
                    return;
                }
            }

            Debug.Assert(!_threadTcs.Task.IsCompleted, "The loop thread was completed before calling uv_unref on the post handle.");

            var stepTimeout = TimeSpan.FromTicks(timeout.Ticks / 3);

            try
            {
                Post(t => t.AllowStop());
                if (!await WaitAsync(_threadTcs.Task, stepTimeout).ConfigureAwait(false))
                {
                    Post(t => t.OnStopRude());
                    if (!await WaitAsync(_threadTcs.Task, stepTimeout).ConfigureAwait(false))
                    {
                        Post(t => t.OnStopImmediate());
                        if (!await WaitAsync(_threadTcs.Task, stepTimeout).ConfigureAwait(false))
                        {
                            _log.LogCritical($"{nameof(LibuvThread)}.{nameof(StopAsync)} failed to terminate libuv thread.");
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                if (!await WaitAsync(_threadTcs.Task, stepTimeout).ConfigureAwait(false))
                {
                    _log.LogCritical($"{nameof(LibuvThread)}.{nameof(StopAsync)} failed to terminate libuv thread.");
                }
            }

            _closeError?.Throw();
        }

#if DEBUG
        private void CheckUvReqLeaks()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Detect leaks in UvRequest objects
            foreach (var request in Requests)
            {
                Debug.Assert(request.Target == null, $"{request.Target?.GetType()} object is still alive.");
            }
        }
#endif

        private void AllowStop()
        {
            _post.Unreference();
        }

        private void OnStopRude()
        {
            Walk(ptr =>
            {
                var handle = UvMemory.FromIntPtr<UvHandle>(ptr);
                if (handle != _post)
                {
                    // handle can be null because UvMemory.FromIntPtr looks up a weak reference
                    handle?.Dispose();
                }
            });
        }

        private void OnStopImmediate()
        {
            _stopImmediate = true;
            _loop.Stop();
        }

        public void Post<T>(Action<T> callback, T state)
        {
            var work = new Work
            {
                CallbackAdapter = CallbackAdapter<T>.PostCallbackAdapter,
                Callback = callback,
                State = state
            };

            _work.Enqueue(work);

            NofityWork();
        }

        private void Post(Action<LibuvThread> callback)
        {
            Post(callback, this);
        }

        public Task PostAsync<T>(Action<T> callback, T state)
        {
            var tcs = new TaskCompletionSource<VoidResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = new Work
            {
                CallbackAdapter = CallbackAdapter<T>.PostAsyncCallbackAdapter,
                Callback = callback,
                State = state,
                Completion = tcs
            };

            _work.Enqueue(work);

            NofityWork();
            return tcs.Task;
        }

        private void NofityWork()
        {
            if (Interlocked.Exchange(ref _workAlerted.Value, 1) == 0)
            {
                _post.Send();
            }
        }

        public void Walk(Action<IntPtr> callback)
        {
            Walk((ptr, arg) => callback(ptr), IntPtr.Zero);
        }

        private void Walk(LibuvFunctions.uv_walk_cb callback, IntPtr arg)
        {
            _transport.Libuv.walk(
                _loop,
                callback,
                arg
                );
        }

        private void PostCloseHandle(Action<IntPtr> callback, IntPtr handle)
        {
            EnqueueCloseHandle(callback, handle);

            NofityCloseHandle();

            void NofityCloseHandle()
            {
                if (Interlocked.Exchange(ref _closeHandleAlerted.Value, 1) == 0 && 
                    _workAlerted.Value == 0)
                {
                    _post.Send();
                }
            }
        }

        private void EnqueueCloseHandle(Action<IntPtr> callback, IntPtr handle)
        {
            var closeHandle = new CloseHandle { Callback = callback, Handle = handle };
            _closeHandles.Enqueue(closeHandle);
        }

        private void ThreadStart(object parameter)
        {
            lock (_startSync)
            {
                var tcs = (TaskCompletionSource<VoidResult>)parameter;
                try
                {
                    _loop.Init(_transport.Libuv);
                    _post.Init(_loop, OnPost, EnqueueCloseHandle);
                    _initCompleted = true;
                    tcs.SetResult(default);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    return;
                }
            }

            try
            {
                _loop.Run();
                if (_stopImmediate)
                {
                    // thread-abort form of exit, resources will be leaked
                    return;
                }

                // run the loop one more time to delete the open handles
                _post.Reference();
                _post.Dispose();

                // We need this walk because we call ReadStop on on accepted connections when there's back pressure
                // Calling ReadStop makes the handle as in-active which means the loop can
                // end while there's still valid handles around. This makes loop.Dispose throw
                // with an EBUSY. To avoid that, we walk all of the handles and dispose them.
                Walk(ptr =>
                {
                    var handle = UvMemory.FromIntPtr<UvHandle>(ptr);
                    // handle can be null because UvMemory.FromIntPtr looks up a weak reference
                    handle?.Dispose();
                });

                // Ensure the Dispose operations complete in the event loop.
                _loop.Run();

                _loop.Dispose();
            }
            catch (Exception ex)
            {
                _closeError = ExceptionDispatchInfo.Capture(ex);
                // Request shutdown so we can rethrow this exception
                // in Stop which should be observable.
                _appLifetime.StopApplication();
            }
            finally
            {
                MemoryPool.Dispose();
                WriteReqPool.Dispose();
                _threadTcs.SetResult(default);

#if DEBUG
                // Check for handle leaks after disposing everything
                CheckUvReqLeaks();
#endif
            }
        }

        private void OnPost()
        {
            DoPostWork(maxItems: _maxWorkBatch);

            DoPostCloseHandle(maxItems: _maxCloseHandleBatch);
        }

        private void DoPostWork(int maxItems)
        {
            var i = 0;
            var queue = _work;
            bool shouldLoop;
            do
            {
                i++;
                if (queue.TryDequeue(out var work))
                {
                    var completion = work.Completion;
                    try
                    {
                        work.CallbackAdapter(work.Callback, work.State);
                        completion?.TrySetResult(default);
                    }
                    catch (Exception ex)
                    {
                        HandleWorkException(completion, ex);
                    }

                    shouldLoop = i < maxItems;
                }
                else
                {
                    shouldLoop = false;
                }

                if (!shouldLoop && i <= maxItems && Volatile.Read(ref _workAlerted.Value) == 1)
                {
                    Volatile.Write(ref _workAlerted.Value, 0);
                    // Loop once more after changing the alert value; to catch any added items in activation race
                    shouldLoop = true;
                }
            } while (shouldLoop);

            void HandleWorkException(TaskCompletionSource<VoidResult> completion, Exception ex)
            {
                if (completion != null)
                {
                    completion.TrySetException(ex);
                }
                else
                {
                    _log.LogError(0, ex, $"{nameof(LibuvThread)}.{nameof(DoPostWork)}");
                    ExceptionDispatchInfo.Capture(ex).Throw();
                }
            }
        }

        private void DoPostCloseHandle(int maxItems)
        {
            var i = 0;
            var queue = _closeHandles;
            bool shouldLoop;
            do
            {
                i++;
                if (queue.TryDequeue(out var closeHandle))
                {
                    try
                    {
                        closeHandle.Callback(closeHandle.Handle);
                    }
                    catch (Exception ex)
                    {
                        HandleCloseHandleException(ex);
                    }

                    shouldLoop = i < maxItems;
                }
                else
                {
                    shouldLoop = false;
                }

                if (!shouldLoop && i <= maxItems && Volatile.Read(ref _closeHandleAlerted.Value) == 1)
                {
                    Volatile.Write(ref _closeHandleAlerted.Value, 0);
                    // Loop once more after changing the alert value; to catch any added items in activation race
                    shouldLoop = true;
                }
            }
            while (shouldLoop);

            void HandleCloseHandleException(Exception ex)
            {
                _log.LogError(0, ex, $"{nameof(LibuvThread)}.{nameof(DoPostCloseHandle)}");
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
        }

        private static async Task<bool> WaitAsync(Task task, TimeSpan timeout)
        {
            return await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) == task;
        }

        public override void Schedule(Action action)
        {
            Post(state => state(), action);
        }

        public override void Schedule(Action<object> action, object state)
        {
            Post(action, state);
        }

        private struct Work
        {
            public Action<object, object> CallbackAdapter;
            public object Callback;
            public object State;
            public TaskCompletionSource<VoidResult> Completion;
        }

        private struct CloseHandle
        {
            public Action<IntPtr> Callback;
            public IntPtr Handle;
        }

        private class CallbackAdapter<T>
        {
            public static readonly Action<object, object> PostCallbackAdapter = (callback, state) => ((Action<T>)callback).Invoke((T)state);
            public static readonly Action<object, object> PostAsyncCallbackAdapter = (callback, state) => ((Action<T>)callback).Invoke((T)state);
        }

        private readonly struct VoidResult { }

        [StructLayout(LayoutKind.Explicit, Size = 128)]
        private struct CachelinePaddedInt
        {
            [FieldOffset(offset: 64)]
            public int Value;
        }
    }
}
