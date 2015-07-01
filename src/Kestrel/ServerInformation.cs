using System;
using Microsoft.AspNet.Builder;
using Microsoft.Framework.ConfigurationModel;
using System.Globalization;
using System.Collections.Generic;

namespace Kestrel
{
    public class ServerInformation : IServerInformation
    {
        public ServerInformation()
        {
            Addresses = new List<ServerAddress>();
        }

        public void Initialize(IConfiguration configuration)
        {
            string urls;
            if (!configuration.TryGet("server.urls", out urls))
            {
                urls = "http://+:5000/";
            }
            foreach (var url in urls.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var address = ServerAddress.FromUrl(url);
                if(address != null)
                {
                    Addresses.Add(address);
                }
            }
        }

        public string Name
        {
            get
            {
                return "Kestrel";
            }
        }

        public IList<ServerAddress> Addresses { get; private set; }
    }
}
