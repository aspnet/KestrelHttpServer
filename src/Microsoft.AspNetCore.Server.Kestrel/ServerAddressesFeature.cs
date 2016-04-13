//// Copyright (c) .NET Foundation. All rights reserved.
//// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.AspNetCore.Server.Features;

namespace Microsoft.AspNetCore.Server.Kestrel
{
    // TODO: Move to internal namespace (like Microsoft.AspNetCore.Mvc.Internal) or make public?
    internal class ServerAddressesFeature : IServerAddressesFeature
    {
        public ICollection<string> Addresses { get; } = new List<string>();
    }
}
