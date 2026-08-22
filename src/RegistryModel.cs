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
    }

    public class AddinRegistry
    {
        public int                   Version  { get; set; }
        public string                Updated  { get; set; } = "";
        public List<RegistryAddin>   Addins   { get; set; } = new List<RegistryAddin>();
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
