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

        /// <summary>
        /// Reads installed.json in either format.
        ///
        /// v1 (no "version" key) recorded a flat "addins" list with no Clarion root. Those entries
        /// land in LegacyUnclaimed rather than Installed: this process only ever sees one Clarion,
        /// so it cannot know which root a v1 entry belonged to and must not guess. Each root claims
        /// what it can prove on disk -- see InstalledAddinStore.ClaimLegacy.
        /// </summary>
        public static InstalledStore ParseStore(string json)
        {
            var store = new InstalledStore();
            if (string.IsNullOrWhiteSpace(json)) return store;

            var raw = _js.Deserialize<Dictionary<string, object>>(json);
            store.Version = raw.TryGetValue("version", out var ver) ? Convert.ToInt32(ver) : 1;

            if (store.Version >= 2)
            {
                foreach (var a in Entries(raw, "installed"))
                    store.Installed.Add(new InstalledAddin
                    {
                        Id          = S(a, "id"),
                        Root        = S(a, "root"),
                        Version     = S(a, "version"),
                        InstalledAt = S(a, "installedAt"),
                        Staged      = Bool(a, "staged"),
                    });

                foreach (var a in Entries(raw, "legacyUnclaimed"))
                    store.LegacyUnclaimed.Add(new InstalledAddin
                    {
                        Id          = S(a, "id"),
                        Version     = S(a, "version"),
                        InstalledAt = S(a, "installedAt"),
                    });
            }
            else
            {
                foreach (var a in Entries(raw, "addins"))
                    store.LegacyUnclaimed.Add(new InstalledAddin
                    {
                        Id          = S(a, "id"),
                        Version     = S(a, "version"),
                        InstalledAt = S(a, "installedAt"),
                    });
            }
            return store;
        }

        public static string SerialiseStore(InstalledStore store)
            => _js.Serialize(new
            {
                version = 2,
                installed = store.Installed.Select(a => new
                {
                    id          = a.Id,
                    root        = a.Root,
                    version     = a.Version,
                    installedAt = a.InstalledAt,
                    staged      = a.Staged,
                }).ToList(),
                legacyUnclaimed = store.LegacyUnclaimed.Select(a => new
                {
                    id          = a.Id,
                    version     = a.Version,
                    installedAt = a.InstalledAt,
                }).ToList()
            });

        private static IEnumerable<Dictionary<string, object>> Entries(Dictionary<string, object> raw, string key)
        {
            if (raw.TryGetValue(key, out var obj) && obj is System.Collections.ArrayList list)
                foreach (Dictionary<string, object> a in list)
                    yield return a;
        }

        private static RegistryAddin MapAddin(Dictionary<string, object> a) => new RegistryAddin
        {
            Id              = S(a, "id"),
            Name            = S(a, "name"),
            Description     = S(a, "description"),
            Author          = S(a, "author"),
            AuthorUrl       = S(a, "authorUrl"),
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
