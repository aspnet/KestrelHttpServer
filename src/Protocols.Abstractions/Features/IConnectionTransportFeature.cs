﻿using System.Buffers;
using System.IO.Pipelines;
using System.Threading;

namespace Microsoft.AspNetCore.Protocols.Features
{
    public interface IConnectionTransportFeature
    {
        MemoryPool MemoryPool { get; }

        IDuplexPipe Transport { get; set; }

        IDuplexPipe Application { get; set; }

        PipeScheduler InputWriterScheduler { get; }

        PipeScheduler OutputReaderScheduler { get; }

        PipeScheduler ApplicationScheduler { get; set; }
    }
}
