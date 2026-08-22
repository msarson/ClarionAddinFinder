using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace AddinFinder
{
    /// <summary>The current release of a repository, as far as we last managed to look.</summary>
    public class GithubRelease
    {
        /// <summary>Release tag, e.g. "v5.8.1".</summary>
        public string Tag { get; set; } = "";

        /// <summary>Tag with any leading "v" removed, for comparing against a manifest version.</summary>
        public string Version => Tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? Tag.Substring(1) : Tag;

        /// <summary>Browser download URL of the installer asset.</summary>
        public string AssetUrl { get; set; } = "";

        /// <summary>File name of that asset, which changes from release to release.</summary>
        public string AssetName { get; set; } = "";

        /// <summary>yyyy-MM-dd this was last read from GitHub.</summary>
        public string CheckedOn { get; set; } = "";

        public bool IsUsable => Tag.Length > 0 && AssetUrl.Length > 0;
    }

    /// <summary>
    /// Resolves "what is the current release of this repository" for addins distributed as a setup
    /// installer rather than as files we can place ourselves.
    ///
    /// The asset URL cannot be pinned in the registry, because publishers rename it every release:
    ///
    ///     v5.8.1  ->  ClarionAssistant-5.8.1-Setup.exe
    ///     v5.8.0  ->  ClarionAssistant-5.8-Setup.exe
    ///     v5.7.0  ->  ClarionAssistant-5.7-Setup.exe
    ///
    /// A pinned URL would 404 the day after it was written. Resolving through the releases API
    /// instead means nobody has to maintain a version or a link -- which matters, because Clarion
    /// Assistant shipped eight releases in seven weeks and any hand-kept entry would have been stale
    /// within days of each one.
    /// </summary>
    public class GithubReleases
    {
        /// <summary>
        /// How long a resolved release is reused before asking GitHub again.
        ///
        /// Unauthenticated requests are limited to 60 per hour PER IP, shared with everything else
        /// on that network -- an office behind one NAT can exhaust it without any single person
        /// doing anything unusual. The pad refreshes whenever it is opened, so asking every time
        /// would spend that budget on a number that changes a few times a month.
        /// </summary>
        public const int CacheHours = 6;

        private readonly string _path;
        private Dictionary<string, GithubRelease> _cache;

        public GithubReleases(string storeDir)
        {
            _path  = Path.Combine(storeDir, "release-cache.json");
            _cache = Read();
        }

        /// <summary>
        /// The current release of "owner/repo", from cache when it is fresh enough.
        ///
        /// Returns the cached answer on failure rather than nothing: a rate limit or a dropped
        /// connection should not make an addin look unavailable, for the same reason a publisher
        /// being unreachable does not empty the pad.
        /// </summary>
        public GithubRelease? Resolve(string ownerRepo, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(ownerRepo)) return null;

            GithubRelease? cached;
            _cache.TryGetValue(ownerRepo, out cached);
            if (cached != null && IsFresh(cached, now)) return cached;

            try
            {
                var fetched = Fetch(ownerRepo);
                if (fetched != null && fetched.IsUsable)
                {
                    fetched.CheckedOn   = now.ToString("yyyy-MM-dd HH:mm");
                    _cache[ownerRepo]   = fetched;
                    Write();
                    return fetched;
                }
            }
            catch { /* fall through to whatever we already had */ }

            return cached;
        }

        private static bool IsFresh(GithubRelease r, DateTime now)
        {
            DateTime checkedOn;
            if (!DateTime.TryParse(r.CheckedOn, out checkedOn)) return false;
            return (now - checkedOn).TotalHours < CacheHours;
        }

        private static GithubRelease? Fetch(string ownerRepo)
        {
            string url = "https://api.github.com/repos/" + ownerRepo + "/releases/latest";
            string json;
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                // GitHub rejects requests without a User-Agent outright.
                wc.Headers[HttpRequestHeader.UserAgent] = "ClarionAddinFinder";
                wc.Headers[HttpRequestHeader.Accept]    = "application/vnd.github+json";
                json = wc.DownloadString(url);
            }
            return SimpleJsonParser.ParseGithubRelease(json);
        }

        private Dictionary<string, GithubRelease> Read()
        {
            try
            {
                if (!File.Exists(_path)) return new Dictionary<string, GithubRelease>(StringComparer.OrdinalIgnoreCase);
                return SimpleJsonParser.ParseReleaseCache(File.ReadAllText(_path, Encoding.UTF8));
            }
            catch { return new Dictionary<string, GithubRelease>(StringComparer.OrdinalIgnoreCase); }
        }

        private void Write()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, SimpleJsonParser.SerialiseReleaseCache(_cache), Encoding.UTF8);
            }
            catch { /* a cache we cannot write is not worth failing a refresh over */ }
        }
    }
}
