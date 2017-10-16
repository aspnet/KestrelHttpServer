// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    public struct WritableBufferAwaitable : ICriticalNotifyCompletion
    {
        private readonly IWritableBufferAwaiter _awaiter;

        public WritableBufferAwaitable(IWritableBufferAwaiter awaiter)
        {
            _awaiter = awaiter;
        }

        public bool IsCompleted => _awaiter.IsCompleted;

        public FlushResult GetResult() => _awaiter.GetResult();

        public WritableBufferAwaitable GetAwaiter() => this;

        public void UnsafeOnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);

        public void OnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);
    }
}
