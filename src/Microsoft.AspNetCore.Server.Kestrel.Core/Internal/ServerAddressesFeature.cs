using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    internal class ServerAddressesFeature : IServerAddressesFeature
    {
        public ICollection<string> Addresses { get; } = new List<string>();
    }
}
