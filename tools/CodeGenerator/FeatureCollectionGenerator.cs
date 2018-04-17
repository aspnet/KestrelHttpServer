// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeGenerator
{
    public static class FeatureCollectionGenerator
    {
        public static string GenerateFile(string namespaceName, string className, string[] allFeatures, string[] implementedFeatures, string extraUsings)
        {
            // NOTE: This list MUST always match the set of feature interfaces implemented by TransportConnection.
            // See also: src/Kestrel/Http/TransportConnection.FeatureCollection.cs
            var features = allFeatures.Select((type, index) => new KnownFeature
            {
                Name = type,
                Index = index
            });

            return $@"// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
{extraUsings}

namespace {namespaceName}
{{
    public partial class {className}
    {{{Each(features, feature => $@"
        private static readonly Type {feature.Name}Type = typeof({feature.Name});")}
{Each(features, feature => $@"
        private object _current{feature.Name};")}

        private int _featureRevision;

        private List<KeyValuePair<Type, object>> MaybeExtra;

        private void FastReset()
        {{{Each(implementedFeatures, feature => $@"
            _current{feature} = this;")}
            {Each(allFeatures.Where(f => !implementedFeatures.Contains(f)), feature => $@"
            _current{feature} = null;")}
        }}

        // Internal for testing
        internal void ResetFeatureCollection()
        {{
            FastReset();
            MaybeExtra?.Clear();
            _featureRevision++;
        }}

        private object ExtraFeatureGet(Type key)
        {{
            if (MaybeExtra == null)
            {{
                return null;
            }}
            for (var i = 0; i < MaybeExtra.Count; i++)
            {{
                var kv = MaybeExtra[i];
                if (kv.Key == key)
                {{
                    return kv.Value;
                }}
            }}
            return null;
        }}

        private void ExtraFeatureSet(Type key, object value)
        {{
            if (MaybeExtra == null)
            {{
                MaybeExtra = new List<KeyValuePair<Type, object>>(2);
            }}

            for (var i = 0; i < MaybeExtra.Count; i++)
            {{
                if (MaybeExtra[i].Key == key)
                {{
                    MaybeExtra[i] = new KeyValuePair<Type, object>(key, value);
                    return;
                }}
            }}
            MaybeExtra.Add(new KeyValuePair<Type, object>(key, value));
        }}

        private object this[Type key]
        {{
            get
            {{
                object feature = null;{Each(features, feature => $@"
                {(feature.Index != 0 ? "else " : "")}if (key == {feature.Name}Type)
                {{
                    feature = _current{feature.Name};
                }}")}
                else if (MaybeExtra != null)
                {{
                    feature = ExtraFeatureGet(key);
                }}

                return feature;
            }}

            set
            {{
                _featureRevision++;
                {Each(features, feature => $@"
                {(feature.Index != 0 ? "else " : "")}if (key == {feature.Name}Type)
                {{
                    _current{feature.Name} = value;
                }}")}
                else
                {{
                    ExtraFeatureSet(key, value);
                }}
            }}
        }}

        private TFeature Get<TFeature>()
        {{
            TFeature feature = default;{Each(features, feature => $@"
            {(feature.Index != 0 ? "else " : "")}if (typeof(TFeature) == typeof({feature.Name}))
            {{
                feature = (TFeature)_current{feature.Name};
            }}")}
            else if (MaybeExtra != null)
            {{
                feature = (TFeature)(ExtraFeatureGet(typeof(TFeature)));
            }}

            return feature;
        }}

        private void Set<TFeature>(TFeature feature) 
        {{
            _featureRevision++;{Each(features, feature => $@"
            {(feature.Index != 0 ? "else " : "")}if (typeof(TFeature) == typeof({feature.Name}))
            {{
                _current{feature.Name} = feature;
            }}")}
            else
            {{
                ExtraFeatureSet(typeof(TFeature), feature);
            }}
        }}

        private IEnumerable<KeyValuePair<Type, object>> FastEnumerable()
        {{{Each(features, feature => $@"
            if (_current{feature.Name} != null)
            {{
                yield return new KeyValuePair<Type, object>({feature.Name}Type, _current{feature.Name});
            }}")}

            if (MaybeExtra != null)
            {{
                foreach (var item in MaybeExtra)
                {{
                    yield return item;
                }}
            }}
        }}
    }}
}}
";
        }

        static string Each<T>(IEnumerable<T> values, Func<T, string> formatter)
        {
            return values.Any() ? values.Select(formatter).Aggregate((a, b) => a + b) : "";
        }

        private class KnownFeature
        {
            public string Name;
            public int Index;
        }
    }
}
