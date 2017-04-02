// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Transport;

namespace Microsoft.AspNetCore.Server.Kestrel
{
    /// <summary>
    /// Enumerates the <see cref="IListenOptions"/> types.
    /// </summary>
    public enum ListenType
    {
        IPEndPoint,
        SocketPath,
        FileHandle
    }
}
