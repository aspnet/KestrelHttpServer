// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Microsoft.AspNetCore.Testing
{
    public static class ListenOptionsExtensions
    {
        public static string GetListenOptionsDetails(this ListenOptions options)
        {
            var sb = new StringBuilder();
            sb.Append(options.GetDisplayName());

            foreach (var connectionAdapter in options.ConnectionAdapters)
            {
                sb.Append(connectionAdapter.GetType().Name);
            }

            return  sb.ToString();
        }
    }
}