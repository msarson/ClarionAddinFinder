using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace AddinFinder
{
    /// <summary>
    /// Thin wrapper around JavaScriptSerializer (available in net48 via System.Web.Extensions).
    /// Avoids a Newtonsoft.Json dependency.
    /// </summary>
    internal static class SimpleJsonParser
    {
        private static readonly JavaScriptSerializer _js = new JavaScriptSerializer();

        public static AddinRegistry ParseRegistry(string json)
        {
            var raw = _js.Deserialize<Dictionary<string, object>>(json);
            var registry = new AddinRegistry
            {
                Version = raw.TryGetValue("version", out var ver) ? Convert.ToInt32(ver) : 1,
                Updated = raw.TryGetValue("updated", out var upd) ? upd?.ToString() ?? "" : "",
            };

            if (raw.TryGetValue("addins", out var addinsObj) &&
                addinsObj is System.Collections.ArrayList addinsList)
            {
                foreach (Dictionary<string, object> a in addinsList)
                    registry.Addins.Add(MapAddin(a));
            }
            return registry;
        }

        public static List<InstalledAddin> ParseInstalled(string json)
        {
            var result = new List<InstalledAddin>();
            if (string.IsNullOrWhiteSpace(json)) return result;
            var raw = _js.Deserialize<Dictionary<string, object>>(json);
            if (raw.TryGetValue("addins", out var obj) &&
                obj is System.Collections.ArrayList list)
            {
                foreach (Dictionary<string, object> a in list)
                    result.Add(new InstalledAddin
                    {
                        Id          = S(a, "id"),
                        Version     = S(a, "version"),
                        InstalledAt = S(a, "installedAt"),
                    });
            }
            return result;
        }

        public static string SerialiseInstalled(List<InstalledAddin> addins)
            => _js.Serialize(new
            {
                addins = addins.Select(a => new
                {
                    id          = a.Id,
                    version     = a.Version,
                    installedAt = a.InstalledAt,
                }).ToList()
            });

        private static RegistryAddin MapAddin(Dictionary<string, object> a) => new RegistryAddin
        {
            Id              = S(a, "id"),
            Name            = S(a, "name"),
            Description     = S(a, "description"),
            Author          = S(a, "author"),
            License         = S(a, "license"),
            Category        = S(a, "category"),
            Version         = S(a, "version"),
            TargetFramework = S(a, "targetFramework"),
            DownloadUrls    = StrList(a, "downloadUrls"),
            DownloadZipUrl  = S(a, "downloadZipUrl"),
            AddinFileUrl    = S(a, "addinFileUrl"),
            HomepageUrl     = S(a, "homepageUrl"),
            ChangelogUrl    = S(a, "changelogUrl"),
            Fork            = Bool(a, "fork"),
            UpstreamUrl     = S(a, "upstreamUrl"),
        };

        private static string S(Dictionary<string, object> d, string key)
            => d.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

        private static bool Bool(Dictionary<string, object> d, string key)
            => d.TryGetValue(key, out var v) && v is bool b && b;

        private static List<string> StrList(Dictionary<string, object> d, string key)
        {
            var result = new List<string>();
            if (d.TryGetValue(key, out var v) && v is System.Collections.ArrayList list)
                foreach (var item in list)
                    if (item != null) result.Add(item.ToString());
            return result;
        }
    }
}
