﻿using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal
{
    public abstract partial class TransportConnection
    {
        public TransportConnection()
        {
            _currentIConnectionIdFeature = this;
            _currentIConnectionTransportFeature = this;
            _currentIHttpConnectionFeature = this;
        }

        public IPAddress RemoteAddress { get; set; }
        public int RemotePort { get; set; }
        public IPAddress LocalAddress { get; set; }
        public int LocalPort { get; set; }

        public string ConnectionId { get; set; }

        public virtual MemoryPool MemoryPool { get; }
        public virtual PipeScheduler InputWriterScheduler { get; }
        public virtual PipeScheduler OutputReaderScheduler { get; }
        // REVIEW: Should we make all properties non-virtual with get and set?
        public PipeScheduler ApplicationScheduler { get; set; }

        public IDuplexPipe Transport { get; set; }
        public IDuplexPipe Application { get; set; }

        public PipeWriter Input => Application.Output;
        public PipeReader Output => Application.Input;
    }
}
