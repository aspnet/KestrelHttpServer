﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal
{
    public abstract partial class TransportConnection : ConnectionContext
    {
        private IDictionary<object, object> _items;
        private List<(Action<DateTimeOffset, object> handler, object state)> _heartbeatHandlers;
        private readonly object _heartbeatLock = new object();
        private CancellationTokenSource _gracefulCts = new CancellationTokenSource();

        public TransportConnection()
        {
            FastReset();

            ConnectionClosingGracefully = _gracefulCts.Token;
        }

        public IPAddress RemoteAddress { get; set; }
        public int RemotePort { get; set; }
        public IPAddress LocalAddress { get; set; }
        public int LocalPort { get; set; }

        public override string ConnectionId { get; set; }

        public override IFeatureCollection Features => this;

        public virtual MemoryPool<byte> MemoryPool { get; }
        public virtual PipeScheduler InputWriterScheduler { get; }
        public virtual PipeScheduler OutputReaderScheduler { get; }
        public virtual long TotalBytesWritten { get; }

        public override IDuplexPipe Transport { get; set; }
        public IDuplexPipe Application { get; set; }

        public override IDictionary<object, object> Items
        {
            get
            {
                // Lazily allocate connection metadata
                return _items ?? (_items = new ConnectionItems());
            }
            set
            {
                _items = value;
            }
        }

        public PipeWriter Input => Application.Output;
        public PipeReader Output => Application.Input;

        public CancellationToken ConnectionClosed { get; set; }

        public CancellationToken ConnectionClosingGracefully { get; set; }

        public void TickHeartbeat(in DateTimeOffset now)
        {
            lock (_heartbeatLock)
            {
                if (_heartbeatHandlers == null)
                {
                    return;
                }

                foreach (var (handler, state) in _heartbeatHandlers)
                {
                    handler(now, state);
                }
            }
        }

        public void OnHeartbeat(Action<object> action, object state)
        {
            // REVIEW: We could avoid this allocation with 2 lists
            Action<DateTimeOffset, object> handler = (now, state2) => action(state2);
            OnHeartbeat(handler, state);
        }

        public void OnHeartbeat(Action<DateTimeOffset, object> action, object state)
        {
            lock (_heartbeatLock)
            {
                if (_heartbeatHandlers == null)
                {
                    _heartbeatHandlers = new List<(Action<DateTimeOffset, object> handler, object state)>();
                }

                _heartbeatHandlers.Add((action, state));
            }
        }

        // DO NOT remove this override to ConnectionContext.Abort. Doing so would cause
        // any TransportConnection that does not override Abort or calls base.Abort
        // to stack overflow when IConnectionLifetimeFeature.Abort() is called.
        // That said, all derived types should override this method should override
        // this implementation of Abort because canceling pending output reads is not
        // sufficient to abort the connection if there is backpressure.
        public override void Abort(ConnectionAbortedException abortReason)
        {
            Output.CancelPendingRead();
        }

        public void CloseGracefully() => _gracefulCts.Cancel();
    }
}
