﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public class FrameConnectionManager
    {
        // Internal for testing
        internal readonly ConcurrentDictionary<long, FrameConnectionReference> _connectionReferences = new ConcurrentDictionary<long, FrameConnectionReference>();
        private readonly IKestrelTrace _trace;

        public FrameConnectionManager(IKestrelTrace trace)
        {
            _trace = trace;
        }

        public void AddConnection(long id, FrameConnection connection)
        {
            if (!_connectionReferences.TryAdd(id, new FrameConnectionReference(connection)))
            {
                throw new ArgumentException(nameof(id));
            }
        }

        public void RemoveConnection(long id)
        {
            if (!_connectionReferences.TryRemove(id, out var reference))
            {
                throw new ArgumentException(nameof(id));
            }

            reference.Dispose();
        }

        public void Walk(Action<FrameConnection> callback)
        {
            foreach (var kvp in _connectionReferences)
            {
                var reference = kvp.Value;
                var connection = reference.Connection;

                if (connection != null)
                {
                    callback(connection);
                }
                else if (_connectionReferences.TryRemove(kvp.Key, out reference))
                {
                    // It's safe to modify the ConcurrentDictionary in the foreach.
                    // The connection reference has become unrooted because the application never completed.
                    _trace.ApplicationNeverCompleted(reference.ConnectionId);
                    reference.Dispose();
                }

                // If both conditions are false, the connection was removed during the heartbeat.
            }
        }
    }
}
