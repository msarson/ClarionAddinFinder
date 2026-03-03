using System;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace AddinFinder
{
    public class SelfUpdateInfo
    {
        public Version  AvailableVersion { get; set; }
        public string   DownloadUrl      { get; set; }
        public string   AddinFileUrl     { get; set; }
    }

    internal class SelfUpdateChecker
    {
        private const string VersionUrl =
            "https://raw.githubusercontent.com/msarson/ClarionAddinFinder/master/version.json";

        /// <summary>
        /// Synchronously checks for a newer version. Call this on a background thread.
        /// Returns null if no update available or if the check fails.
        /// </summary>
        public static SelfUpdateInfo? Check()
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

                string json = null!;
                Exception? lastEx = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0) Thread.Sleep(2000 * attempt);
                    try
                    {
                        using (var wc = new WebClient())
                        {
                            wc.Headers[HttpRequestHeader.UserAgent] = "ClarionAddinFinder/0.5";
                            json = wc.DownloadString(VersionUrl);
                        }
                        lastEx = null;
                        break;
                    }
                    catch (WebException ex) { lastEx = ex; }
                }
                if (lastEx != null || json == null) return null;

                var info    = Parse(json);
                if (info == null) return null;

                // Compare against the DISK file version, not the in-memory assembly version.
                // After ApplyPendingUpdates runs at startup, the disk already has the new DLL
                // even though the old version is still loaded in memory. This means one restart
                // is sufficient — disk version matches latest so no banner fires on restart 1.
                string asmPath    = typeof(SelfUpdateChecker).Assembly.Location;
                var    diskVer    = System.Diagnostics.FileVersionInfo.GetVersionInfo(asmPath);
                var    diskVersion = new Version(diskVer.FileMajorPart, diskVer.FileMinorPart, diskVer.FileBuildPart);

                return info.AvailableVersion > diskVersion ? info : null;
            }
            catch { return null; }
        }

        private static SelfUpdateInfo? Parse(string json)
        {
            try
            {
                string ver     = Extract(json, "version");
                string dlUrl   = Extract(json, "downloadUrl");
                string addinUrl= Extract(json, "addinFileUrl");
                if (string.IsNullOrEmpty(ver) || string.IsNullOrEmpty(dlUrl)) return null;
                return new SelfUpdateInfo
                {
                    AvailableVersion = Version.Parse(ver),
                    DownloadUrl      = dlUrl,
                    AddinFileUrl     = addinUrl
                };
            }
            catch { return null; }
        }

        private static string Extract(string json, string key)
        {
            var m = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : string.Empty;
        }
    }
}
