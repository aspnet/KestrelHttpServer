// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public class HeartbeatManager : IHeartbeatHandler
    {
        private static long _nextId;

        private readonly ConcurrentDictionary<long, HeartbeatHandlerReference> _handlerReferences = new ConcurrentDictionary<long, HeartbeatHandlerReference>();
        private readonly IKestrelTrace _trace;

        public HeartbeatManager(IKestrelTrace trace)
        {
            _trace = trace;
        }

        public long AddHandler(IHeartbeatHandler handler)
        {
            var id = Interlocked.Increment(ref _nextId);

            if (!_handlerReferences.TryAdd(id, new HeartbeatHandlerReference(handler)))
            {
                throw new InvalidOperationException($"Duplicate heartbeat handler ID: {id}");
            }

            return id;
        }

        public void RemoveHandler(long id)
        {
            if (!_handlerReferences.TryRemove(id, out _))
            {
                throw new ArgumentException(nameof(id));
            }
        }

        public void OnHeartbeat(DateTimeOffset now)
        {
            try
            {
                foreach (var kvp in _handlerReferences)
                {
                    var reference = kvp.Value;

                    if (reference.TryGetHandler(out var handler))
                    {
                        handler.OnHeartbeat(now);
                    }
                    else
                    {
                        // It's safe to modify the ConcurrentDictionary in the foreach.
                        // The handler reference has become unrooted because the application never completed.
                        _handlerReferences.TryRemove(kvp.Key, out reference);
                    }
                }
            }
            catch (Exception ex)
            {
                _trace.LogError(0, ex, $"{nameof(HeartbeatManager)}.{nameof(OnHeartbeat)}");
            }
        }
    }
}
