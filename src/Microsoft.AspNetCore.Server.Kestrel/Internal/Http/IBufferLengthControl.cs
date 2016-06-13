﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public interface IBufferLengthControl
    {
        void Add(int count);
        void Subtract(int count);
    }
}
