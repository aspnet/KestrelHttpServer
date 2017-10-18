// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class HttpProtocolFeatureCollectionTests : IDisposable
    {
        private readonly PipeFactory _pipeFactory;
        private readonly Http1ConnectionContext _http1ConnectionContext;
        private readonly Http1Connection _http1Connection;
        private readonly IFeatureCollection _collection;

        public HttpProtocolFeatureCollectionTests()
        {
            _pipeFactory = new PipeFactory();
            var pair = _pipeFactory.CreateConnectionPair();

            var serviceContext = new ServiceContext
            {
                HttpParserFactory = _ => new HttpParser<Http1ParsingHandler>(),
                ServerOptions = new KestrelServerOptions()
            };

            _http1ConnectionContext = new Http1ConnectionContext
            {
                ServiceContext = serviceContext,
                ConnectionFeatures = new FeatureCollection(),
                PipeFactory = _pipeFactory,
                Application = pair.Application,
                Transport = pair.Transport
            };

            _http1Connection = new Http1Connection<object>(application: null, context: _http1ConnectionContext);
            _http1Connection.Reset();
            _collection = _http1Connection;
        }

        public void Dispose()
        {
            _pipeFactory.Dispose();
        }

        [Fact]
        public int FeaturesStartAsSelf()
        {
            var featureCount = 0;
            foreach (var featureIter in _collection)
            {
                Type type = featureIter.Key;
                if (type.IsAssignableFrom(typeof(HttpProtocol)))
                {
                    var featureLookup = _collection[type];
                    Assert.Same(featureLookup, featureIter.Value);
                    Assert.Same(featureLookup, _collection);
                    featureCount++;
                }
            }

            Assert.NotEqual(0, featureCount);

            return featureCount;
        }

        [Fact]
        public int FeaturesCanBeAssignedTo()
        {
            var featureCount = SetFeaturesToNonDefault();
            Assert.NotEqual(0, featureCount);

            featureCount = 0;
            foreach (var feature in _collection)
            {
                Type type = feature.Key;
                if (type.IsAssignableFrom(typeof(HttpProtocol)))
                {
                    Assert.Same(_collection[type], feature.Value);
                    Assert.NotSame(_collection[type], _collection);
                    featureCount++;
                }
            }

            Assert.NotEqual(0, featureCount);

            return featureCount;
        }

        [Fact]
        public void FeaturesResetToSelf()
        {
            var featuresAssigned = SetFeaturesToNonDefault();
            _http1Connection.ResetFeatureCollection();
            var featuresReset = FeaturesStartAsSelf();

            Assert.Equal(featuresAssigned, featuresReset);
        }

        [Fact]
        public void FeaturesByGenericSameAsByType()
        {
            var featuresAssigned = SetFeaturesToNonDefault();

            CompareGenericGetterToIndexer();

            _http1Connection.ResetFeatureCollection();
            var featuresReset = FeaturesStartAsSelf();

            Assert.Equal(featuresAssigned, featuresReset);
        }

        [Fact]
        public void FeaturesSetByTypeSameAsGeneric()
        {
            _collection[typeof(IHttpRequestFeature)] = CreateHttp1Connection();
            _collection[typeof(IHttpResponseFeature)] = CreateHttp1Connection();
            _collection[typeof(IHttpRequestIdentifierFeature)] = CreateHttp1Connection();
            _collection[typeof(IHttpRequestLifetimeFeature)] = CreateHttp1Connection();
            _collection[typeof(IHttpConnectionFeature)] = CreateHttp1Connection();
            _collection[typeof(IHttpMaxRequestBodySizeFeature)] = CreateHttp1Connection();
            _collection[typeof(IHttpMinRequestBodyDataRateFeature)] = CreateHttp1Connection();
            _collection[typeof(IHttpMinResponseDataRateFeature)] = CreateHttp1Connection();
            _collection[typeof(IHttpBodyControlFeature)] = CreateHttp1Connection();

            CompareGenericGetterToIndexer();

            EachHttpProtocolFeatureSetAndUnique();
        }

        [Fact]
        public void FeaturesSetByGenericSameAsByType()
        {
            _collection.Set<IHttpRequestFeature>(CreateHttp1Connection());
            _collection.Set<IHttpResponseFeature>(CreateHttp1Connection());
            _collection.Set<IHttpRequestIdentifierFeature>(CreateHttp1Connection());
            _collection.Set<IHttpRequestLifetimeFeature>(CreateHttp1Connection());
            _collection.Set<IHttpConnectionFeature>(CreateHttp1Connection());
            _collection.Set<IHttpMaxRequestBodySizeFeature>(CreateHttp1Connection());
            _collection.Set<IHttpMinRequestBodyDataRateFeature>(CreateHttp1Connection());
            _collection.Set<IHttpMinResponseDataRateFeature>(CreateHttp1Connection());
            _collection.Set<IHttpBodyControlFeature>(CreateHttp1Connection());

            CompareGenericGetterToIndexer();

            EachHttpProtocolFeatureSetAndUnique();
        }

        private void CompareGenericGetterToIndexer()
        {
            Assert.Same(_collection.Get<IHttpRequestFeature>(), _collection[typeof(IHttpRequestFeature)]);
            Assert.Same(_collection.Get<IHttpResponseFeature>(), _collection[typeof(IHttpResponseFeature)]);
            Assert.Same(_collection.Get<IHttpRequestIdentifierFeature>(), _collection[typeof(IHttpRequestIdentifierFeature)]);
            Assert.Same(_collection.Get<IHttpRequestLifetimeFeature>(), _collection[typeof(IHttpRequestLifetimeFeature)]);
            Assert.Same(_collection.Get<IHttpConnectionFeature>(), _collection[typeof(IHttpConnectionFeature)]);
            Assert.Same(_collection.Get<IHttpMaxRequestBodySizeFeature>(), _collection[typeof(IHttpMaxRequestBodySizeFeature)]);
            Assert.Same(_collection.Get<IHttpMinRequestBodyDataRateFeature>(), _collection[typeof(IHttpMinRequestBodyDataRateFeature)]);
            Assert.Same(_collection.Get<IHttpMinResponseDataRateFeature>(), _collection[typeof(IHttpMinResponseDataRateFeature)]);
            Assert.Same(_collection.Get<IHttpBodyControlFeature>(), _collection[typeof(IHttpBodyControlFeature)]);
        }

        private int EachHttpProtocolFeatureSetAndUnique()
        {
            int featureCount = 0;
            foreach (var item in _collection)
            {
                Type type = item.Key;
                if (type.IsAssignableFrom(typeof(HttpProtocol)))
                {
                    Assert.Equal(1, _collection.Count(kv => ReferenceEquals(kv.Value, item.Value)));

                    featureCount++;
                }
            }

            Assert.NotEqual(0, featureCount);

            return featureCount;
        }

        private int SetFeaturesToNonDefault()
        {
            int featureCount = 0;
            foreach (var feature in _collection)
            {
                Type type = feature.Key;
                if (type.IsAssignableFrom(typeof(HttpProtocol)))
                {
                    _collection[type] = CreateHttp1Connection();
                    featureCount++;
                }
            }

            var protocolFeaturesCount = EachHttpProtocolFeatureSetAndUnique();

            Assert.Equal(protocolFeaturesCount, featureCount);

            return featureCount;
        }

        private HttpProtocol CreateHttp1Connection() => new Http1Connection<object>(application: null, context: _http1ConnectionContext);
    }
}
