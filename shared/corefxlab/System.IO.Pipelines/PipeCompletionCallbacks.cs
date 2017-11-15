// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines
{
    internal class PipeCompletionCallbacks
    {
        private readonly ArrayPool<PipeCompletionCallback> _pool;
        private readonly int _count;
        private readonly Exception _exception;
        private readonly PipeCompletionCallback[] _callbacks;

        public PipeCompletionCallbacks(ArrayPool<PipeCompletionCallback> pool, int count, Exception exception, PipeCompletionCallback[] callbacks)
        {
            _pool = pool;
            _count = count;
            _exception = exception;
            _callbacks = callbacks;
        }

        public void Execute()
        {
            if (_callbacks == null || _count == 0)
            {
                return;
            }

            try
            {
                List<Exception> exceptions = null;

                for (int i = 0; i < _count; i++)
                {
                    var callback = _callbacks[i];
                    try
                    {
                        callback.Callback(_exception, callback.State);
                    }
                    catch (Exception ex)
                    {
                        if (exceptions == null)
                        {
                            exceptions = new List<Exception>();
                        }
                        exceptions.Add(ex);
                    }
                }

                if (exceptions != null)
                {
                    throw new AggregateException(exceptions);
                }
            }
            finally
            {
                _pool.Return(_callbacks, clearArray: true);
            }
        }
    }
}