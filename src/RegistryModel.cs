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
        public string License       { get; set; } = "";
        public string Category      { get; set; } = "";
        public string Version       { get; set; } = "";
        public string TargetFramework { get; set; } = "";
        public string DownloadUrl   { get; set; } = "";
        public string AddinFileUrl  { get; set; } = "";
        public string HomepageUrl   { get; set; } = "";
        public string ChangelogUrl  { get; set; } = "";
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
    }
}
