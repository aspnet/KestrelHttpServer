// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.WindowsRio.Internal
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RioRequestResults
    {
        internal const int MaxResults = 12;
        private const int SizeOfResultInLongs = 3;
        private const int LengthOfLongArray = MaxResults * SizeOfResultInLongs;

        private fixed long _results[LengthOfLongArray];

        public ref RioRequestResult this[int offset]
        {
            get
            {
                if ((uint)offset > MaxResults)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset));
                }

                fixed(long *p = _results)
                {
                    return ref Unsafe.AsRef<RioRequestResult>(&p[offset * SizeOfResultInLongs]);
                }
            }
        }
    }
}
