// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack
{
    public struct HeaderField
    {
        public HeaderField(Span<byte> name, Span<byte> value)
        {
            Name = new byte[name.Length];
            name.CopyTo(Name);

            Value = new byte[value.Length];
            value.CopyTo(Value);
        }

        public byte[] Name { get; }

        public byte[] Value { get; }

        // + 32 explained here: http://httpwg.org/specs/rfc7541.html#rfc.section.4.1
        public int Length => Name.Length + Value.Length + 32;
    }
}
