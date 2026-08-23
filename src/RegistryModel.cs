using System;
using System.Collections.Generic;

namespace AddinFinder
{
    /// <summary>Registry addin entry as deserialised from registry.json.</summary>
    public class RegistryAddin
    {
        public string Id            { get; set; } = "";
        public string Name          { get; set; } = "";
        public string Description   { get; set; } = "";
        public string Author        { get; set; } = "";
        public string AuthorUrl     { get; set; } = "";
        public string License       { get; set; } = "";
        public string Category      { get; set; } = "";
        public string Version       { get; set; } = "";
        public string TargetFramework { get; set; } = "";
        public List<string> DownloadUrls { get; set; } = new List<string>();
        public string DownloadZipUrl { get; set; } = "";
        public string AddinFileUrl  { get; set; } = "";
        public string HomepageUrl   { get; set; } = "";
        public string ChangelogUrl  { get; set; } = "";
        public bool   Fork          { get; set; } = false;
        public string UpstreamUrl   { get; set; } = "";

        /// <summary>
        /// Publisher id this entry came from, or "" for an entry from the legacy flat list.
        /// Provenance is shown to the user, so it must survive from fetch through to install.
        /// </summary>
        public string Publisher      { get; set; } = "";

        /// <summary>"active" (default) or "deprecated". See AddinLifecycle.</summary>
        public string Status         { get; set; } = AddinLifecycle.Active;

        /// <summary>The publisher's own words about a non-active status. Shown verbatim.</summary>
        public string StatusNote     { get; set; } = "";

        /// <summary>Optional id of the addin that supersedes this one.</summary>
        public string ReplacedBy     { get; set; } = "";

        /// <summary>
        /// Set when the entry came from cache rather than a live fetch, so the pad can say so
        /// instead of presenting stale data as current.
        /// </summary>
        public bool   FromCache      { get; set; }

        /// <summary>
        /// Set when the publisher's list loaded successfully and this addin was NOT in it, but the
        /// user has it installed. Distinct from a fetch failure -- see PublisherHealth.
        /// </summary>
        public bool   NoLongerPublished { get; set; }

        /// <summary>
        /// "owner/repo" for an addin distributed as a Windows setup installer rather than as files
        /// Addin Finder can place. Its presence is what puts the addin in download-only mode.
        ///
        /// A repository rather than a URL, because publishers rename the asset every release
        /// (ClarionAssistant-5.8-Setup.exe, then -5.8.1-), so a pinned link 404s almost immediately.
        /// The current tag and asset are resolved from the releases API instead, which also means
        /// nobody has to maintain a version here.
        /// </summary>
        public string GithubRepo   { get; set; } = "";

        /// <summary>
        /// True when this addin installs itself. Addin Finder downloads the installer and gets out
        /// of the way: the setup elevates, picks its own Clarion targets, and writes files we did
        /// not place -- so managing it as ours would be a claim we cannot support.
        /// </summary>
        public bool IsSetup => GithubRepo.Length > 0;

        /// <summary>Resolved at refresh from the releases API. Null for a normally-packaged addin.</summary>
        public GithubRelease? Release { get; set; }

        public bool IsOffered => Status == AddinLifecycle.Active && !NoLongerPublished;
    }

    public static class AddinLifecycle
    {
        public const string Active     = "active";
        public const string Deprecated = "deprecated";
    }

    public static class PublisherStatus
    {
        /// <summary>Normal.</summary>
        public const string Active = "active";

        /// <summary>The publisher's own declaration that they have stopped. Ordinary and blameless.</summary>
        public const string Abandoned = "abandoned";

        /// <summary>The registry's action, for safety. Deliberately distinct from Abandoned.</summary>
        public const string Revoked = "revoked";
    }

    /// <summary>A publisher recorded in the root registry.</summary>
    public class Publisher
    {
        public string Id         { get; set; } = "";
        public string Name       { get; set; } = "";
        public string Repo       { get; set; } = "";

