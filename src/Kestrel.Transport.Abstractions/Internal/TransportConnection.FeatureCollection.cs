// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal
{
    public partial class TransportConnection : IFeatureCollection,
                                               IHttpConnectionFeature,
                                               IConnectionIdFeature,
                                               IConnectionTransportFeature,
                                               IConnectionItemsFeature,
                                               IMemoryPoolFeature,
                                               IApplicationTransportFeature,
                                               ITransportSchedulerFeature,
                                               IConnectionLifetimeFeature,
                                               IBytesWrittenFeature
    {
        // NOTE: When feature interfaces are added to or removed from this TransportConnection class implementation,
        // then the list of `features` in the generated code project MUST also be updated.
        // See also: tools/CodeGenerator/TransportConnectionFeatureCollection.cs

        bool IFeatureCollection.IsReadOnly => false;

        int IFeatureCollection.Revision => _featureRevision;

        object IFeatureCollection.this[Type key]
        {
            get
            {
                return this[key];
            }
            set
            {
                this[key] = value;
            }
        }

        TFeature IFeatureCollection.Get<TFeature>()
        {
            return Get<TFeature>();
        }

        void IFeatureCollection.Set<TFeature>(TFeature feature)
        {
            Set(feature);
        }

        string IHttpConnectionFeature.ConnectionId
        {
            get => ConnectionId;
            set => ConnectionId = value;
        }

        IPAddress IHttpConnectionFeature.RemoteIpAddress
        {
            get => RemoteAddress;
            set => RemoteAddress = value;
        }

        IPAddress IHttpConnectionFeature.LocalIpAddress
        {
            get => LocalAddress;
            set => LocalAddress = value;
        }

        int IHttpConnectionFeature.RemotePort
        {
            get => RemotePort;
            set => RemotePort = value;
        }

        int IHttpConnectionFeature.LocalPort
        {
            get => LocalPort;
            set => LocalPort = value;
        }

        MemoryPool<byte> IMemoryPoolFeature.MemoryPool => MemoryPool;

        IDuplexPipe IConnectionTransportFeature.Transport
        {
            get => Transport;
            set => Transport = value;
        }

        IDuplexPipe IApplicationTransportFeature.Application
        {
            get => Application;
            set => Application = value;
        }

        IDictionary<object, object> IConnectionItemsFeature.Items
        {
            get => Items;
            set => Items = value;
        }

        PipeScheduler ITransportSchedulerFeature.InputWriterScheduler => InputWriterScheduler;
        PipeScheduler ITransportSchedulerFeature.OutputReaderScheduler => OutputReaderScheduler;

        IEnumerator<KeyValuePair<Type, object>> IEnumerable<KeyValuePair<Type, object>>.GetEnumerator() => FastEnumerable().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => FastEnumerable().GetEnumerator();

        CancellationToken IConnectionLifetimeFeature.ConnectionClosed
        {
            get => ConnectionClosed;
            set => ConnectionClosed = value;
        }

        void IConnectionLifetimeFeature.Abort() => Abort();

        long IBytesWrittenFeature.TotalBytesWritten => TotalBytesWritten;
    }
}
