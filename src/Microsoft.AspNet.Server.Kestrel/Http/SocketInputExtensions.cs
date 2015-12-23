﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNet.Server.Kestrel.Infrastructure;

namespace Microsoft.AspNet.Server.Kestrel.Http
{
    public static class SocketInputExtensions
    {
        public static Task<int> ReadAsync(this SocketInput input, byte[] buffer, int offset, int count)
        {
            while (input.IsCompleted)
            {
                var begin = input.ConsumingStart();
                int actual;
                var end = begin.CopyTo(buffer, offset, count, out actual);
                input.ConsumingComplete(end, end);

                if (actual != 0)
                {
                    return Task.FromResult(actual);
                }
                if (input.RemoteIntakeFin)
                {
                    return TaskUtilities.ZeroTask;
                }
            }

            return input.ReadAsyncAwaited(buffer, offset, count);
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
