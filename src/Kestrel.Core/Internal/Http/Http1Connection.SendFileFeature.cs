// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public partial class Http1Connection : IHttpSendFileFeature
    {
        async Task IHttpSendFileFeature.SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (count.HasValue && count.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var fileSize = (new System.IO.FileInfo(path)).Length;
            if (offset > fileSize)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (!count.HasValue)
            {
                count = fileSize - offset;
            }
            else if (fileSize - offset < count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var actualCount = count.Value;

            cancellationToken.ThrowIfCancellationRequested();
            using (var sendFile = new SendFile(this, _context.Transport.Output))
            {
                await sendFile.SendFileAsync(path, offset, actualCount, cancellationToken);
            } 
        }
    }
}
