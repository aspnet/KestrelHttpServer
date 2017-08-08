using System;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Protocols.Abstractions
{
    public abstract class ConnectionContext
    {
        public abstract string ConnectionId { get; set; }

        public abstract IFeatureCollection Features { get; }

        public abstract IPipe Transport { get; set; }
    }
}
