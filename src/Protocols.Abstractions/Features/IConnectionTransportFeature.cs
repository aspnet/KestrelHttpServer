using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;

namespace Microsoft.AspNetCore.Protocols.Abstractions.Features
{
    public interface IConnectionTransportFeature
    {
        PipeFactory PipeFactory { get; set; }

        IPipe Transport { get; set; }
    }
}
