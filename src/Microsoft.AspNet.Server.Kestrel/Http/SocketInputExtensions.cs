﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public static class SocketInputExtensions
    {
        public static ValueTask<int> ReadAsync(this SocketInput input, byte[] buffer, int offset, int count)
        {
            while (true)
            {
                if (!input.IsCompleted)
                {
                    return input.ReadAsyncAwaited(buffer, offset, count);
                }

                var begin = input.ConsumingStart();

                int actual;
                var end = begin.CopyTo(buffer, offset, count, out actual);
                input.ConsumingComplete(end, end);

                if (actual != 0)
                {
                    return actual;
                }
                if (input.RemoteIntakeFin)
                {
                    return 0;
                }
            }
        }

        private static async Task<int> ReadAsyncAwaited(this SocketInput input, byte[] buffer, int offset, int count)
        {
            while (true)
            {
                await input;

                var begin = input.ConsumingStart();
                int actual;
                var end = begin.CopyTo(buffer, offset, count, out actual);
                input.ConsumingComplete(end, end);

                if (actual != 0)
                {
                    return actual;
                }
                if (input.RemoteIntakeFin)
                {
                    return 0;
                }
            }
        }
    }
}
