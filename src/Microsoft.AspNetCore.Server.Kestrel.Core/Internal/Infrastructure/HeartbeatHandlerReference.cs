// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure
{
    public class HeartbeatHandlerReference
    {
        private readonly WeakReference<IHeartbeatHandler> _weakReference;

        public HeartbeatHandlerReference(IHeartbeatHandler handler)
        {
            _weakReference = new WeakReference<IHeartbeatHandler>(handler);
        }

        public bool TryGetHandler(out IHeartbeatHandler handler)
        {
            return _weakReference.TryGetTarget(out handler);
        }
    }
}
