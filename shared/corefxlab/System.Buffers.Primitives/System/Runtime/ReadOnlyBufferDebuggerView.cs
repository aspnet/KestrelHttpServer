// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Server.Kestrel.Internal.System.Buffers;
using System.Diagnostics;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.Runtime
{
    internal class ReadOnlyBufferDebuggerView<T>
    {
        private ReadOnlyBuffer<T> _buffer;

        public ReadOnlyBufferDebuggerView(ReadOnlyBuffer<T> buffer)
        {
            _buffer = buffer;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items
        {
            get {
                return _buffer.ToArray();
            }
        }
    }
}
