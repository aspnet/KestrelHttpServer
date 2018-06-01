﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http2.HPack;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    public class MockTrace : IKestrelTrace
    {
        public void ApplicationError(string connectionId, string requestId, Exception ex) { }
        public IDisposable BeginScope<TState>(TState state) => null;
        public void ConnectionBadRequest(string connectionId, BadHttpRequestException ex) { }
        public void ConnectionDisconnect(string connectionId) { }
        public void ConnectionError(string connectionId, Exception ex) { }
        public void ConnectionHeadResponseBodyWrite(string connectionId, long count) { }
        public void ConnectionKeepAlive(string connectionId) { }
        public void ConnectionPause(string connectionId) { }
        public void ConnectionRead(string connectionId, int count) { }
        public void ConnectionReadFin(string connectionId) { }
        public void ConnectionReset(string connectionId) { }
        public void ConnectionResume(string connectionId) { }
        public void ConnectionRejected(string connectionId) { }
        public void ConnectionStart(string connectionId) { }
        public void ConnectionStop(string connectionId) { }
        public void ConnectionWrite(string connectionId, int count) { }
        public void ConnectionWriteCallback(string connectionId, int status) { }
        public void ConnectionWriteFin(string connectionId) { }
        public void ConnectionWroteFin(string connectionId, int status) { }
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) { }
        public void NotAllConnectionsAborted() { }
        public void NotAllConnectionsClosedGracefully() { }
        public void RequestProcessingError(string connectionId, Exception ex) { }
        public void HeartbeatSlow(TimeSpan interval, DateTimeOffset now) { }
        public void ApplicationNeverCompleted(string connectionId) { }
        public void RequestBodyStart(string connectionId, string traceIdentifier) { }
        public void RequestBodyDone(string connectionId, string traceIdentifier) { }
        public void RequestBodyNotEntirelyRead(string connectionId, string traceIdentifier) { }
        public void RequestBodyDrainTimedOut(string connectionId, string traceIdentifier) { }
        public void RequestBodyMininumDataRateNotSatisfied(string connectionId, string traceIdentifier, double rate) { }
        public void ResponseMininumDataRateNotSatisfied(string connectionId, string traceIdentifier) { }
        public void ApplicationAbortedConnection(string connectionId, string traceIdentifier) { }
        public void Http2ConnectionError(string connectionId, Http2ConnectionErrorException ex) { }
        public void Http2StreamError(string connectionId, Http2StreamErrorException ex) { }
        public void HPackDecodingError(string connectionId, int streamId, HPackDecodingException ex) { }
    }
}
