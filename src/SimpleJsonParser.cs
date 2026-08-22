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

            // Setup addins in the root registry, for a publisher who has not federated yet. Same
            // separate key, for the same reason: a build before 0.8.1 must not see them.
            foreach (var a in SetupEntries(raw, "")) registry.Addins.Add(a);

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
            foreach (var addin in SetupEntries(raw, publisherId)) result.Add(addin);
            return result;
        }

        /// <summary>
        /// Addins distributed as a setup installer, read from their own "setupAddins" key.
        ///
        /// A separate key, and not a flag on an ordinary entry, because of what builds before 0.8.1
        /// would do with one. Such an entry carries no download URLs -- the asset is renamed every
        /// release, so there is nothing to pin -- and an older client walks straight through that:
        /// the URL-ownership check passes (nothing to check), Download returns immediately on an
        /// empty URL, and MoveIntoPlace still creates the destination before copying nothing into
        /// it. The user would be left with an EMPTY folder under accessory\addins and a phantom
        /// install recorded against it. An empty folder in the scanned root is exactly the shape
        /// that has already stopped a Clarion starting.
        ///
        /// Older builds read only "addins", so putting these anywhere else makes them invisible
        /// rather than dangerous. Same reasoning as installed.v2.json and settings.v2.json: a new
        /// shape does not go where an old reader will find it and misunderstand it.
        /// </summary>
        private static IEnumerable<RegistryAddin> SetupEntries(Dictionary<string, object> raw,
                                                               string publisherId)
        {
            foreach (var a in Entries(raw, "setupAddins"))
            {
                var addin = MapAddin(a);
                addin.Publisher = publisherId;

                // Without a repository there is nothing to resolve a release from, so the entry
                // could only ever render as an addin that cannot be obtained.
                if (addin.GithubRepo.Length == 0) continue;
                yield return addin;
            }
        }

        /// <summary>
        /// Reads a GitHub /releases/latest response down to the tag and the installer asset.
        ///
        /// The asset is chosen by extension rather than by name, because the name changes every
        /// release -- which is the whole reason the URL cannot live in the registry.
        /// </summary>
        public static GithubRelease? ParseGithubRelease(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var raw = _js.Deserialize<Dictionary<string, object>>(json);

            var release = new GithubRelease { Tag = S(raw, "tag_name") };
            if (release.Tag.Length == 0) return null;

            var assets = Entries(raw, "assets").ToList();
            var chosen = assets.FirstOrDefault(a => S(a, "name").EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                      ?? assets.FirstOrDefault(a => S(a, "name").EndsWith(".msi", StringComparison.OrdinalIgnoreCase));

            // A release with exactly one asset and an unexpected extension is still almost certainly
            // the installer -- better than reporting an update the user cannot get.
            if (chosen == null && assets.Count == 1) chosen = assets[0];
            if (chosen == null) return null;

            release.AssetName = S(chosen, "name");
            release.AssetUrl  = S(chosen, "browser_download_url");
            return release;
        }

        public static Dictionary<string, GithubRelease> ParseReleaseCache(string json)
        {
            var result = new Dictionary<string, GithubRelease>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return result;

            var raw = _js.Deserialize<Dictionary<string, object>>(json);
            foreach (var e in Entries(raw, "releases"))
            {
                string repo = S(e, "repo");
                if (repo.Length == 0) continue;
                result[repo] = new GithubRelease
                {
                    Tag       = S(e, "tag"),
                    AssetUrl  = S(e, "assetUrl"),
                    AssetName = S(e, "assetName"),
                    CheckedOn = S(e, "checkedOn"),
                };
            }
            return result;
        }

        public static string SerialiseReleaseCache(Dictionary<string, GithubRelease> cache)
            => _js.Serialize(new
            {
                releases = cache.Select(pair => new
                {
                    repo      = pair.Key,
                    tag       = pair.Value.Tag,
                    assetUrl  = pair.Value.AssetUrl,
                    assetName = pair.Value.AssetName,
                    checkedOn = pair.Value.CheckedOn,
                }).ToList()
            });

        /// <summary>
        /// Reads settings in either shape into the instance, which already knows which Clarion it
        /// is for. A pre-v2 document has no per-Clarion section, so its values are adopted for that
        /// root -- see AddinFinderSettings.AdoptLegacy.
        /// </summary>
        public static void FillSettings(AddinFinderSettings settings, string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            var raw = _js.Deserialize<Dictionary<string, object>>(json);
            settings.SuppressRestartReminder = Bool(raw, "suppressRestartReminder");

            if (Int(raw, "version") >= 2)
            {
                var entries = new List<ClarionSettings>();
                foreach (var c in Entries(raw, "perClarion"))
                    entries.Add(new ClarionSettings
                    {
                        Root                   = S(c, "root"),
                        AcceptedTermsVersion   = Int(c, "acceptedTermsVersion"),
                        LastSeenVersion        = S(c, "lastSeenVersion"),
                        AcknowledgedPublishers = StrList(c, "acknowledgedPublishers"),
                    });
                settings.SetPerClarion(entries);
                return;
            }

            settings.AdoptLegacy(Int(raw, "acceptedTermsVersion"),
                                 S(raw, "lastSeenVersion"),
                                 StrList(raw, "acknowledgedPublishers"));
        }

        public static string SerialiseSettings(AddinFinderSettings settings,
                                               List<ClarionSettings> perClarion)
            => _js.Serialize(new
            {
                version                 = 2,
                suppressRestartReminder = settings.SuppressRestartReminder,
                perClarion = perClarion.Select(c => new
                {
                    root                   = c.Root,
                    acceptedTermsVersion   = c.AcceptedTermsVersion,
                    lastSeenVersion        = c.LastSeenVersion,
                    acknowledgedPublishers = c.AcknowledgedPublishers,
                }).ToList()
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
            githubRepo      = a.GithubRepo,
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
            GithubRepo      = S(a, "githubRepo"),
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
