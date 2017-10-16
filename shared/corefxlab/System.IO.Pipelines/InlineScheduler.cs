// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    public class InlineScheduler : IScheduler
    {
        public static readonly InlineScheduler Default = new InlineScheduler();

        public void Schedule(Action<object> action, object state)
        {
            action(state);
        }
    }
}
