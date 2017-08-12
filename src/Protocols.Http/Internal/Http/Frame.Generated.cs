// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    public partial class Frame
    {
        private static readonly Type IHttpRequestFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IHttpRequestFeature);
        private static readonly Type IHttpResponseFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IHttpResponseFeature);
        private static readonly Type IHttpRequestIdentifierFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IHttpRequestIdentifierFeature);
        private static readonly Type IServiceProvidersFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IServiceProvidersFeature);
        private static readonly Type IHttpRequestLifetimeFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature);
        private static readonly Type IHttpConnectionFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IHttpConnectionFeature);
        private static readonly Type IHttpAuthenticationFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.Authentication.IHttpAuthenticationFeature);
        private static readonly Type IQueryFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IQueryFeature);
        private static readonly Type IFormFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IFormFeature);
        private static readonly Type IHttpUpgradeFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IHttpUpgradeFeature);
        private static readonly Type IResponseCookiesFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IResponseCookiesFeature);
        private static readonly Type IItemsFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IItemsFeature);
        private static readonly Type ITlsConnectionFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.ITlsConnectionFeature);
        private static readonly Type IHttpWebSocketFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IHttpWebSocketFeature);
        private static readonly Type ISessionFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.ISessionFeature);
        private static readonly Type IHttpMaxRequestBodySizeFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature);
        private static readonly Type IHttpMinRequestBodyDataRateFeatureType = typeof(global::Microsoft.AspNetCore.Server.Kestrel.Core.Features.IHttpMinRequestBodyDataRateFeature);
        private static readonly Type IHttpMinResponseDataRateFeatureType = typeof(global::Microsoft.AspNetCore.Server.Kestrel.Core.Features.IHttpMinResponseDataRateFeature);
        private static readonly Type IHttpBodyControlFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature);
        private static readonly Type IHttpSendFileFeatureType = typeof(global::Microsoft.AspNetCore.Http.Features.IHttpSendFileFeature);

        private object _currentIHttpRequestFeature;
        private object _currentIHttpResponseFeature;
        private object _currentIHttpRequestIdentifierFeature;
        private object _currentIServiceProvidersFeature;
        private object _currentIHttpRequestLifetimeFeature;
        private object _currentIHttpConnectionFeature;
        private object _currentIHttpAuthenticationFeature;
        private object _currentIQueryFeature;
        private object _currentIFormFeature;
        private object _currentIHttpUpgradeFeature;
        private object _currentIResponseCookiesFeature;
        private object _currentIItemsFeature;
        private object _currentITlsConnectionFeature;
        private object _currentIHttpWebSocketFeature;
        private object _currentISessionFeature;
        private object _currentIHttpMaxRequestBodySizeFeature;
        private object _currentIHttpMinRequestBodyDataRateFeature;
        private object _currentIHttpMinResponseDataRateFeature;
        private object _currentIHttpBodyControlFeature;
        private object _currentIHttpSendFileFeature;

        private void FastReset()
        {
            _currentIHttpRequestFeature = this;
            _currentIHttpResponseFeature = this;
            _currentIHttpUpgradeFeature = this;
            _currentIHttpRequestIdentifierFeature = this;
            _currentIHttpRequestLifetimeFeature = this;
            _currentIHttpConnectionFeature = this;
            _currentIHttpMaxRequestBodySizeFeature = this;
            _currentIHttpMinRequestBodyDataRateFeature = this;
            _currentIHttpMinResponseDataRateFeature = this;
            _currentIHttpBodyControlFeature = this;
            
            _currentIServiceProvidersFeature = null;
            _currentIHttpAuthenticationFeature = null;
            _currentIQueryFeature = null;
            _currentIFormFeature = null;
            _currentIResponseCookiesFeature = null;
            _currentIItemsFeature = null;
            _currentITlsConnectionFeature = null;
            _currentIHttpWebSocketFeature = null;
            _currentISessionFeature = null;
            _currentIHttpSendFileFeature = null;
        }

        internal object FastFeatureGet(Type key)
        {
            if (key == IHttpRequestFeatureType)
            {
                return _currentIHttpRequestFeature;
            }
            if (key == IHttpResponseFeatureType)
            {
                return _currentIHttpResponseFeature;
            }
            if (key == IHttpRequestIdentifierFeatureType)
            {
                return _currentIHttpRequestIdentifierFeature;
            }
            if (key == IServiceProvidersFeatureType)
            {
                return _currentIServiceProvidersFeature;
            }
            if (key == IHttpRequestLifetimeFeatureType)
            {
                return _currentIHttpRequestLifetimeFeature;
            }
            if (key == IHttpConnectionFeatureType)
            {
                return _currentIHttpConnectionFeature;
            }
            if (key == IHttpAuthenticationFeatureType)
            {
                return _currentIHttpAuthenticationFeature;
            }
            if (key == IQueryFeatureType)
            {
                return _currentIQueryFeature;
            }
            if (key == IFormFeatureType)
            {
                return _currentIFormFeature;
            }
            if (key == IHttpUpgradeFeatureType)
            {
                return _currentIHttpUpgradeFeature;
            }
            if (key == IResponseCookiesFeatureType)
            {
                return _currentIResponseCookiesFeature;
            }
            if (key == IItemsFeatureType)
            {
                return _currentIItemsFeature;
            }
            if (key == ITlsConnectionFeatureType)
            {
                return _currentITlsConnectionFeature;
            }
            if (key == IHttpWebSocketFeatureType)
            {
                return _currentIHttpWebSocketFeature;
            }
            if (key == ISessionFeatureType)
            {
                return _currentISessionFeature;
            }
            if (key == IHttpMaxRequestBodySizeFeatureType)
            {
                return _currentIHttpMaxRequestBodySizeFeature;
            }
            if (key == IHttpMinRequestBodyDataRateFeatureType)
            {
                return _currentIHttpMinRequestBodyDataRateFeature;
            }
            if (key == IHttpMinResponseDataRateFeatureType)
            {
                return _currentIHttpMinResponseDataRateFeature;
            }
            if (key == IHttpBodyControlFeatureType)
            {
                return _currentIHttpBodyControlFeature;
            }
            if (key == IHttpSendFileFeatureType)
            {
                return _currentIHttpSendFileFeature;
            }
            return ExtraFeatureGet(key);
        }

        internal void FastFeatureSet(Type key, object feature)
        {
            _featureRevision++;
            
            if (key == IHttpRequestFeatureType)
            {
                _currentIHttpRequestFeature = feature;
                return;
            }
            if (key == IHttpResponseFeatureType)
            {
                _currentIHttpResponseFeature = feature;
                return;
            }
            if (key == IHttpRequestIdentifierFeatureType)
            {
                _currentIHttpRequestIdentifierFeature = feature;
                return;
            }
            if (key == IServiceProvidersFeatureType)
            {
                _currentIServiceProvidersFeature = feature;
                return;
            }
            if (key == IHttpRequestLifetimeFeatureType)
            {
                _currentIHttpRequestLifetimeFeature = feature;
                return;
            }
            if (key == IHttpConnectionFeatureType)
            {
                _currentIHttpConnectionFeature = feature;
                return;
            }
            if (key == IHttpAuthenticationFeatureType)
            {
                _currentIHttpAuthenticationFeature = feature;
                return;
            }
            if (key == IQueryFeatureType)
            {
                _currentIQueryFeature = feature;
                return;
            }
            if (key == IFormFeatureType)
            {
                _currentIFormFeature = feature;
                return;
            }
            if (key == IHttpUpgradeFeatureType)
            {
                _currentIHttpUpgradeFeature = feature;
                return;
            }
            if (key == IResponseCookiesFeatureType)
            {
                _currentIResponseCookiesFeature = feature;
                return;
            }
            if (key == IItemsFeatureType)
            {
                _currentIItemsFeature = feature;
                return;
            }
            if (key == ITlsConnectionFeatureType)
            {
                _currentITlsConnectionFeature = feature;
                return;
            }
            if (key == IHttpWebSocketFeatureType)
            {
                _currentIHttpWebSocketFeature = feature;
                return;
            }
            if (key == ISessionFeatureType)
            {
                _currentISessionFeature = feature;
                return;
            }
            if (key == IHttpMaxRequestBodySizeFeatureType)
            {
                _currentIHttpMaxRequestBodySizeFeature = feature;
                return;
            }
            if (key == IHttpMinRequestBodyDataRateFeatureType)
            {
                _currentIHttpMinRequestBodyDataRateFeature = feature;
                return;
            }
            if (key == IHttpMinResponseDataRateFeatureType)
            {
                _currentIHttpMinResponseDataRateFeature = feature;
                return;
            }
            if (key == IHttpBodyControlFeatureType)
            {
                _currentIHttpBodyControlFeature = feature;
                return;
            }
            if (key == IHttpSendFileFeatureType)
            {
                _currentIHttpSendFileFeature = feature;
                return;
            };
            ExtraFeatureSet(key, feature);
        }

        private IEnumerable<KeyValuePair<Type, object>> FastEnumerable()
        {
            if (_currentIHttpRequestFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpRequestFeatureType, _currentIHttpRequestFeature as global::Microsoft.AspNetCore.Http.Features.IHttpRequestFeature);
            }
            if (_currentIHttpResponseFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpResponseFeatureType, _currentIHttpResponseFeature as global::Microsoft.AspNetCore.Http.Features.IHttpResponseFeature);
            }
            if (_currentIHttpRequestIdentifierFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpRequestIdentifierFeatureType, _currentIHttpRequestIdentifierFeature as global::Microsoft.AspNetCore.Http.Features.IHttpRequestIdentifierFeature);
            }
            if (_currentIServiceProvidersFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IServiceProvidersFeatureType, _currentIServiceProvidersFeature as global::Microsoft.AspNetCore.Http.Features.IServiceProvidersFeature);
            }
            if (_currentIHttpRequestLifetimeFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpRequestLifetimeFeatureType, _currentIHttpRequestLifetimeFeature as global::Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature);
            }
            if (_currentIHttpConnectionFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpConnectionFeatureType, _currentIHttpConnectionFeature as global::Microsoft.AspNetCore.Http.Features.IHttpConnectionFeature);
            }
            if (_currentIHttpAuthenticationFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpAuthenticationFeatureType, _currentIHttpAuthenticationFeature as global::Microsoft.AspNetCore.Http.Features.Authentication.IHttpAuthenticationFeature);
            }
            if (_currentIQueryFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IQueryFeatureType, _currentIQueryFeature as global::Microsoft.AspNetCore.Http.Features.IQueryFeature);
            }
            if (_currentIFormFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IFormFeatureType, _currentIFormFeature as global::Microsoft.AspNetCore.Http.Features.IFormFeature);
            }
            if (_currentIHttpUpgradeFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpUpgradeFeatureType, _currentIHttpUpgradeFeature as global::Microsoft.AspNetCore.Http.Features.IHttpUpgradeFeature);
            }
            if (_currentIResponseCookiesFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IResponseCookiesFeatureType, _currentIResponseCookiesFeature as global::Microsoft.AspNetCore.Http.Features.IResponseCookiesFeature);
            }
            if (_currentIItemsFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IItemsFeatureType, _currentIItemsFeature as global::Microsoft.AspNetCore.Http.Features.IItemsFeature);
            }
            if (_currentITlsConnectionFeature != null)
            {
                yield return new KeyValuePair<Type, object>(ITlsConnectionFeatureType, _currentITlsConnectionFeature as global::Microsoft.AspNetCore.Http.Features.ITlsConnectionFeature);
            }
            if (_currentIHttpWebSocketFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpWebSocketFeatureType, _currentIHttpWebSocketFeature as global::Microsoft.AspNetCore.Http.Features.IHttpWebSocketFeature);
            }
            if (_currentISessionFeature != null)
            {
                yield return new KeyValuePair<Type, object>(ISessionFeatureType, _currentISessionFeature as global::Microsoft.AspNetCore.Http.Features.ISessionFeature);
            }
            if (_currentIHttpMaxRequestBodySizeFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpMaxRequestBodySizeFeatureType, _currentIHttpMaxRequestBodySizeFeature as global::Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature);
            }
            if (_currentIHttpMinRequestBodyDataRateFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpMinRequestBodyDataRateFeatureType, _currentIHttpMinRequestBodyDataRateFeature as global::Microsoft.AspNetCore.Server.Kestrel.Core.Features.IHttpMinRequestBodyDataRateFeature);
            }
            if (_currentIHttpMinResponseDataRateFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpMinResponseDataRateFeatureType, _currentIHttpMinResponseDataRateFeature as global::Microsoft.AspNetCore.Server.Kestrel.Core.Features.IHttpMinResponseDataRateFeature);
            }
            if (_currentIHttpBodyControlFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpBodyControlFeatureType, _currentIHttpBodyControlFeature as global::Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature);
            }
            if (_currentIHttpSendFileFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpSendFileFeatureType, _currentIHttpSendFileFeature as global::Microsoft.AspNetCore.Http.Features.IHttpSendFileFeature);
            }

            if (MaybeExtra != null)
            {
                foreach(var item in MaybeExtra)
                {
                    yield return item;
                }
            }
        }
    }
}
