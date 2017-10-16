// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.Collections.Sequences
{
    // new interface
    public interface ISequence<T>
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="position"></param>
        /// <param name="advance"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        bool TryGet(ref Position position, out T item, bool advance = true);
    }
}
