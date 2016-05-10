// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Pools.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Pools
{
    public class HeaderFactory : ComponentFactory<Headers>
    {
        public HeaderFactory() : base()
        {
        }

        public HeaderFactory(int MaxPooled) : base (MaxPooled)
        {
        }

        // https://github.com/dotnet/coreclr/pull/4468#issuecomment-212931043
        // 12x Faster than new T() which uses System.Activator 
        protected override Headers CreateNew(int correlationId) => new CorrelatedHeaders() { CorrelationId = correlationId };

        public override void Dispose(ref Headers component, bool requestImmediateReuse)
        {
            if (MaxPooled > 0)
            {
                if (requestImmediateReuse)
                {
                    component.Reset();
                }
                else
                {
                    CorrelatedHeaders headers = null;
                    if ((headers = component as CorrelatedHeaders) != null)
                    {
                        component.Uninitialize();
                        GetPool(headers.CorrelationId).Return(component);
                    }
                    component = null;
                }
            }
            else
            {
                component = null;
            }
        }
    }
}
