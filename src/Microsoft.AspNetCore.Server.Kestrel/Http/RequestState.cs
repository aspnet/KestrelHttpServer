// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    // enum, but enum doesn't work with Interlocked
    class RequestState
    {
        public const int NotStarted = -1;
        public const int Waiting = 0;
        public const int ReadingHeaders = 1;
        public const int ExecutingRequest = 2;
        public const int UpgradedRequest = 3;
        // Do not change order of these with out changing comparision tests
        public const int Stopping = 99;
        // States are status codes
        public const int Timeout = 408;
        // Other final states
        public const int Stopped = 1000;
        public const int Aborted = 1001;
    }
}
