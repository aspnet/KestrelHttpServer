using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Features
{
    interface IDecrementConcurrentConnectionCountFeature
    {
        /// <summary>
        /// Idempotent method to stop counting a connection towards <see cref="KestrelServerLimits.MaxConcurrentConnections"/>.
        /// </summary>
        void ReleaseConnection();
    }
}
