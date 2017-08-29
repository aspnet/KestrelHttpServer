// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace CodeGenerator
{
    public class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Missing path to FrameHeaders.Generated.cs");
                return 1;
            }
            else if (args.Length < 2)
            {
                Console.Error.WriteLine("Missing path to Frame.Generated.cs");
                return 1;
            }
            else if (args.Length < 3)
            {
                Console.Error.WriteLine("Missing path to HttpUtilities.Generated.cs");
                return 1;
            }

            Run(args[0], args[1], args[2]);

            return 0;
        }

        public static void Run(string knownHeadersPath, string frameFeatureCollectionPath, string httpUtilitiesPath)
        {
            var knownHeadersContent = KnownHeaders.GeneratedFile();
            var frameFeatureCollectionContent = FrameFeatureCollection.GeneratedFile(nameof(Frame));
            var httpUtilitiesContent = HttpUtilities.HttpUtilities.GeneratedFile();

            var existingKnownHeaders = File.Exists(knownHeadersPath) ? File.ReadAllText(knownHeadersPath) : "";
            if (!string.Equals(knownHeadersContent, existingKnownHeaders))
            {
                File.WriteAllText(knownHeadersPath, knownHeadersContent);
            }

            var existingFrameFeatureCollection = File.Exists(frameFeatureCollectionPath) ? File.ReadAllText(frameFeatureCollectionPath) : "";
            if (!string.Equals(frameFeatureCollectionContent, existingFrameFeatureCollection))
            {
                File.WriteAllText(frameFeatureCollectionPath, frameFeatureCollectionContent);
            }

            var existingHttpUtilities = File.Exists(httpUtilitiesPath) ? File.ReadAllText(httpUtilitiesPath) : "";
            if (!string.Equals(httpUtilitiesContent, existingHttpUtilities))
            {
                File.WriteAllText(httpUtilitiesPath, httpUtilitiesContent);
            }
        }
    }
}
