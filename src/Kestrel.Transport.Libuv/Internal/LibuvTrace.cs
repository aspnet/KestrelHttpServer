// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal
{
    public class LibuvTrace : ILibuvTrace
    {
        // ConnectionRead: Reserved: 3

        private static readonly Action<ILogger, string, Exception> _connectionPause =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(4, nameof(ConnectionPause)), @"Connection id ""{ConnectionId}"" paused.");

        private static readonly Action<ILogger, string, Exception> _connectionResume =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(5, nameof(ConnectionResume)), @"Connection id ""{ConnectionId}"" resumed.");

        private static readonly Action<ILogger, string, Exception> _connectionReadFin =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(6, nameof(ConnectionReadFin)), @"Connection id ""{ConnectionId}"" received FIN.");

        private static readonly Action<ILogger, string, Exception> _connectionWriteFin =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(7, nameof(ConnectionWriteFin)), @"Connection id ""{ConnectionId}"" sending FIN.");

        // ConnectionWrite: Reserved: 11

        // ConnectionWriteCallback: Reserved: 12

        private static readonly Action<ILogger, string, Exception> _connectionError =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(14, nameof(ConnectionError)), @"Connection id ""{ConnectionId}"" communication error.");

        private static readonly Action<ILogger, string, Exception> _connectionReset =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(19, nameof(ConnectionReset)), @"Connection id ""{ConnectionId}"" reset.");

        private static readonly Action<ILogger, string, string, Exception> _connectionAborted =
            LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(20, nameof(ConnectionAborted)), @"Connection id ""{ConnectionId}"" closing because: ""{Message}""");

        private readonly ILogger _logger;

        public LibuvTrace(ILogger logger)
        {
            _logger = logger;
        }

        public void ConnectionRead(string connectionId, int count)
        {
            // Don't log for now since this could be *too* verbose.
            // Reserved: Event ID 3
        }

        public void ConnectionReadFin(string connectionId)
        {
            _connectionReadFin(_logger, connectionId, null);
        }

        public void ConnectionWriteFin(string connectionId)
        {
            _connectionWriteFin(_logger, connectionId, null);
        }

        public void ConnectionWrite(string connectionId, int count)
        {
            // Don't log for now since this could be *too* verbose.
            // Reserved: Event ID 11
        }

        public void ConnectionWriteCallback(string connectionId, int status)
        {
            // Don't log for now since this could be *too* verbose.
            // Reserved: Event ID 12
        }

        public void ConnectionError(string connectionId, Exception ex)
        {
            _connectionError(_logger, connectionId, ex);
        }

        public void ConnectionReset(string connectionId)
        {
            _connectionReset(_logger, connectionId, null);
        }

        public void ConnectionPause(string connectionId)
        {
            _connectionPause(_logger, connectionId, null);
        }

        public void ConnectionResume(string connectionId)
        {
            _connectionResume(_logger, connectionId, null);
        }

        public void ConnectionAborted(string connectionId, string message)
        {
            _connectionAborted(_logger, connectionId, message, null);
        }

        public IDisposable BeginScope<TState>(TState state) => _logger.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _logger.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => _logger.Log(logLevel, eventId, state, exception, formatter);
    }
}
