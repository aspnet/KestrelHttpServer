// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack
{
    public struct HeaderField
    {
        public HeaderField(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }

        public string Value { get; }

        // + 32 explained here: http://httpwg.org/specs/rfc7541.html#rfc.section.4.1
        public int Length => Name.Length + Value.Length + 32;
    }
}
