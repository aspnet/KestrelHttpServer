// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    public class Requests
    {
        public const int Pipelining = 16;

        private const string plaintextRequest = "GET /plaintext HTTP/1.1\r\nHost: www.example.com\r\n\r\n";

        private const string liveAspNetRequest = "GET https://live.asp.net/ HTTP/1.1\r\n" +
            "Host: live.asp.net\r\n" +
            "Connection: keep-alive\r\n" +
            "Upgrade-Insecure-Requests: 1\r\n" +
            "User-Agent: Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/54.0.2840.99 Safari/537.36\r\n" +
            "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8\r\n" +
            "DNT: 1\r\n" +
            "Accept-Encoding: gzip, deflate, sdch, br\r\n" +
            "Accept-Language: en-US,en;q=0.8\r\n" +
            "Cookie: __unam=7a67379-1s65dc575c4-6d778abe-1; omniID=9519gfde_3347_4762_8762_df51458c8ec2\r\n\r\n";

        private const string unicodeRequest =
            "GET http://stackoverflow.com/questions/40148683/why-is-%e0%a5%a7%e0%a5%a8%e0%a5%a9-numeric HTTP/1.1\r\n" +
            "Accept: text/html, application/xhtml+xml, image/jxr, */*\r\n" +
            "Accept-Language: en-US,en-GB;q=0.7,en;q=0.3\r\n" +
            "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/52.0.2743.116 Safari/537.36 Edge/15.14965\r\n" +
            "Accept-Encoding: gzip, deflate\r\n" +
            "Host: stackoverflow.com\r\n" +
            "Connection: Keep-Alive\r\n" +
            "Cache-Control: max-age=0\r\n" +
            "Upgrade-Insecure-Requests: 1\r\n" +
            "DNT: 1\r\n" +
            "Referer: http://stackoverflow.com/?tab=month\r\n" +
            "Pragma: no-cache\r\n" +
            "Cookie: prov=20629ccd-8b0f-e8ef-2935-cd26609fc0bc; __qca=P0-1591065732-1479167353442; _ga=GA1.2.1298898376.1479167354; _gat=1; sgt=id=9519gfde_3347_4762_8762_df51458c8ec2; acct=t=why-is-%e0%a5%a7%e0%a5%a8%e0%a5%a9-numeric&s=why-is-%e0%a5%a7%e0%a5%a8%e0%a5%a9-numeric\r\n\r\n";

        public static readonly byte[] PlaintextPipelinedRequests = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(plaintextRequest, Pipelining)));
        public static readonly byte[] PlaintextRequest = Encoding.ASCII.GetBytes(plaintextRequest);

        public static readonly byte[] LiveAspNetPipelinedRequests = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(liveAspNetRequest, Pipelining)));
        public static readonly byte[] LiveAspNetRequest = Encoding.ASCII.GetBytes(liveAspNetRequest);

        public static readonly byte[] UnicodePipelinedRequests = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(unicodeRequest, Pipelining)));
        public static readonly byte[] UnicodeRequest = Encoding.ASCII.GetBytes(unicodeRequest);

        public static void SetupFrameObjects(out MemoryPool memoryPool, out SocketInput input, out Frame<object> frame)
        {
            memoryPool = new MemoryPool();

            var trace = new KestrelTrace(new TestKestrelTrace());
            var threadPool = new LoggingThreadPool(trace);
            input = new SocketInput(memoryPool, threadPool);

            var connectionContext = new MockConnection(new KestrelServerOptions());
            connectionContext.Input = input;

            frame = new Frame<object>(application: null, context: connectionContext);
        }

        public static void CleanUpFrameObjects(ref MemoryPool memoryPool, ref SocketInput input, ref Frame<object> frame)
        {
            input.IncomingFin();
            input.Dispose();
            memoryPool.Dispose();
        }
    }
}