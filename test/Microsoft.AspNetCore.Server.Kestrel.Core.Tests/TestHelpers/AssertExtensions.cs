﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Xunit.Sdk;

namespace Xunit
{
    public static class AssertExtensions
    {
        public static void Equal(byte[] expected, Span<byte> actual)
        {
            if (expected.Length != actual.Length)
            {
                throw new XunitException($"Expected length to be {expected.Length} but was {actual.Length}");
            }

            for (var i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                {
                    throw new XunitException($@"Expected byte at index {i} to be '{expected[i]}' but was '{actual[i]}'");
                }
            }
        }

        public static void Ascii(string expected, ArraySegment<byte> actual)
        {
            var bytes = Encoding.ASCII.GetBytes(expected);
            Assert.Equal(bytes.Length, actual.Count);
            for (var index = 0; index < bytes.Length; index++)
            {
                Assert.Equal(bytes[index], actual.Array[actual.Offset + index]);
            }
        }
    }
}
