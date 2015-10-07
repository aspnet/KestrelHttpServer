
using System;
using System.Collections.Generic;

namespace Microsoft.AspNet.Server.Kestrel.Http 
{
    public partial class Frame
    {
        private const int flagIHttpRequestFeature = 1;
        private const int flagIHttpResponseFeature = 2;
        private const int flagIHttpUpgradeFeature = 4;
        
        private static readonly Type IHttpRequestFeatureType = typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestFeature);
        private static readonly Type IHttpResponseFeatureType = typeof(global::Microsoft.AspNet.Http.Features.IHttpResponseFeature);
        private static readonly Type IHttpRequestIdentifierFeatureType = typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestIdentifierFeature);
        private static readonly Type IServiceProvidersFeatureType = typeof(global::Microsoft.AspNet.Http.Features.Internal.IServiceProvidersFeature);
        private static readonly Type IHttpRequestLifetimeFeatureType = typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestLifetimeFeature);
        private static readonly Type IHttpConnectionFeatureType = typeof(global::Microsoft.AspNet.Http.Features.IHttpConnectionFeature);
        private static readonly Type IHttpAuthenticationFeatureType = typeof(global::Microsoft.AspNet.Http.Features.Authentication.IHttpAuthenticationFeature);
        private static readonly Type IQueryFeatureType = typeof(global::Microsoft.AspNet.Http.Features.Internal.IQueryFeature);
        private static readonly Type IFormFeatureType = typeof(global::Microsoft.AspNet.Http.Features.Internal.IFormFeature);
        private static readonly Type IHttpUpgradeFeatureType = typeof(global::Microsoft.AspNet.Http.Features.IHttpUpgradeFeature);
        private static readonly Type IResponseCookiesFeatureType = typeof(global::Microsoft.AspNet.Http.Features.Internal.IResponseCookiesFeature);
        private static readonly Type IItemsFeatureType = typeof(global::Microsoft.AspNet.Http.Features.Internal.IItemsFeature);
        private static readonly Type ITlsConnectionFeatureType = typeof(global::Microsoft.AspNet.Http.Features.ITlsConnectionFeature);
        private static readonly Type IHttpWebSocketFeatureType = typeof(global::Microsoft.AspNet.Http.Features.IHttpWebSocketFeature);
        private static readonly Type ISessionFeatureType = typeof(global::Microsoft.AspNet.Http.Features.ISessionFeature);
        private static readonly Type IHttpSendFileFeatureType = typeof(global::Microsoft.AspNet.Http.Features.IHttpSendFileFeature);
        
        private object _currentIHttpRequestIdentifierFeature;
        private object _currentIServiceProvidersFeature;
        private object _currentIHttpRequestLifetimeFeature;
        private object _currentIHttpConnectionFeature;
        private object _currentIHttpAuthenticationFeature;
        private object _currentIQueryFeature;
        private object _currentIFormFeature;

        private int _featureOverridenFlags = 0;

        private void FastReset()
        {
            _featureOverridenFlags = 0;
            
            _currentIHttpRequestIdentifierFeature = null;
            _currentIServiceProvidersFeature = null;
            _currentIHttpRequestLifetimeFeature = null;
            _currentIHttpConnectionFeature = null;
            _currentIHttpAuthenticationFeature = null;
            _currentIQueryFeature = null;
            _currentIFormFeature = null;
        }

        private object FastFeatureGet(Type key)
        {
            if (key == IHttpRequestFeatureType)
            {
                if ((_featureOverridenFlags & flagIHttpRequestFeature) == 0)
                {
                    return this;
                }
                return SlowFeatureGet(key);
            }
            if (key == IHttpResponseFeatureType)
            {
                if ((_featureOverridenFlags & flagIHttpResponseFeature) == 0)
                {
                    return this;
                }
                return SlowFeatureGet(key);
            }
            if (key == IHttpUpgradeFeatureType)
            {
                if ((_featureOverridenFlags & flagIHttpUpgradeFeature) == 0)
                {
                    return this;
                }
                return SlowFeatureGet(key);
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
            return  SlowFeatureGet(key);
        }

        private object SlowFeatureGet(Type key)
        {
            if (MaybeExtra == null) 
            {
                return null;
            }
            for (var i = 0; i < MaybeExtra.Count; i++)
            {
                var kv = MaybeExtra[i];
                if (kv.Key == key)
                {
                    return kv.Value;
                }
            }
            return null;
        }

        private void FastFeatureSetInner(int flag, Type key, object feature)
        {
            SetExtra(key, feature);
            _featureOverridenFlags |= flag;
        }

        private void FastFeatureSet(Type key, object feature)
        {
            _featureRevision++;
            
            if (key == IHttpRequestFeatureType)
            {
                FastFeatureSetInner(flagIHttpRequestFeature, key, feature);
                return;
            }
            if (key == IHttpResponseFeatureType)
            {
                FastFeatureSetInner(flagIHttpResponseFeature, key, feature);
                return;
            }
            if (key == IHttpUpgradeFeatureType)
            {
                FastFeatureSetInner(flagIHttpUpgradeFeature, key, feature);
                return;
            };
            
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
            };
            SetExtra(key, feature);
        }

        private IEnumerable<KeyValuePair<Type, object>> FastEnumerable()
        {
            if ((_featureOverridenFlags & flagIHttpRequestFeature) == 0)
            {
                yield return new KeyValuePair<Type, object>(IHttpRequestFeatureType, this as global::Microsoft.AspNet.Http.Features.IHttpRequestFeature);
            }
            else
            {
                var feature = SlowFeatureGet(IHttpRequestFeatureType);
                if (feature != null)
                {
                    yield return new KeyValuePair<Type, object>(IHttpRequestFeatureType, feature as global::Microsoft.AspNet.Http.Features.IHttpRequestFeature);
                }
            }
            if ((_featureOverridenFlags & flagIHttpResponseFeature) == 0)
            {
                yield return new KeyValuePair<Type, object>(IHttpResponseFeatureType, this as global::Microsoft.AspNet.Http.Features.IHttpResponseFeature);
            }
            else
            {
                var feature = SlowFeatureGet(IHttpResponseFeatureType);
                if (feature != null)
                {
                    yield return new KeyValuePair<Type, object>(IHttpResponseFeatureType, feature as global::Microsoft.AspNet.Http.Features.IHttpResponseFeature);
                }
            }
            if ((_featureOverridenFlags & flagIHttpUpgradeFeature) == 0)
            {
                yield return new KeyValuePair<Type, object>(IHttpUpgradeFeatureType, this as global::Microsoft.AspNet.Http.Features.IHttpUpgradeFeature);
            }
            else
            {
                var feature = SlowFeatureGet(IHttpUpgradeFeatureType);
                if (feature != null)
                {
                    yield return new KeyValuePair<Type, object>(IHttpUpgradeFeatureType, feature as global::Microsoft.AspNet.Http.Features.IHttpUpgradeFeature);
                }
            };

            
            if (_currentIHttpRequestIdentifierFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpRequestIdentifierFeatureType, _currentIHttpRequestIdentifierFeature as global::Microsoft.AspNet.Http.Features.IHttpRequestIdentifierFeature);
            }
            if (_currentIServiceProvidersFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IServiceProvidersFeatureType, _currentIServiceProvidersFeature as global::Microsoft.AspNet.Http.Features.Internal.IServiceProvidersFeature);
            }
            if (_currentIHttpRequestLifetimeFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpRequestLifetimeFeatureType, _currentIHttpRequestLifetimeFeature as global::Microsoft.AspNet.Http.Features.IHttpRequestLifetimeFeature);
            }
            if (_currentIHttpConnectionFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpConnectionFeatureType, _currentIHttpConnectionFeature as global::Microsoft.AspNet.Http.Features.IHttpConnectionFeature);
            }
            if (_currentIHttpAuthenticationFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpAuthenticationFeatureType, _currentIHttpAuthenticationFeature as global::Microsoft.AspNet.Http.Features.Authentication.IHttpAuthenticationFeature);
            }
            if (_currentIQueryFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IQueryFeatureType, _currentIQueryFeature as global::Microsoft.AspNet.Http.Features.Internal.IQueryFeature);
            }
            if (_currentIFormFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IFormFeatureType, _currentIFormFeature as global::Microsoft.AspNet.Http.Features.Internal.IFormFeature);
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
