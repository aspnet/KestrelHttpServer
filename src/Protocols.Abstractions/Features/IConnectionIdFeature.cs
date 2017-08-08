using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.Protocols.Abstractions.Features
{
    public interface IConnectionIdFeature
    {
        string ConnectionId { get; set; }
    }
}
