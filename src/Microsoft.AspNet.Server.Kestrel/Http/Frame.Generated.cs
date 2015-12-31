
using System;
using System.Collections.Generic;
using Microsoft.AspNet.Http.Internal;

namespace Microsoft.AspNet.Server.Kestrel.Http 
{
    public partial class Frame
    {
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

        private ContextFeatures _currentContextFeatures;
        private ConnectionFeatures _currentConnectionFeatures;
        private RequestFeatures _currentRequestFeatures;
        private ResponseFeatures _currentResponseFeatures;
        private WebsocketFeatures _currentWebsocketFeatures;
        private object _currentIHttpUpgradeFeature;
        private object _currentIHttpSendFileFeature;

        private void FastReset()
        {
            _currentRequestFeatures = new RequestFeatures() { Request = this };
            _currentResponseFeatures = new ResponseFeatures() { Response = this };
            _currentContextFeatures = new ContextFeatures() { Lifetime = this };
            _currentConnectionFeatures = new ConnectionFeatures() { Connection = this };
            _currentWebsocketFeatures = new WebsocketFeatures() { Request = this };

            _currentIHttpUpgradeFeature = this;
            _currentIHttpSendFileFeature = null;
        }

        private TFeature FastFeatureGet<TFeature>()
        {
            if (typeof(TFeature) == typeof(ContextFeatures))
            {
                return (TFeature)(object)_currentContextFeatures;
            }
            else if (typeof(TFeature) == typeof(ConnectionFeatures))
            {
                return (TFeature)(object)_currentConnectionFeatures;
            }
            else if (typeof(TFeature) == typeof(RequestFeatures))
            {
                return (TFeature)(object)_currentRequestFeatures;
            }
            else if (typeof(TFeature) == typeof(ResponseFeatures))
            {
                return (TFeature)(object)_currentResponseFeatures;
            }
            else if (typeof(TFeature) == typeof(WebsocketFeatures))
            {
                return (TFeature)(object)_currentWebsocketFeatures;
            }
            return (TFeature)FastFeatureGet(typeof(TFeature));
        }

