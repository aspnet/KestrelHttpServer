// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    public class TaskRunScheduler : IScheduler
    {
        public static TaskRunScheduler Default = new TaskRunScheduler();

        public void Schedule(Action<object> action, object state)
        {
            Task.Factory.StartNew(action, state);
        }
    }
}
