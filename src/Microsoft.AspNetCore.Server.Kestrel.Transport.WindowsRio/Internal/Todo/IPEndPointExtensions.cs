// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System.Net.Sockets
{
    internal static class IPEndPointExtensions
    {
        public static Internals.SocketAddress Serialize(EndPoint endpoint)
        {
            if (endpoint is DnsEndPoint)
            {
                throw new NotSupportedException();
            }

            var ipEndPoint = endpoint as IPEndPoint;
            if (ipEndPoint != null)
            {
                return new Internals.SocketAddress(ipEndPoint.Address, ipEndPoint.Port);
            }

            throw new NotSupportedException();
        }
    }
}
