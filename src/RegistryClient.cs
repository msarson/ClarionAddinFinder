using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace AddinFinder
{
    /// <summary>Fetches and parses the remote registry.json from GitHub.</summary>
    public class RegistryClient
    {
        private const string RegistryUrl =
            "https://raw.githubusercontent.com/msarson/clarion-addin-registry/main/registry.json";

        public AddinRegistry Fetch()
        {
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "ClarionAddinFinder/1.0";
                string json = wc.DownloadString(RegistryUrl);
                return SimpleJsonParser.ParseRegistry(json);
            }
        }
    }
}
