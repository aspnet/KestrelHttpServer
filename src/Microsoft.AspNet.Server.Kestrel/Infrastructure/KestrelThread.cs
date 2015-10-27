// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;
using Microsoft.AspNet.Server.Kestrel.Networking;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Microsoft.AspNet.Server.Kestrel
{
    /// <summary>
    /// Summary description for KestrelThread
    /// </summary>
    public class KestrelThread
    {
        private const int _maxPooledWriteRequests = 64;

        private static Action<object, object> _objectCallbackAdapter = (callback, state) => ((Action<object>)callback).Invoke(state);
        private KestrelEngine _engine;
        private readonly IApplicationLifetime _appLifetime;
        private Thread _thread;
        private UvLoopHandle _loop;
        private UvAsyncHandle _post;
        private ConcurrentQueue<Work> _workCurrent = new ConcurrentQueue<Work>();
        private ConcurrentQueue<CloseHandle> _closeHandlesCurrent = new ConcurrentQueue<CloseHandle>();
        private ConcurrentQueue<UvWriteReq> _writeRequestPool = new ConcurrentQueue<UvWriteReq>();
        private bool _stopImmediate = false;
        private bool _initCompleted = false;
        private ExceptionDispatchInfo _closeError;
        private IKestrelTrace _log;

        public KestrelThread(KestrelEngine engine)
        {
            _engine = engine;
            _appLifetime = engine.AppLifetime;
            _log = engine.Log;
            _loop = new UvLoopHandle(_log);
            _post = new UvAsyncHandle(_log);
            _thread = new Thread(ThreadStart);
            QueueCloseHandle = PostCloseHandle;
        }

        public UvLoopHandle Loop { get { return _loop; } }

        public ExceptionDispatchInfo FatalError { get { return _closeError; } }

        public Action<Action<IntPtr>, IntPtr> QueueCloseHandle { get; internal set; }

        public Task StartAsync()
        {
            var tcs = new TaskCompletionSource<int>();
            _thread.Start(tcs);
            return tcs.Task;
        }

        public void Stop(TimeSpan timeout)
        {
            if (!_initCompleted)
            {
                return;
            }

            Post(OnStop, null);
            if (!_thread.Join((int)timeout.TotalMilliseconds))
            {
                Post(OnStopRude, null);
                if (!_thread.Join((int)timeout.TotalMilliseconds))
                {
                    Post(OnStopImmediate, null);
                    if (!_thread.Join((int)timeout.TotalMilliseconds))
                    {
#if NET451
                        _thread.Abort();
#endif
                    }
                }
            }
            if (_closeError != null)
            {
                _closeError.Throw();
            }
        }

        private void OnStop(object obj)
        {
            if (_writeRequestPool != null)
            {
                var writeRequests = _writeRequestPool;
                _writeRequestPool = null;

                UvWriteReq writeReq;
                while (writeRequests.TryDequeue(out writeReq))
                {
                    writeReq.Dispose();
                }
            }
            _post.Unreference();
        }

        private void OnStopRude(object obj)
        {
            _engine.Libuv.walk(
                _loop,
                (ptr, arg) =>
                {
                    var handle = UvMemory.FromIntPtr<UvHandle>(ptr);
                    if (handle != _post)
                    {
                        handle.Dispose();
                    }
                },
                IntPtr.Zero);
        }

        private void OnStopImmediate(object obj)
        {
            _stopImmediate = true;
            _loop.Stop();
        }

        public void Post(Action<object> callback, object state)
        {
            _workCurrent.Enqueue(new Work { CallbackAdapter = _objectCallbackAdapter, Callback = callback, State = state });

            _post.Send();
        }

        public void Post<T>(Action<T> callback, T state)
        {

            _workCurrent.Enqueue(new Work
            {
                CallbackAdapter = (callback2, state2) => ((Action<T>)callback2).Invoke((T)state2),
                Callback = callback,
                State = state
            });

            _post.Send();
        }

        public Task PostAsync(Action<object> callback, object state)
        {
            var tcs = new TaskCompletionSource<int>();

            _workCurrent.Enqueue(new Work
            {
                CallbackAdapter = _objectCallbackAdapter,
                Callback = callback,
                State = state,
                Completion = tcs
            });

            _post.Send();
            return tcs.Task;
        }

        public Task PostAsync<T>(Action<T> callback, T state)
        {
            var tcs = new TaskCompletionSource<int>();

            _workCurrent.Enqueue(new Work
            {
                CallbackAdapter = (state1, state2) => ((Action<T>)state1).Invoke((T)state2),
                Callback = callback,
                State = state,
                Completion = tcs
            });

            _post.Send();
            return tcs.Task;
        }

        public void Send(Action<object> callback, object state)
        {
            if (_loop.ThreadId == Thread.CurrentThread.ManagedThreadId)
            {
                callback.Invoke(state);
            }
            else
            {
                PostAsync(callback, state).Wait();
            }
        }

        private void PostCloseHandle(Action<IntPtr> callback, IntPtr handle)
        {
            _closeHandlesCurrent.Enqueue(new CloseHandle { Callback = callback, Handle = handle });
            
            _post.Send();
        }

        private void ThreadStart(object parameter)
        {
            var tcs = (TaskCompletionSource<int>)parameter;
            try
            {
                _loop.Init(_engine.Libuv);
                _post.Init(_loop, OnPost);
                tcs.SetResult(0);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
                return;
            }

            _initCompleted = true;

            try
            {
                var ran1 = _loop.Run();
                if (_stopImmediate)
                {
                    // thread-abort form of exit, resources will be leaked
                    return;
                }

                // run the loop one more time to delete the open handles
                _post.Reference();
                _post.DangerousClose();

                _engine.Libuv.walk(
                    _loop,
                    (ptr, arg) =>
                    {
                        var handle = UvMemory.FromIntPtr<UvHandle>(ptr);
                        if (handle != _post)
                        {
                            handle.Dispose();
                        }
                    },
                    IntPtr.Zero);

                // Ensure the "DangerousClose" operation completes in the event loop.
                var ran2 = _loop.Run();

                _loop.Dispose();
            }
            catch (Exception ex)
            {
                _closeError = ExceptionDispatchInfo.Capture(ex);
                // Request shutdown so we can rethrow this exception
                // in Stop which should be observable.
                _appLifetime.StopApplication();
            }
        }

        private void OnPost()
        {
            DoPostWork();
            DoPostCloseHandle();
        }

        private void DoPostWork()
        {
            Work work;
            while (_workCurrent.TryDequeue(out work))
            {
                try
                {
                    work.CallbackAdapter(work.Callback, work.State);
                    if (work.Completion != null)
                    {
                        ThreadPool.QueueUserWorkItem(
                            tcs =>
                            {
                                ((TaskCompletionSource<int>)tcs).SetResult(0);
                            },
                            work.Completion);
                    }
                }
                catch (Exception ex)
                {
                    if (work.Completion != null)
                    {
                        ThreadPool.QueueUserWorkItem(tcs => ((TaskCompletionSource<int>)tcs).SetException(ex), work.Completion);
                    }
                    else
                    {
                        _log.LogError("KestrelThread.DoPostWork", ex);
                        throw;
                    }
                }
            }
        }
        private void DoPostCloseHandle()
        {
            CloseHandle closeHandle;
            while (_closeHandlesCurrent.TryDequeue(out closeHandle))
            {
                try
                {
                    closeHandle.Callback(closeHandle.Handle);
                }
                catch (Exception ex)
                {
                    _log.LogError("KestrelThread.DoPostCloseHandle", ex);
                    throw;
                }
            }
        }

        public UvWriteReq LeaseWriteRequest()
        {
            UvWriteReq writeReq;

            var writeRequests = _writeRequestPool;
            if (writeRequests == null || !writeRequests.TryDequeue(out writeReq))
            {
                writeReq = new UvWriteReq(_log);
                writeReq.Init(_loop);
            }

            return writeReq;
        }

        public void ReturnWriteRequest(UvWriteReq writeReq)
        {
            if ((_writeRequestPool?.Count ?? _maxPooledWriteRequests) < _maxPooledWriteRequests)
            {
                writeReq.Reset();
                _writeRequestPool.Enqueue(writeReq);
            }
            else
            {
                writeReq.Dispose();
            }
        }

        private struct Work
        {
            public Action<object, object> CallbackAdapter;
            public object Callback;
            public object State;
            public TaskCompletionSource<int> Completion;
        }
        private struct CloseHandle
        {
            public Action<IntPtr> Callback;
            public IntPtr Handle;
        }
    }
}
