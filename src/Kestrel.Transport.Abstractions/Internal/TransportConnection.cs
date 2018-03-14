﻿using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal
{
    public abstract partial class TransportConnection
    {
        private IDictionary<object, object> _metadata;

        public TransportConnection()
        {
            _currentIConnectionIdFeature = this;
            _currentIConnectionTransportFeature = this;
            _currentIHttpConnectionFeature = this;
            _currentIConnectionMetadataFeature = this;
            _currentIApplicationTransportFeature = this;
            _currentIMemoryPoolFeature = this;
            _currentITransportSchedulerFeature = this;
        }

        public IPAddress RemoteAddress { get; set; }
        public int RemotePort { get; set; }
        public IPAddress LocalAddress { get; set; }
        public int LocalPort { get; set; }

        public string ConnectionId { get; set; }

        public virtual MemoryPool<byte> MemoryPool { get; }
        public virtual PipeScheduler InputWriterScheduler { get; }
        public virtual PipeScheduler OutputReaderScheduler { get; }

        public IDuplexPipe Transport { get; set; }
        public IDuplexPipe Application { get; set; }

        public IDictionary<object, object> Metadata
        {
            get
            {
                // Lazily allocate connection metadata
                return _metadata ?? (_metadata = new ConnectionMetadata());
            }
            set
            {
                _metadata = value;
            }
        }

        public PipeWriter Input => Application.Output;
        public PipeReader Output => Application.Input;
    }
}
