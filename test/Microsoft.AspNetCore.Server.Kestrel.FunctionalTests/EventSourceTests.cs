﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.FunctionalTests
{
    public class EventSourceTests : IDisposable
    {
        private readonly TestEventListener _listener = new TestEventListener();

        public EventSourceTests()
        {
            _listener.EnableEvents(KestrelEventSource.Log, EventLevel.Verbose);
        }

        [Fact]
        public async Task EmitsConnectionStartAndStop()
        {
            string connectionId = null;
            int port;
            using (var server = new TestServer(context =>
            {
                connectionId = context.Features.Get<IHttpConnectionFeature>().ConnectionId;
                return Task.CompletedTask;
            }))
            {
                port = server.Port;
                using (var connection = server.CreateConnection())
                {
                    await connection.SendAll("GET / HTTP/1.1",
                        "",
                        "")
                        .TimeoutAfter(TimeSpan.FromSeconds(10));
                    await connection.Receive("HTTP/1.1 200");
                }
            }

            // capture list here as other tests executing in parallel may log events
            Assert.NotNull(connectionId);
            var events = _listener.EventData.Where(e => e != null && GetProperty(e, "connectionId") == connectionId).ToList();

            var start = Assert.Single(events, e => e.EventName == "ConnectionStart");
            Assert.All(new[] { "connectionId", "scheme", "remoteEndPoint", "localEndPoint" }, p => Assert.Contains(p, start.PayloadNames));
            Assert.Equal("http", GetProperty(start, "scheme"));
            Assert.Equal($"127.0.0.1:{port}", GetProperty(start, "localEndPoint"));

            var stop = Assert.Single(events, e => e.EventName == "ConnectionStop");
            Assert.All(new[] { "connectionId" }, p => Assert.Contains(p, stop.PayloadNames));
            Assert.Same(KestrelEventSource.Log, stop.EventSource);
        }

        private string GetProperty(EventWrittenEventArgs data, string propName)
            => data.Payload[data.PayloadNames.IndexOf(propName)] as string;

        private class TestEventListener : EventListener
        {
            private volatile bool _disposed;
            private ConcurrentBag<EventWrittenEventArgs> _events = new ConcurrentBag<EventWrittenEventArgs>();

            public IEnumerable<EventWrittenEventArgs> EventData => _events;

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                if (!_disposed)
                {
                    _events.Add(eventData);
                }
            }

            public override void Dispose()
            {
                _disposed = true;
                base.Dispose();
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }
}
