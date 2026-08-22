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

            // The legacy flat list. Kept while publishers migrate to their own files, and read by
            // builds that predate federation -- which is why the root file keeps this key rather
            // than the format changing under them.
            foreach (var a in Entries(raw, "addins"))
                registry.Addins.Add(MapAddin(a));

            foreach (var p in Entries(raw, "publishers"))
                registry.Publishers.Add(new Publisher
                {
                    Id         = S(p, "id"),
                    Name       = S(p, "name"),
                    Repo       = S(p, "repo"),
                    Branch     = S(p, "branch"),
                    Status     = Or(S(p, "status"), PublisherStatus.Active),
                    StatusNote = S(p, "statusNote"),
                });

            registry.Revoked = StrList(raw, "revoked");
            return registry;
        }

        /// <summary>
        /// Reads one publisher's own addins.json. Entry format is identical to the legacy list, so a
        /// publisher migrating copies their entries across unchanged. Every addin is stamped with the
        /// publisher it came from -- provenance has to survive from here through to install.
        /// </summary>
        public static List<RegistryAddin> ParsePublisherAddins(string json, string publisherId)
        {
            var result = new List<RegistryAddin>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            var raw = _js.Deserialize<Dictionary<string, object>>(json);
            foreach (var a in Entries(raw, "addins"))
            {
                var addin = MapAddin(a);
                addin.Publisher = publisherId;
                result.Add(addin);
            }
            return result;
        }

        public static AddinFinderSettings ParseSettings(string json)
        {
            var s = new AddinFinderSettings();
            if (string.IsNullOrWhiteSpace(json)) return s;

            var raw = _js.Deserialize<Dictionary<string, object>>(json);
            s.SuppressRestartReminder = Bool(raw, "suppressRestartReminder");
            s.AcceptedTermsVersion    = Int(raw, "acceptedTermsVersion");
            s.AcknowledgedPublishers  = StrList(raw, "acknowledgedPublishers");
            return s;
        }

        public static string SerialiseSettings(AddinFinderSettings s)
            => _js.Serialize(new
            {
                suppressRestartReminder = s.SuppressRestartReminder,
                acceptedTermsVersion    = s.AcceptedTermsVersion,
                acknowledgedPublishers  = s.AcknowledgedPublishers,
            });

        public static List<PublisherHealthEntry> ParsePublisherHealth(string json)
        {
            var result = new List<PublisherHealthEntry>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            var raw = _js.Deserialize<Dictionary<string, object>>(json);
            foreach (var e in Entries(raw, "publishers"))
                result.Add(new PublisherHealthEntry
                {
                    Id                  = S(e, "id"),
                    LastOutcome         = S(e, "lastOutcome"),
                    LastSuccess         = S(e, "lastSuccess"),
                    NotFoundSince       = S(e, "notFoundSince"),
                    ConsecutiveNotFound = Int(e, "consecutiveNotFound"),
                });
            return result;
        }

        public static string SerialisePublisherHealth(List<PublisherHealthEntry> entries)
            => _js.Serialize(new
            {
                publishers = entries.Select(e => new
                {
                    id                  = e.Id,
                    lastOutcome         = e.LastOutcome,
                    lastSuccess         = e.LastSuccess,
                    notFoundSince       = e.NotFoundSince,
                    consecutiveNotFound = e.ConsecutiveNotFound,
                }).ToList()
            });

        public static Dictionary<string, List<RegistryAddin>> ParseRegistryCache(string json)
        {
            var result = new Dictionary<string, List<RegistryAddin>>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            var raw = _js.Deserialize<Dictionary<string, object>>(json);
            foreach (var p in Entries(raw, "publishers"))
            {
                string id = S(p, "id");
                if (id.Length == 0) continue;
                var list = new List<RegistryAddin>();
                foreach (var a in Entries(p, "addins"))
                {
                    var addin = MapAddin(a);
                    addin.Publisher = id;
                    list.Add(addin);
                }
                result[id] = list;
            }
            return result;
        }

        public static string SerialiseRegistryCache(Dictionary<string, List<RegistryAddin>> byPublisher)
            => _js.Serialize(new
            {
                publishers = byPublisher.Select(pair => new
                {
                    id     = pair.Key,
                    addins = pair.Value.Select(SerialisableAddin).ToList()
                }).ToList()
            });

        private static object SerialisableAddin(RegistryAddin a) => new
        {
            id              = a.Id,
            name            = a.Name,
            description     = a.Description,
            author          = a.Author,
            authorUrl       = a.AuthorUrl,
            license         = a.License,
            category        = a.Category,
            version         = a.Version,
            targetFramework = a.TargetFramework,
            downloadUrls    = a.DownloadUrls,
            downloadZipUrl  = a.DownloadZipUrl,
            addinFileUrl    = a.AddinFileUrl,
            homepageUrl     = a.HomepageUrl,
            changelogUrl    = a.ChangelogUrl,
            fork            = a.Fork,
            upstreamUrl     = a.UpstreamUrl,
            status          = a.Status,
            statusNote      = a.StatusNote,
            replacedBy      = a.ReplacedBy,
        };

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
                        Publisher   = S(a, "publisher"),
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
                    publisher   = a.Publisher,
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
            Status          = Or(S(a, "status"), AddinLifecycle.Active),
            StatusNote      = S(a, "statusNote"),
            ReplacedBy      = S(a, "replacedBy"),
        };

        private static string Or(string value, string fallback)
            => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

        private static int Int(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

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
