// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class HttpProtocolFeatureCollection
    {
        private readonly IFeatureCollection _collection;

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IHttpRequestFeature GetViaTypeOf_First()
        {
            return (IHttpRequestFeature)_collection[typeof(IHttpRequestFeature)];
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IHttpRequestFeature GetViaGeneric_First()
        {
            return _collection.Get<IHttpRequestFeature>();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IHttpSendFileFeature GetViaTypeOf_Last()
        {
            return (IHttpSendFileFeature)_collection[typeof(IHttpSendFileFeature)];
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IHttpSendFileFeature GetViaGeneric_Last()
        {
            return _collection.Get<IHttpSendFileFeature>();
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public object GetViaTypeOf_Custom()
        {
            return (IHttpCustomFeature)_collection[typeof(IHttpCustomFeature)];
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public object GetViaGeneric_Custom()
        {
            return _collection.Get<IHttpCustomFeature>();
        }


        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public object GetViaTypeOf_NotFound()
        {
            return (IHttpNotFoundFeature)_collection[typeof(IHttpNotFoundFeature)];
        }

        [Benchmark]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public object GetViaGeneric_NotFound()
        {
            return _collection.Get<IHttpNotFoundFeature>();
        }

        public HttpProtocolFeatureCollection()
        {
            var pipeFactory = new PipeFactory();
            var pair = pipeFactory.CreateConnectionPair();

            var serviceContext = new ServiceContext
            {
                HttpParserFactory = _ => NullParser<Http1ParsingHandler>.Instance,
                ServerOptions = new KestrelServerOptions()
            };
            var http1ConnectionContext = new Http1ConnectionContext
            {
                ServiceContext = serviceContext,
                ConnectionFeatures = new FeatureCollection(),
                PipeFactory = pipeFactory,
                Application = pair.Application,
                Transport = pair.Transport
            };

            var http1Connection = new Http1Connection<object>(application: null, context: http1ConnectionContext);
            http1Connection.Reset();

            _collection = http1Connection;

            IHttpSendFileFeature sendFileFeature = new SendFileFeature();
            _collection.Set(sendFileFeature);
        }


        private class SendFileFeature : IHttpSendFileFeature
        {
            public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellation)
            {
                throw new System.NotImplementedException();
            }
        }

        private interface IHttpCustomFeature
        {
        }

        private interface IHttpNotFoundFeature
        {
        }
    }
}
