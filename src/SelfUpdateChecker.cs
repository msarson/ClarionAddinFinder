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
            "https://raw.githubusercontent.com/msarson/ClarionAddinFinder/main/version.json";

        /// <summary>
        /// Checks for a newer version of AddinFinder asynchronously.
        /// Calls <paramref name="callback"/> on the thread pool — marshal to UI before touching controls.
        /// </summary>
        public static void CheckAsync(Action<SelfUpdateInfo?> callback)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol =
                        SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

                    string json;
                    // Retry up to 3 times (same pattern as AddinInstaller)
                    Exception? lastEx = null;
                    json = null!;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (attempt > 0) Thread.Sleep(2000 * attempt);
                        try
                        {
                            using (var wc = new WebClient())
                            {
                                wc.Headers[HttpRequestHeader.UserAgent] = "ClarionAddinFinder/1.0";
                                json = wc.DownloadString(VersionUrl);
                            }
                            lastEx = null;
                            break;
                        }
                        catch (WebException ex) { lastEx = ex; }
                    }
                    if (lastEx != null) { callback(null); return; }

                    var info = Parse(json);
                    var running = Assembly.GetExecutingAssembly().GetName().Version;

                    // Only report if strictly newer
                    callback(info != null && info.AvailableVersion > running ? info : null);
                }
                catch { callback(null); }
            });
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
