// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNet.Server.Kestrel.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Server.Kestrel
{
    /// <summary>
    /// Summary description for KestrelThread
    /// </summary>
    public class KestrelThread
    {
        KestrelEngine _engine;
        Thread _thread;
        UvLoopHandle _loop;
        UvAsyncHandle _post;
        Queue<Work> _workAdding = new Queue<Work>();
        Queue<Work> _workRunning = new Queue<Work>();
        object _workSync = new Object();
        private ExceptionDispatchInfo _closeError;

        public KestrelThread(KestrelEngine engine)
        {
            _engine = engine;
            _thread = new Thread(ThreadStart);
        }

        public UvLoopHandle Loop { get { return _loop; } }

        public Task StartAsync()
        {
            var tcs = new TaskCompletionSource<int>();
            _thread.Start(tcs);
            return tcs.Task;
        }

        public void Stop(TimeSpan timeout)
        {
            Post(OnStop, null);
            if (!_thread.Join((int)timeout.TotalMilliseconds))
            {
                throw new TimeoutException("Loop did not close");
            }
            if (_closeError != null)
            {
                _closeError.Throw();
            }
        }

        private void OnStop(object obj)
        {
            _post.Unreference();
            _loop.Stop();
        }

        public void Post(Action<object> callback, object state)
        {
            lock (_workSync)
            {
                _workAdding.Enqueue(new Work { Callback = callback, State = state });
            }
            _post.Send();
        }

        public Task PostAsync(Action<object> callback, object state)
        {
            var tcs = new TaskCompletionSource<int>();
            lock (_workSync)
            {
                _workAdding.Enqueue(new Work { Callback = callback, State = state, Completion = tcs });
            }
            _post.Send();
            return tcs.Task;
        }

        private void ThreadStart(object parameter)
        {
            var tcs = (TaskCompletionSource<int>)parameter;
            SetupLoop(tcs);
            RunLoop();
        }

        private void SetupLoop(TaskCompletionSource<int> tcs)
        {
            try
            {
                _loop = new UvLoopHandle();
                _post = new UvAsyncHandle(_loop, OnPost);
                tcs.SetResult(0);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }

        private void RunLoop()
        {
            using (_loop)
            // _post is disposed in the uv_walk callback
            {
                try
                {
                    _post.Reference();
                    _loop.Run();

                    _loop.Validate();
                    UnsafeNativeMethods.uv_walk(
                        _loop,
                        (ptr, arg) =>
                        {
                            var handle = UvMemory.FromIntPtr<UvHandle>(ptr);
                            handle.Dispose();
                        },
                        IntPtr.Zero);
                    _loop.Run();
                }
                catch (Exception ex)
                {
                    _closeError = ExceptionDispatchInfo.Capture(ex);
                }
            }
        }

        private void OnPost()
        {
            Queue<Work> queue;
            lock (_workSync)
            {
                queue = _workAdding;
                _workAdding = _workRunning;
                _workRunning = queue;
            }
            while (queue.Count != 0)
            {
                var work = queue.Dequeue();
                try
                {
                    work.Callback(work.State);
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
                        ThreadPool.QueueUserWorkItem(_ => work.Completion.SetException(ex), null);
                    }
                    else
                    {
                        Trace.WriteLine("KestrelThread.DoPostWork " + ex.ToString());
                    }
                }
            }
        }

        private struct Work
        {
            public Action<object> Callback;
            public object State;
            public TaskCompletionSource<int> Completion;
        }
    }
}
