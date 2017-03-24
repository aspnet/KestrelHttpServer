// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class ConnectionLifetimeControl
    {
        public ConnectionLifetimeControl(string connectionId, IPipe outputPipe, IKestrelTrace log)
        {
            ConnectionId = connectionId;
            Output = outputPipe;
            Log = log;
        }

        private string ConnectionId { get; }
        private IPipe Output { get; }
        private IKestrelTrace Log { get; }

        public void End(ProduceEndType endType)
        {
            switch (endType)
            {
                case ProduceEndType.ConnectionKeepAlive:
                    Log.ConnectionKeepAlive(ConnectionId);
                    break;
                case ProduceEndType.SocketShutdown:
                    Output.Reader.CancelPendingRead();
                    goto case ProduceEndType.SocketDisconnect;
                case ProduceEndType.SocketDisconnect:
                    Output.Writer.Complete();
                    Log.ConnectionDisconnect(ConnectionId);
                    break;
            }
        }
    }
}
