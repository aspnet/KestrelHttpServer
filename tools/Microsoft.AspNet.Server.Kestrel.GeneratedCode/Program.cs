using System;
using System.IO;

namespace Microsoft.AspNet.Server.Kestrel.GeneratedCode
{
    public class Program
    {
        public int Main(string[] args)
        {
            var text0 = KnownHeaders.GeneratedFile();
            var text1 = FrameFeatureCollection.GeneratedFile();
            var text2 = KnownHeaders.GeneratedMemoryPoolIterator2();

            if (args.Length == 1)
            {
                var existing = File.Exists(args[0]) ? File.ReadAllText(args[0]) : "";
                if (!string.Equals(text0, existing))
                {
                    File.WriteAllText(args[0], text0);
                }
            }
            else if (args.Length == 2)
            {
                var existing0 = File.Exists(args[0]) ? File.ReadAllText(args[0]) : "";
                if (!string.Equals(text0, existing0))
                {
                    File.WriteAllText(args[0], text0);
                }

                var existing1 = File.Exists(args[1]) ? File.ReadAllText(args[1]) : "";
                if (!string.Equals(text1, existing1))
                {
                    File.WriteAllText(args[1], text1);
                }
            }
            else if (args.Length == 3)
            {
                var existing0 = File.Exists(args[0]) ? File.ReadAllText(args[0]) : "";
                if (!string.Equals(text0, existing0))
                {
                    File.WriteAllText(args[0], text0);
                }

                var existing1 = File.Exists(args[1]) ? File.ReadAllText(args[1]) : "";
                if (!string.Equals(text1, existing1))
                {
                    File.WriteAllText(args[1], text1);
                }

                var existing2 = File.Exists(args[2]) ? File.ReadAllText(args[2]) : "";
                if (!string.Equals(text2, existing2))
                {
                    File.WriteAllText(args[2], text2);
                }
            }
            else
            {
                Console.WriteLine(text0);
            }
            return 0;
        }
    }
}
