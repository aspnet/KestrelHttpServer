using System;
using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Protocols.Features
{
    public interface IConnectionApplicationFeature
    {
        IPipeConnection Connection { get; set; }
    }
}