        /// <summary>
        /// Default branch of the publisher's repo. Recorded rather than assumed: msarson/clarion-addins
        /// is on "main" while the root registry is on "master", and guessing produces a 404 that looks
        /// exactly like a deleted repository.
        /// </summary>
        public string Branch     { get; set; } = "";

        public string Status     { get; set; } = PublisherStatus.Active;
        public string StatusNote { get; set; } = "";

        /// <summary>Where this publisher's addin list lives. Derived, never taken from the registry.</summary>
        public string AddinsUrl =>
            "https://raw.githubusercontent.com/" + Id + "/" + Repo + "/" +
            (string.IsNullOrEmpty(Branch) ? "main" : Branch) + "/addins.json";

        /// <summary>
        /// A download URL may only serve from the publisher's own GitHub account. Derived from the id,
        /// so it costs no registry maintenance -- and it stops "approved once" becoming permission to
        /// serve arbitrary binaries from anywhere later.
        /// </summary>
        public bool OwnsDownloadUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return true;   // nothing to serve, nothing to check
            string prefix = "https://github.com/" + Id + "/";
            return url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The same rule for an addin that installs itself, where the registry records "owner/repo"
        /// instead of a URL.
        ///
        /// It needs saying separately because a setup entry carries no download URLs at all, so it
        /// sails through OwnsDownloadUrl -- three empty strings, nothing to check -- while the thing
        /// actually fetched comes from a repository nobody was checking. That is the one place the
        /// rule matters most: what arrives is an installer, which the user then runs with elevation.
        ///
        /// Strict, and for the same reason as the URL form: approving a publisher must not become
        /// permission to point users at someone else's account later.
        /// </summary>
        public bool OwnsRepo(string ownerRepo)
        {
            if (string.IsNullOrEmpty(ownerRepo)) return true;   // nothing to resolve, nothing to check
            int slash = ownerRepo.IndexOf('/');
            if (slash <= 0) return false;                       // not "owner/repo" -- do not guess
            return string.Equals(ownerRepo.Substring(0, slash), Id, StringComparison.OrdinalIgnoreCase);
        }
    }

    public class AddinRegistry
    {
        public int                   Version    { get; set; }
        public string                Updated    { get; set; } = "";
        public List<RegistryAddin>   Addins     { get; set; } = new List<RegistryAddin>();
        public List<Publisher>       Publishers { get; set; } = new List<Publisher>();

        /// <summary>Publisher ids the registry has revoked. Empty in normal operation.</summary>
        public List<string>          Revoked    { get; set; } = new List<string>();
    }

    public class InstalledAddin
    {
        public string Id            { get; set; } = "";
        public string Version       { get; set; } = "";
        public string InstalledAt   { get; set; } = "";

        /// <summary>
        /// The Clarion root this addin is installed into. Empty only for legacy (v1) entries, which
        /// predate per-root tracking and wait in InstalledStore.LegacyUnclaimed until a root claims
        /// them.
        /// </summary>
        public string Root          { get; set; } = "";

        /// <summary>
        /// True while the files are still in the pending folder because the originals were locked.
        /// A staged entry is exempt from disk reconciliation -- its files have not reached
        /// accessory/addins yet, so checking for them there would wrongly delete the entry.
        /// </summary>
        public bool   Staged        { get; set; }

        /// <summary>
        /// Publisher this copy came from, or "" when unknown -- entries adopted from disk, and
        /// anything another installer put there, never have one. Shown to the user, so an empty
        /// value must read as "unknown", never as a default publisher.
        /// </summary>
        public string Publisher     { get; set; } = "";
    }

    /// <summary>The whole installed.json document. Version 2 keys entries by Clarion root.</summary>
    public class InstalledStore
    {
        public int                  Version         { get; set; } = 2;
        public List<InstalledAddin> Installed       { get; set; } = new List<InstalledAddin>();

        /// <summary>v1 entries not yet attributed to a root. See SimpleJsonParser.ParseStore.</summary>
        public List<InstalledAddin> LegacyUnclaimed { get; set; } = new List<InstalledAddin>();
    }
}
