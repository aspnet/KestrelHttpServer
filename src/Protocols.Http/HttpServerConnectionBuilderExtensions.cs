using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Protocols.Abstractions
{
    public static class HttpServerConnectionBuilderExtensions
    {
        public static IApplicationBuilder UseHttpServer(this IConnectionBuilder connectionBuilder)
        {
            return null;
        }
    }
}
