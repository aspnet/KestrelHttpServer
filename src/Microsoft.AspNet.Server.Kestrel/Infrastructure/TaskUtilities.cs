// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Http;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    public static class TaskUtilities
    {
#if DOTNET5_4 || DNXCORE50
        public static Task CompletedTask = Task.CompletedTask;
#else
        public static Task CompletedTask = Task.FromResult<object>(null);
#endif

        private static WaitCallback _runAction = (o) =>
        {
            try
            {
                ((Action)o)();
            }
            catch (Exception)
            {
                // log with a static logger in some way?
            }
        };

        private static WaitCallback _completeTcs = (o) =>
        {
            try
            {
                ((TaskCompletionSource<object>)o).SetResult(null);
            }
            catch (Exception)
            {
                // log with a static logger in some way?
            }
        };

        private static WaitCallback _abortFrame = (o) =>
        {
            try
            {
                ((Frame)o).Abort();
            }
            catch (Exception)
            {
                // log with a static logger in some way?
            }
        };

        public static void RunOnThreadPool(Action action)
        {
            ThreadPool.QueueUserWorkItem(_runAction, action);
        }

        public static void CompleteOnThreadPool(TaskCompletionSource<object> tcs)
        {
            ThreadPool.QueueUserWorkItem(_completeTcs, tcs);
        }

        public static void ErrorOnThreadPool(TaskCompletionSource<object> tcs, Exception ex)
        {
            // ex is closure captured 
            ThreadPool.QueueUserWorkItem((o) =>
            {
                try
                {
                    ((TaskCompletionSource<object>)o).SetException(ex);
                }
                catch (Exception)
                {
                    // log with a static logger in some way?
                }
            }, tcs);
        }

        public static void AbortOnThreadPool(Frame frame)
        {
            ThreadPool.QueueUserWorkItem(_abortFrame, frame);
        }
    }
}