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
        private static ILogger _logger;
        private static readonly KestrelTrace _instance = new KestrelTrace();

        public static void Initialize(ILogger logger)
        {
            _logger = logger;
        }

        private KestrelTrace()
        {
        }

        public static KestrelTrace Log
        {
            get
            {
                if (_logger == null)
                {
                    throw new InvalidOperationException($"{nameof(KestrelTrace)} is not initialized. Please call {nameof(Initialize)}({nameof(ILogger)}) method first.");
                }

                return _instance;
            }
        }

        public void ConnectionStart(long connectionId)
        {
            _logger.LogDebug(13, $"{nameof(ConnectionStart)} -> Id: {connectionId}");
        }

        public void ConnectionStop(long connectionId)
        {
            _logger.LogDebug(14, $"{nameof(ConnectionStop)} -> Id: {connectionId}");
        }

        internal void ConnectionRead(long connectionId, int status)
        {
            _logger.LogDebug(4, $"{nameof(ConnectionRead)} -> Id: {connectionId}, Status: {status}");
        }

        internal void ConnectionPause(long connectionId)
        {
            _logger.LogDebug(5, $"{nameof(ConnectionPause)} -> Id: {connectionId}");
        }

        internal void ConnectionResume(long connectionId)
        {
            _logger.LogDebug(6, $"{nameof(ConnectionResume)} -> Id: {connectionId}");
        }

        internal void ConnectionReadFin(long connectionId)
        {
            _logger.LogDebug(7, $"{nameof(ConnectionReadFin)} -> Id: {connectionId}");
        }

        internal void ConnectionWriteFin(long connectionId, int step)
        {
            _logger.LogDebug(8, $"{nameof(ConnectionWriteFin)} -> Id: {connectionId}, Step: {step}");
        }

        internal void ConnectionKeepAlive(long connectionId)
        {
            _logger.LogDebug(9, $"{nameof(ConnectionKeepAlive)} -> Id: {connectionId}");
        }

        internal void ConnectionDisconnect(long connectionId)
        {
            _logger.LogDebug(10, $"{nameof(ConnectionDisconnect)} -> Id: {connectionId}");
        }

        internal void ConnectionWrite(long connectionId, int count)
        {
            _logger.LogDebug(11, $"{nameof(ConnectionWrite)} -> Id: {connectionId}, Count: {count}");
        }

        internal void ConnectionWriteCallback(long connectionId, int status)
        {
            _logger.LogDebug(12, $"{nameof(ConnectionWriteCallback)} -> Id: {connectionId}, Status: {status}");
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
