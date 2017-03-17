﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Testing
{
    public class TestApplicationErrorLogger : ILogger
    {
        // Application errors are logged using 13 as the eventId.
        private const int ApplicationErrorEventId = 13;

        public bool ThrowOnCriticalErrors { get; set; } = true;

        public List<LogMessage> Messages { get; } = new List<LogMessage>();

        public int TotalErrorsLogged => Messages.Count(message => message.LogLevel == LogLevel.Error);

        public int CriticalErrorsLogged => Messages.Count(message => message.LogLevel == LogLevel.Critical);

        public int ApplicationErrorsLogged => Messages.Count(message => message.EventId.Id == ApplicationErrorEventId);

        public IDisposable BeginScope<TState>(TState state)
        {
            return new Disposable(() => { });
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
#if true
            if (logLevel == LogLevel.Critical && ThrowOnCriticalErrors)
#endif
            {
                Console.WriteLine($"Log {logLevel}[{eventId}]: {formatter(state, exception)} {exception?.Message}");

                if (logLevel == LogLevel.Critical && ThrowOnCriticalErrors)
                {
                    throw new Exception("Unexpected critical error.", exception);
                }
            }

            lock (Messages)
            {
                Messages.Add(new LogMessage
                {
                    LogLevel = logLevel,
                    EventId = eventId,
                    Exception = exception,
                    Message = formatter(state, exception)
                });
            }
        }

        public class LogMessage
        {
            public LogLevel LogLevel { get; set; }
            public EventId EventId { get; set; }
            public Exception Exception { get; set; }
            public string Message { get; set; }
        }
    }
}
