// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Microsoft.AspNetCore.Testing
{
    public static class StringExtensions
    {
        private static IEnumerable<char> InvalidFileChars =
            Path.GetInvalidPathChars()
            .Union(Path.GetInvalidFileNameChars())
            .Union(new char[] {
                ' ', // space
                '\\', // back slash
                '/', // forward slash
                '\x7F', // delete
            });

        public static string EscapeNonPrintable(this string s)
        {
            var ellipsis = s.Length > 128
                ? "..."
                : string.Empty;
            return s.Substring(0, Math.Min(128, s.Length))
                .Replace("\r", @"\x0D")
                .Replace("\n", @"\x0A")
                .Replace("\0", @"\x00")
                + ellipsis;
        }

        public static string RemoveIllegalFileChars(this string s)
        {
            var sb = new StringBuilder();

            foreach (var c in s)
            {
                sb.Append(InvalidFileChars.Contains(c) ? '-' : c);
            }
            return sb.ToString();
        }

        public static string ShortenTestName(this string s)
        {
            return s.Substring(0, Math.Min(s.Length, 120));
        }

        public static string GetFileFriendlyString(this string s)
        {
            if (s == null)
            {
                return "null";
            }
            if (s == string.Empty)
            {
                return "empty";
            }
            return s;
        }

        public static string GetFileFriendlyString(this string[] s)
        {
            return s.Aggregate((a, b) => a.GetFileFriendlyString() + ";" + b.GetFileFriendlyString());
        }
    }
}