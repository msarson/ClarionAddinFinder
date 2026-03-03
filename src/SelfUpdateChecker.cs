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
                var running = Assembly.GetExecutingAssembly().GetName().Version;
                return (info != null && info.AvailableVersion > running) ? info : null;
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
