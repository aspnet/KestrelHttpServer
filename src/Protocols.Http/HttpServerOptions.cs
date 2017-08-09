using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.Server.Kestrel.Core
{
    public class HttpServerOptions
    {
        /// <summary>
        /// Gets or sets whether the <c>Server</c> header should be included in each response.
        /// </summary>
        /// <remarks>
        /// Defaults to true.
        /// </remarks>
        public bool AddServerHeader { get; set; } = true;

        public HttpServerLimits Limits { get; set; }
        public bool AllowSynchronousIO { get; set; }
    }
}