        private object FastFeatureGet(Type key)
        {
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestFeature))
            {
                return _currentRequestFeatures.Request;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpResponseFeature))
            {
                return _currentResponseFeatures.Response;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestIdentifierFeature))
            {
                return _currentContextFeatures.RequestIdentifier;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IServiceProvidersFeature))
            {
                return _currentContextFeatures.ServiceProviders;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestLifetimeFeature))
            {
                return _currentContextFeatures.Lifetime;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpConnectionFeature))
            {
                return _currentConnectionFeatures.Connection;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Authentication.IHttpAuthenticationFeature))
            {
                return _currentContextFeatures.Authentication;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IQueryFeature))
            {
                return _currentRequestFeatures.Query;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IFormFeature))
            {
                return _currentRequestFeatures.Form;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpUpgradeFeature))
            {
                return _currentIHttpUpgradeFeature;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IResponseCookiesFeature))
            {
                return _currentResponseFeatures.Cookies;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IItemsFeature))
            {
                return _currentContextFeatures.Items;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.ITlsConnectionFeature))
            {
                return _currentConnectionFeatures.TlsConnection;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpWebSocketFeature))
            {
                return _currentWebsocketFeatures.WebSockets;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.ISessionFeature))
            {
                return _currentContextFeatures.Session;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IRequestCookiesFeature))
            {
                return _currentRequestFeatures.Cookies;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpSendFileFeature))
            {
                return _currentIHttpSendFileFeature;
            }
            return ExtraFeatureGet(key);
        }

        private void FastFeatureSet<TFeature>(TFeature instance)
        {
            FastFeatureSet(typeof(TFeature), instance);
        }

        private void FastFeatureSet(Type key, object feature)
        {
            _featureRevision++;
            
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestFeature))
            {
                _currentRequestFeatures.Request = (IHttpRequestFeature)feature;
                _currentWebsocketFeatures.Request = (IHttpRequestFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpResponseFeature))
            {
                _currentResponseFeatures.Response = (IHttpResponseFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestIdentifierFeature))
            {
                _currentContextFeatures.RequestIdentifier = (IHttpRequestIdentifierFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IServiceProvidersFeature))
            {
                _currentContextFeatures.ServiceProviders = (IServiceProvidersFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpRequestLifetimeFeature))
            {
                _currentContextFeatures.Lifetime = (IHttpRequestLifetimeFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpConnectionFeature))
            {
                _currentConnectionFeatures.Connection = (IHttpConnectionFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Authentication.IHttpAuthenticationFeature))
            {
                _currentContextFeatures.Authentication = (IHttpAuthenticationFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IQueryFeature))
            {
                _currentRequestFeatures.Query = (IQueryFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IFormFeature))
            {
                _currentRequestFeatures.Form = (IFormFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpUpgradeFeature))
            {
                _currentIHttpUpgradeFeature = feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IResponseCookiesFeature))
            {
                _currentResponseFeatures.Cookies = (IResponseCookiesFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IItemsFeature))
            {
                _currentContextFeatures.Items = (IItemsFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.ITlsConnectionFeature))
            {
                _currentConnectionFeatures.TlsConnection = (ITlsConnectionFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpWebSocketFeature))
            {
                _currentWebsocketFeatures.WebSockets = (IHttpWebSocketFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.ISessionFeature))
            {
                _currentContextFeatures.Session = (ISessionFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.Internal.IRequestCookiesFeature))
            {
                _currentRequestFeatures.Cookies = (IRequestCookiesFeature)feature;
                return;
            }
            if (key == typeof(global::Microsoft.AspNet.Http.Features.IHttpSendFileFeature))
            {
                _currentIHttpSendFileFeature = feature;
                return;
            };
            ExtraFeatureSet(key, feature);
        }

        private IEnumerable<KeyValuePair<Type, object>> FastEnumerable()
        {
            if (_currentRequestFeatures.Request != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpRequestFeatureType, _currentRequestFeatures.Request as global::Microsoft.AspNet.Http.Features.IHttpRequestFeature);
            }
            if (_currentResponseFeatures.Response != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpResponseFeatureType, _currentResponseFeatures.Response as global::Microsoft.AspNet.Http.Features.IHttpResponseFeature);
            }
            if (_currentContextFeatures.RequestIdentifier != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpRequestIdentifierFeatureType, _currentContextFeatures.RequestIdentifier as global::Microsoft.AspNet.Http.Features.IHttpRequestIdentifierFeature);
            }
            if (_currentContextFeatures.ServiceProviders != null)
            {
                yield return new KeyValuePair<Type, object>(IServiceProvidersFeatureType, _currentContextFeatures.ServiceProviders as global::Microsoft.AspNet.Http.Features.Internal.IServiceProvidersFeature);
            }
            if (_currentContextFeatures.Lifetime != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpRequestLifetimeFeatureType, _currentContextFeatures.Lifetime as global::Microsoft.AspNet.Http.Features.IHttpRequestLifetimeFeature);
            }
            if (_currentConnectionFeatures.Connection != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpConnectionFeatureType, _currentConnectionFeatures.Connection as global::Microsoft.AspNet.Http.Features.IHttpConnectionFeature);
            }
            if (_currentContextFeatures.Authentication != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpAuthenticationFeatureType, _currentContextFeatures.Authentication as global::Microsoft.AspNet.Http.Features.Authentication.IHttpAuthenticationFeature);
            }
            if (_currentRequestFeatures.Query != null)
            {
                yield return new KeyValuePair<Type, object>(IQueryFeatureType, _currentRequestFeatures.Query as global::Microsoft.AspNet.Http.Features.Internal.IQueryFeature);
            }
            if (_currentRequestFeatures.Form != null)
            {
                yield return new KeyValuePair<Type, object>(IFormFeatureType, _currentRequestFeatures.Form as global::Microsoft.AspNet.Http.Features.Internal.IFormFeature);
            }
            if (_currentIHttpUpgradeFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpUpgradeFeatureType, _currentIHttpUpgradeFeature as global::Microsoft.AspNet.Http.Features.IHttpUpgradeFeature);
            }
            if (_currentResponseFeatures.Cookies != null)
            {
                yield return new KeyValuePair<Type, object>(IResponseCookiesFeatureType, _currentResponseFeatures.Cookies as global::Microsoft.AspNet.Http.Features.Internal.IResponseCookiesFeature);
            }
            if (_currentContextFeatures.Items != null)
            {
                yield return new KeyValuePair<Type, object>(IItemsFeatureType, _currentContextFeatures.Items as global::Microsoft.AspNet.Http.Features.Internal.IItemsFeature);
            }
            if (_currentConnectionFeatures.TlsConnection != null)
            {
                yield return new KeyValuePair<Type, object>(ITlsConnectionFeatureType, _currentConnectionFeatures.TlsConnection as global::Microsoft.AspNet.Http.Features.ITlsConnectionFeature);
            }
            if (_currentWebsocketFeatures.WebSockets != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpWebSocketFeatureType, _currentWebsocketFeatures.WebSockets as global::Microsoft.AspNet.Http.Features.IHttpWebSocketFeature);
            }
            if (_currentContextFeatures.Session != null)
            {
                yield return new KeyValuePair<Type, object>(ISessionFeatureType, _currentContextFeatures.Session as global::Microsoft.AspNet.Http.Features.ISessionFeature);
            }
            if (_currentRequestFeatures.Cookies != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpSendFileFeatureType, _currentRequestFeatures.Cookies as global::Microsoft.AspNet.Http.Features.Internal.IRequestCookiesFeature);
            }
            if (_currentIHttpSendFileFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IHttpSendFileFeatureType, _currentIHttpSendFileFeature as global::Microsoft.AspNet.Http.Features.IHttpSendFileFeature);
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
