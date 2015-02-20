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
        Queue<Action> _workAdding = new Queue<Action>();
        Queue<Action> _workRunning = new Queue<Action>();
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
            Post(OnStop);
            if (!_thread.Join((int)timeout.TotalMilliseconds))
            {
                throw new TimeoutException("Loop did not close");
            }
            if (_closeError != null)
            {
                _closeError.Throw();
            }
        }

        private void OnStop()
        {
            _post.Unreference();
            // In a perfect world at this point, there wouldn't be anything left
            //  that is referenced on the loop …
            var postHandle = _post.Handle;
            _post.Dispose();
            // … so when returning here, the DestroyMemory callback would be
            //  executed in the next loop iteration and the loop would exit naturally


            // However, the world isn't perfect and there are currently ways
            //  that handles are left that would make the loop run forever.
            // Right now from the loop's point of view, _post is still active.
            // So we skip _post when we go through the handles that are still active
            //  and close them manually.
            UnsafeNativeMethods.uv_walk(
                _loop,
                (ptr, arg) =>
                {
                    if (ptr != postHandle)
                        UnsafeNativeMethods.uv_close(ptr, null);
                },
                IntPtr.Zero);
            // This does not Dispose() the handles, so for each one
            //  a nice message is written to the Console by the handle's finalizer

            // Now all references are definitely going to be gone
            //  and the loop will exit after the next iteration
        }

        public void Post(Action callback)
        {
            lock (_workSync)
            {
                _workAdding.Enqueue(callback);
            }
            _post.Send();
        }

        public Task PostAsync(Action callback)
        {
            var tcs = new TaskCompletionSource<int>();
            Post(() =>
            {
                try
                {
                    callback();
                    tcs.SetResult(0);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });
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
            {
                try
                {
                    _post.Reference();
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
            var finishedBatch = finishCurrentBatch();
            foreach (var work in finishedBatch)
            {
                work();
            }
            finishedBatch.Clear();
        }

        private Queue<Action> finishCurrentBatch()
        {
            Queue<Action> queue;
            lock (_workSync)
            {
                queue = _workAdding;
                _workAdding = _workRunning;
                _workRunning = queue;
            }
            return queue;
        }
    }
}
