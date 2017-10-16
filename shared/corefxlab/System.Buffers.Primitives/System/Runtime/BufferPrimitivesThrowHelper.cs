// This file was processed with Internalizer tool and should not be edited manually

using System;
using System.Buffers;
using System.Runtime;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.System.Runtime
{
    public static class BufferPrimitivesThrowHelper
    {
        public static void ThrowArgumentNullException(ExceptionArgument argument)
        {
            throw GetArgumentNullException(argument);
        }

        public static void ThrowArgumentException()
        {
            throw GetArgumentException();
        }

        public static void ThrowArgumentException(ExceptionArgument argument)
        {
            throw GetArgumentException(argument);
        }

        public static void ThrowArgumentOutOfRangeException()
        {
            throw GetArgumentOutOfRangeException();
        }

        public static void ThrowArgumentOutOfRangeException(ExceptionArgument argument)
        {
            throw GetArgumentOutOfRangeException(argument);
        }

        public static void ThrowInvalidOperationException()
        {
            throw GetInvalidOperationException();
        }

        public static void ThrowInvalidOperationException_ForBoxingSpans()
        {
            throw GetInvalidOperationException_ForBoxingSpans();
        }

        public static void ThrowObjectDisposedException(string objectName)
        {
            throw GetObjectDisposedException(objectName);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ArgumentNullException GetArgumentNullException(ExceptionArgument argument)
        {
            return new ArgumentNullException(GetArgumentName(argument));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ArgumentException GetArgumentException()
        {
            return new ArgumentException();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ArgumentException GetArgumentException(ExceptionArgument argument)
        {
            return new ArgumentException(GetArgumentName(argument));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ArgumentOutOfRangeException GetArgumentOutOfRangeException()
        {
            return new ArgumentOutOfRangeException();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ArgumentOutOfRangeException GetArgumentOutOfRangeException(ExceptionArgument argument)
        {
            return new ArgumentOutOfRangeException(GetArgumentName(argument));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InvalidOperationException GetInvalidOperationException()
        {
            return new InvalidOperationException();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InvalidOperationException GetInvalidOperationException_ForBoxingSpans()
        {
            return new InvalidOperationException("Spans must not be boxed");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ObjectDisposedException GetObjectDisposedException(string objectName)
        {
            return new ObjectDisposedException(objectName);
        }

        private static string GetArgumentName(ExceptionArgument argument)
        {
            Debug.Assert(Enum.IsDefined(typeof(ExceptionArgument), argument),
                "The enum value is not defined, please check the ExceptionArgument Enum.");

            return argument.ToString();
        }
    }

    public enum ExceptionArgument
    {
        pointer,
        array
    }
}