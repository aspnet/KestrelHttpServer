// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Framework.Logging;

namespace Microsoft.AspNet.Server.Kestrel
{
    /// <summary>
    /// Summary description for KestrelTrace
    /// </summary>
    public class KestrelTrace : ILogger
    {
        public readonly ILogger _logger; 

        public KestrelTrace(ILogger logger)
        {
            _logger = logger;
            Log = this;
        }

        public static KestrelTrace Log;
  
        public void ConnectionStart(long connectionId)
        {
            this.LogDebug(13, $"{nameof(ConnectionStart)}: Id: {connectionId}");
        }

      //  [Event(14, Level = EventLevel.Informational, Message = "Id {0}")]
        public void ConnectionStop(long connectionId)
        {
            this.LogDebug(14, $"ConnectionStop(connectionId: {connectionId})");
            //     WriteEvent(14, connectionId);
        }


   //     [Event(4, Message = "Id {0} Status {1}")]
        internal void ConnectionRead(long connectionId, int status)
        {
            this.LogDebug(4, $"ConnectionStop(connectionId: {connectionId}, status: {status})");
            //       WriteEvent(4, connectionId, status);
        }

 //       [Event(5, Message = "Id {0}")]
        internal void ConnectionPause(long connectionId)
        {
            this.LogDebug(5, $"ConnectionPause(connectionId: {connectionId})");
            //         WriteEvent(5, connectionId);
        }

 //       [Event(6, Message = "Id {0}")]
        internal void ConnectionResume(long connectionId)
        {
            this.LogDebug(6, $"ConnectionResume(connectionId: {connectionId})");
            //         WriteEvent(6, connectionId);
        }

  //      [Event(7, Message = "Id {0}")]
        internal void ConnectionReadFin(long connectionId)
        {
            this.LogDebug(7, $"ConnectionReadFin(connectionId: {connectionId})");
            //        WriteEvent(7, connectionId);
        }

//        [Event(8, Message = "Id {0} Step {1}")]
        internal void ConnectionWriteFin(long connectionId, int step)
        {
            this.LogDebug(8, $"{nameof(ConnectionWriteFin)}({nameof(connectionId)}: {connectionId}, {nameof(step)}: {step})");
            //          WriteEvent(8, connectionId, step);
        }

 //       [Event(9, Message = "Id {0}")]
        internal void ConnectionKeepAlive(long connectionId)
        {
            this.LogDebug(9, $"{nameof(ConnectionKeepAlive)}({nameof(connectionId)}: {connectionId})");
            //         WriteEvent(9, connectionId);
        }

 //       [Event(10, Message = "Id {0}")]
        internal void ConnectionDisconnect(long connectionId)
        {
            this.LogDebug(10, $"{nameof(ConnectionDisconnect)}({nameof(connectionId)}: {connectionId})");
            //         WriteEvent(10, connectionId);
        }

  //      [Event(11, Message = "Id {0} Count {1}")]
        internal void ConnectionWrite(long connectionId, int count)
        {
            this.LogDebug(11,
                $"{nameof(ConnectionWrite)}({nameof(connectionId)}: {connectionId}, {nameof(count)}: {count})");
            //        WriteEvent(11, connectionId, count);
        }

 //       [Event(12, Message = "Id {0} Status {1}")]
        internal void ConnectionWriteCallback(long connectionId, int status)
        {
            this.LogDebug(11,
                $"{nameof(ConnectionWriteCallback)}({nameof(connectionId)}: {connectionId}, {nameof(status)}: {status})");
            //         WriteEvent(12, connectionId, status);
        }

        void ILogger.Log(LogLevel logLevel, int eventId, object state, Exception exception, Func<object, Exception, string> formatter)
        {
            _logger.Log(logLevel, eventId, state, exception, formatter);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _logger.IsEnabled(logLevel);
        }

        public IDisposable BeginScopeImpl(object state)
        {
            return _logger.BeginScopeImpl(state);
        }
    }
}
