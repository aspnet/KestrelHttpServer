// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal
{
    public class KestrelTrace : IKestrelTrace
    {
        private static readonly Action<ILogger, string, Exception> _connectionStart =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, nameof(ConnectionStart)), @"Connection id ""{ConnectionId}"" started.");

        private static readonly Action<ILogger, string, Exception> _connectionStop =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(2, nameof(ConnectionStop)), @"Connection id ""{ConnectionId}"" stopped.");

        private static readonly Action<ILogger, string, Exception> _connectionPause =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(4, nameof(ConnectionPause)), @"Connection id ""{ConnectionId}"" paused.");

        private static readonly Action<ILogger, string, Exception> _connectionResume =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(5, nameof(ConnectionResume)), @"Connection id ""{ConnectionId}"" resumed.");

        private static readonly Action<ILogger, string, Exception> _connectionKeepAlive =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(9, nameof(ConnectionKeepAlive)), @"Connection id ""{ConnectionId}"" completed keep alive response.");

        private static readonly Action<ILogger, string, Exception> _connectionDisconnect =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(10, nameof(ConnectionDisconnect)), @"Connection id ""{ConnectionId}"" disconnecting.");

        private static readonly Action<ILogger, string, string, Exception> _applicationError =
            LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(13, nameof(ApplicationError)), @"Connection id ""{ConnectionId}"", Request id ""{TraceIdentifier}"": An unhandled exception was thrown by the application.");

        private static readonly Action<ILogger, Exception> _notAllConnectionsClosedGracefully =
            LoggerMessage.Define(LogLevel.Debug, new EventId(16, nameof(NotAllConnectionsClosedGracefully)), "Some connections failed to close gracefully during server shutdown.");

        private static readonly Action<ILogger, string, string, Exception> _connectionBadRequest =
            LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(17, nameof(ConnectionBadRequest)), @"Connection id ""{ConnectionId}"" bad request data: ""{message}""");

        private static readonly Action<ILogger, string, long, Exception> _connectionHeadResponseBodyWrite =
            LoggerMessage.Define<string, long>(LogLevel.Debug, new EventId(18, nameof(ConnectionHeadResponseBodyWrite)), @"Connection id ""{ConnectionId}"" write of ""{count}"" body bytes to non-body HEAD response.");

        private static readonly Action<ILogger, string, Exception> _requestProcessingError =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(20, nameof(RequestProcessingError)), @"Connection id ""{ConnectionId}"" request processing ended abnormally.");

        private static readonly Action<ILogger, Exception> _notAllConnectionsAborted =
            LoggerMessage.Define(LogLevel.Debug, new EventId(21, nameof(NotAllConnectionsAborted)), "Some connections failed to abort during server shutdown.");

        private static readonly Action<ILogger, TimeSpan, DateTimeOffset, Exception> _heartbeatSlow =
            LoggerMessage.Define<TimeSpan, DateTimeOffset>(LogLevel.Warning, new EventId(22, nameof(HeartbeatSlow)), @"Heartbeat took longer than ""{interval}"" at ""{now}"".");

        private static readonly Action<ILogger, string, Exception> _applicationNeverCompleted =
            LoggerMessage.Define<string>(LogLevel.Critical, new EventId(23, nameof(ApplicationNeverCompleted)), @"Connection id ""{ConnectionId}"" application never completed");

        private static readonly Action<ILogger, string, Exception> _connectionRejected =
            LoggerMessage.Define<string>(LogLevel.Warning, new EventId(24, nameof(ConnectionRejected)), @"Connection id ""{ConnectionId}"" rejected because the maximum number of concurrent connections has been reached.");

        private static readonly Action<ILogger, string, string, Exception> _requestBodyStart =
            LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(25, nameof(RequestBodyStart)), @"Connection id ""{ConnectionId}"", Request id ""{TraceIdentifier}"": started reading request body.");

        private static readonly Action<ILogger, string, string, Exception> _requestBodyDone =
            LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(26, nameof(RequestBodyDone)), @"Connection id ""{ConnectionId}"", Request id ""{TraceIdentifier}"": done reading request body.");

        private static readonly Action<ILogger, string, string, double, Exception> _requestBodyMinimumDataRateNotSatisfied =
            LoggerMessage.Define<string, string, double>(LogLevel.Information, new EventId(27, nameof(RequestBodyMininumDataRateNotSatisfied)), @"Connection id ""{ConnectionId}"", Request id ""{TraceIdentifier}"": the request timed out because it was not sent by the client at a minimum of {Rate} bytes/second.");

        private static readonly Action<ILogger, string, string, Exception> _responseMinimumDataRateNotSatisfied =
            LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(28, nameof(ResponseMininumDataRateNotSatisfied)), @"Connection id ""{ConnectionId}"", Request id ""{TraceIdentifier}"": the connection was closed because the response was not read by the client at the specified minimum data rate.");

        private static readonly Action<ILogger, string, Exception> _http2ConnectionError =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(29, nameof(Http2ConnectionError)), @"Connection id ""{ConnectionId}"": HTTP/2 connection error.");

        private static readonly Action<ILogger, string, Exception> _http2StreamError =
            LoggerMessage.Define<string>(LogLevel.Information, new EventId(30, nameof(Http2StreamError)), @"Connection id ""{ConnectionId}"": HTTP/2 stream error.");

        private static readonly Action<ILogger, string, int, Exception> _hpackDecodingError =
            LoggerMessage.Define<string, int>(LogLevel.Information, new EventId(31, nameof(HPackDecodingError)), @"Connection id ""{ConnectionId}"": HPACK decoding error while decoding headers for stream ID {StreamId}.");

        private static readonly Action<ILogger, string, string, Exception> _requestBodyNotEntirelyRead =
            LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(32, nameof(RequestBodyNotEntirelyRead)), @"Connection id ""{ConnectionId}"", Request id ""{TraceIdentifier}"": the application completed without reading the entire request body.");

        private static readonly Action<ILogger, string, string, Exception> _requestBodyDrainTimedOut =
            LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(33, nameof(RequestBodyDrainTimedOut)), @"Connection id ""{ConnectionId}"", Request id ""{TraceIdentifier}"": automatic draining of the request body timed out after taking over 5 seconds.");

        private static readonly Action<ILogger, string, string, Exception> _applicationAbortedConnection =
            LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(34, nameof(RequestBodyDrainTimedOut)), @"Connection id ""{ConnectionId}"", Request id ""{TraceIdentifier}"": the application aborted the connection.");

        protected readonly ILogger _logger;

        public KestrelTrace(ILogger logger)
        {
            _logger = logger;
        }

        public virtual void ConnectionStart(string connectionId)
        {
            _connectionStart(_logger, connectionId, null);
        }

        public virtual void ConnectionStop(string connectionId)
        {
            _connectionStop(_logger, connectionId, null);
        }

        public virtual void ConnectionPause(string connectionId)
        {
            _connectionPause(_logger, connectionId, null);
        }

        public virtual void ConnectionResume(string connectionId)
        {
            _connectionResume(_logger, connectionId, null);
        }

        public virtual void ConnectionKeepAlive(string connectionId)
        {
            _connectionKeepAlive(_logger, connectionId, null);
        }

        public virtual void ConnectionRejected(string connectionId)
        {
            _connectionRejected(_logger, connectionId, null);
        }

        public virtual void ConnectionDisconnect(string connectionId)
        {
            _connectionDisconnect(_logger, connectionId, null);
        }

        public virtual void ApplicationError(string connectionId, string traceIdentifier, Exception ex)
        {
            _applicationError(_logger, connectionId, traceIdentifier, ex);
        }

        public virtual void ConnectionHeadResponseBodyWrite(string connectionId, long count)
        {
            _connectionHeadResponseBodyWrite(_logger, connectionId, count, null);
        }

        public virtual void NotAllConnectionsClosedGracefully()
        {
            _notAllConnectionsClosedGracefully(_logger, null);
        }

        public virtual void ConnectionBadRequest(string connectionId, BadHttpRequestException ex)
        {
            _connectionBadRequest(_logger, connectionId, ex.Message, ex);
        }

        public virtual void RequestProcessingError(string connectionId, Exception ex)
        {
            _requestProcessingError(_logger, connectionId, ex);
        }

        public virtual void NotAllConnectionsAborted()
        {
            _notAllConnectionsAborted(_logger, null);
        }

        public virtual void HeartbeatSlow(TimeSpan interval, DateTimeOffset now)
        {
            _heartbeatSlow(_logger, interval, now, null);
        }

        public virtual void ApplicationNeverCompleted(string connectionId)
        {
            _applicationNeverCompleted(_logger, connectionId, null);
        }

        public virtual void RequestBodyStart(string connectionId, string traceIdentifier)
        {
            _requestBodyStart(_logger, connectionId, traceIdentifier, null);
        }

        public virtual void RequestBodyDone(string connectionId, string traceIdentifier)
        {
            _requestBodyDone(_logger, connectionId, traceIdentifier, null);
        }

        public virtual void RequestBodyMininumDataRateNotSatisfied(string connectionId, string traceIdentifier, double rate)
        {
            _requestBodyMinimumDataRateNotSatisfied(_logger, connectionId, traceIdentifier, rate, null);
        }

        public virtual void RequestBodyNotEntirelyRead(string connectionId, string traceIdentifier)
        {
            _requestBodyNotEntirelyRead(_logger, connectionId, traceIdentifier, null);
        }

        public virtual void RequestBodyDrainTimedOut(string connectionId, string traceIdentifier)
        {
            _requestBodyDrainTimedOut(_logger, connectionId, traceIdentifier, null);
        }

        public virtual void ResponseMininumDataRateNotSatisfied(string connectionId, string traceIdentifier)
        {
            _responseMinimumDataRateNotSatisfied(_logger, connectionId, traceIdentifier, null);
        }

        public virtual void ApplicationAbortedConnection(string connectionId, string traceIdentifier)
        {
            _applicationAbortedConnection(_logger, connectionId, traceIdentifier, null);
        }

        public virtual void Http2ConnectionError(string connectionId, Http2ConnectionErrorException ex)
        {
            _http2ConnectionError(_logger, connectionId, ex);
        }

        public virtual void Http2StreamError(string connectionId, Http2StreamErrorException ex)
        {
            _http2StreamError(_logger, connectionId, ex);
        }

        public virtual void HPackDecodingError(string connectionId, int streamId, HPackDecodingException ex)
        {
            _hpackDecodingError(_logger, connectionId, streamId, ex);
        }

        public virtual void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => _logger.Log(logLevel, eventId, state, exception, formatter);

        public virtual bool IsEnabled(LogLevel logLevel) => _logger.IsEnabled(logLevel);

        public virtual IDisposable BeginScope<TState>(TState state) => _logger.BeginScope(state);
    }
}
