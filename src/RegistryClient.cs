using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace AddinFinder
{
    /// <summary>What a refresh produced, including how each publisher fared.</summary>
    public class RegistryResult
    {
        public List<RegistryAddin> Addins     { get; set; } = new List<RegistryAddin>();
        public List<Publisher>     Publishers { get; set; } = new List<Publisher>();

        /// <summary>Publisher id -> how its list fetch went this refresh.</summary>
        public Dictionary<string, FetchOutcome> Outcomes { get; set; } =
            new Dictionary<string, FetchOutcome>();

        /// <summary>Publisher ids whose repeated 404s now read as a withdrawn list.</summary>
        public HashSet<string> PresumedWithdrawn { get; set; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True if the root registry itself could not be read.</summary>
        public bool RootFetchFailed { get; set; }
    }

    /// <summary>Fetches the root registry and each publisher's own addin list.</summary>
    public class RegistryClient
    {
        private const string RegistryUrl =
            "https://raw.githubusercontent.com/msarson/clarion-addin-registry/master/registry.json";

        private static string DefaultStoreDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClarionAddinFinder");

        private readonly RegistryCache   _cache;
        private readonly PublisherHealth _health;
        private readonly GithubReleases  _releases;

        public RegistryClient() : this(DefaultStoreDir) { }

        public RegistryClient(string storeDir)
        {
            _cache    = new RegistryCache(storeDir);
            _health   = new PublisherHealth(storeDir);
            _releases = new GithubReleases(storeDir);
        }

        /// <summary>Legacy shape, kept so existing callers compile. Prefer FetchAll.</summary>
        public AddinRegistry Fetch()
        {
            var result = FetchAll(DateTime.Today);
            return new AddinRegistry { Version = 2, Addins = result.Addins, Publishers = result.Publishers };
        }

        /// <summary>
        /// Reads the root registry, then every publisher's list.
        ///
        /// One publisher failing must never empty the pad, so each is fetched independently and a
        /// failure falls back to that publisher's cached list rather than to nothing. The distinction
        /// between "could not reach" and "the server says it is not there" is preserved all the way
        /// to the caller -- see PublisherHealth for why that matters.
        /// </summary>
        public RegistryResult FetchAll(DateTime today)
        {
            var result = new RegistryResult();

            AddinRegistry root;
            try
            {
                root = SimpleJsonParser.ParseRegistry(Download(RegistryUrl));
            }
            catch
            {
                // Without the root we do not know who the publishers are. Show everything ever
                // cached rather than an empty pad.
                result.RootFetchFailed = true;
                return FromCacheOnly(result);
            }

            result.Publishers = root.Publishers;

            // Legacy flat entries first, so a publisher-sourced entry can override one below.
            foreach (var a in root.Addins) result.Addins.Add(a);

            var revoked = new HashSet<string>(root.Revoked, StringComparer.OrdinalIgnoreCase);
            foreach (var p in root.Publishers)
                if (revoked.Contains(p.Id)) p.Status = PublisherStatus.Revoked;

            // In parallel: one slow or hanging publisher should not hold up the rest.
            var fetches = root.Publishers.Select(p => Task.Run(() => FetchPublisher(p))).ToArray();
            try { Task.WaitAll(fetches, TimeSpan.FromSeconds(30)); } catch { /* handled per task below */ }

            for (int i = 0; i < root.Publishers.Count; i++)
            {
                Publisher p = root.Publishers[i];
                PublisherFetch fetched;
                try   { fetched = fetches[i].IsCompleted ? fetches[i].Result : NotAnswered(); }
                catch { fetched = NotAnswered(); }

                _health.Record(p.Id, fetched.Outcome, today);
                result.Outcomes[p.Id] = fetched.Outcome;

                if (fetched.Outcome == FetchOutcome.Ok) _cache.Put(p.Id, fetched.Addins);
                else if (_health.IsPresumedWithdrawn(p.Id, today)) result.PresumedWithdrawn.Add(p.Id);

                MergeIn(result.Addins,
                        fetched.Outcome == FetchOutcome.Ok ? fetched.Addins : _cache.Get(p.Id));
            }

            ResolveSetupReleases(result.Addins, today);
            return result;
        }

        /// <summary>
        /// Fills in the current release for addins that install themselves.
        ///
        /// Their version cannot come from the registry: publishers rename the asset every release,
        /// and Clarion Assistant shipped eight releases in seven weeks -- a hand-kept entry would be
        /// stale within days of each. Asking the releases API instead means nobody maintains it.
        ///
        /// A failure here leaves Release null, which reads as "no installer available" rather than
        /// as an error. Losing a version number should not make a working addin look broken.
        /// </summary>
        private void ResolveSetupReleases(List<RegistryAddin> addins, DateTime today)
        {
            foreach (var addin in addins.Where(a => a.IsSetup))
            {
                try { addin.Release = _releases.Resolve(addin.GithubRepo, today); }
                catch { /* cached answer, or none */ }

                // The registry carries no version for these, so the release is the only source.
                if (addin.Release != null && addin.Release.Version.Length > 0)
                    addin.Version = addin.Release.Version;
            }
        }

        /// <summary>
        /// Addins the user installed THROUGH US that no longer appear anywhere in the current
        /// listing.
        ///
        /// Two things are deliberately excluded.
        ///
        /// Anything we never knew about: since 0.7.1 the store adopts every addin folder found on
        /// disk, which is right for collision checks and for not overwriting other people's work,
        /// but those are not our addins. Clarion Assistant, a hand-unzipped copy, anything another
        /// installer placed -- listing them as "no longer published" would claim a relationship
        /// that never existed and offer the user actions we have no business offering. Evidence
        /// that we knew it is a cached registry entry, or a publisher recorded at install time.
        ///
        /// And anything whose publisher merely could not be reached: absence after a failed fetch
        /// means nothing at all. The caller supplies what is installed; the disk and
        /// installed.v2.json remain the only authority on that.
        /// </summary>
        public List<RegistryAddin> DescribeWithdrawn(RegistryResult result,
                                                     IEnumerable<InstalledAddin> installed)
        {
            var listed = new HashSet<string>(result.Addins.Select(a => a.Id),
                                             StringComparer.OrdinalIgnoreCase);
            var gone = new List<RegistryAddin>();

            foreach (var inst in installed)
            {
                if (listed.Contains(inst.Id)) continue;

                // Say nothing while its publisher is merely unreachable.
                FetchOutcome outcome;
                if (inst.Publisher.Length > 0 &&
                    result.Outcomes.TryGetValue(inst.Publisher, out outcome) &&
                    outcome != FetchOutcome.Ok)
                    continue;

                RegistryAddin? known = _cache.Find(inst.Id);
                if (known == null)
                {
                    // Never listed by anyone we follow. If we installed it ourselves the publisher
                    // is recorded, and it is ours to account for even without a cached entry.
                    if (inst.Publisher.Length == 0) continue;
                    known = new RegistryAddin
                    {
                        Id        = inst.Id,
                        Name      = inst.Id,
                        Publisher = inst.Publisher,
                        Version   = inst.Version,
                    };
                }
                known.NoLongerPublished = true;
                gone.Add(known);
            }
            return gone;
        }

        // ---- internals ------------------------------------------------------------------------

        private class PublisherFetch
        {
            public FetchOutcome        Outcome { get; set; }
            public List<RegistryAddin> Addins  { get; set; } = new List<RegistryAddin>();
        }

        private static PublisherFetch NotAnswered()
            => new PublisherFetch { Outcome = FetchOutcome.Unreachable };

        private PublisherFetch FetchPublisher(Publisher p)
        {
            try
            {
                var addins = SimpleJsonParser.ParsePublisherAddins(Download(p.AddinsUrl), p.Id);

                // A publisher may only serve binaries from their own account. Enforced here rather
                // than by anyone maintaining a list. A violation drops the entry, not the publisher:
                // one bad URL should not take out someone's other addins.
                var kept = addins.Where(a =>
                    p.OwnsDownloadUrl(a.DownloadZipUrl) &&
                    p.OwnsDownloadUrl(a.AddinFileUrl) &&
                    a.DownloadUrls.All(p.OwnsDownloadUrl)).ToList();

                return new PublisherFetch { Outcome = FetchOutcome.Ok, Addins = kept };
            }
            catch (WebException wex)
            {
                var http = wex.Response as HttpWebResponse;
                bool notFound = http != null && http.StatusCode == HttpStatusCode.NotFound;
                return new PublisherFetch
                {
                    Outcome = notFound ? FetchOutcome.NotFound : FetchOutcome.Unreachable
                };
            }
            catch
            {
                return new PublisherFetch { Outcome = FetchOutcome.Malformed };
            }
        }

        /// <summary>A publisher entry replaces a legacy entry with the same id.</summary>
        private static void MergeIn(List<RegistryAddin> into, List<RegistryAddin> entries)
        {
            foreach (var a in entries)
            {
                into.RemoveAll(x => string.Equals(x.Id, a.Id, StringComparison.OrdinalIgnoreCase)
                                    && x.Publisher.Length == 0);
                if (!into.Any(x => string.Equals(x.Id, a.Id, StringComparison.OrdinalIgnoreCase)
                                   && x.Publisher == a.Publisher))
                    into.Add(a);
            }
        }

        private RegistryResult FromCacheOnly(RegistryResult result)
        {
            foreach (string id in _cache.PublisherIds)
            {
                result.Outcomes[id] = FetchOutcome.Unreachable;
                MergeIn(result.Addins, _cache.Get(id));
            }
            return result;
        }

        private static string Download(string url)
        {
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "ClarionAddinFinder/1.0";
                return wc.DownloadString(url);
            }
        }
    }
}
