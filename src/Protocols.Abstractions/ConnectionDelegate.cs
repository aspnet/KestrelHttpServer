using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Protocols.Abstractions
{
    public delegate Task ConnectionDelegate(ConnectionContext connection);
}
