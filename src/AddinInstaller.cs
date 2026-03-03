using System;
using System.IO;
using System.IO.Compression;
using System.Net;

namespace AddinFinder
{
    /// <summary>Downloads and installs addin files into the Clarion addins folder.</summary>
    public class AddinInstaller
    {
        private static readonly string StagingRoot =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "ClarionAddinFinder", "pending");

        private readonly string _addinsRoot;
        private readonly InstalledAddinStore _store;

        public AddinInstaller(string clarionRoot, InstalledAddinStore store)
        {
            _addinsRoot = Path.Combine(clarionRoot, "accessory", "addins");
            _store      = store;
        }

        private const string UninstallMarker = "_uninstall";

        /// <summary>
        /// Apply any pending updates/uninstalls staged during the previous session.
        /// Call this at startup before any addin folders are loaded.
        /// </summary>
        public int ApplyPendingUpdates()
        {
            int applied = 0;
            if (!Directory.Exists(StagingRoot)) return 0;

            foreach (string stagingDir in Directory.GetDirectories(StagingRoot))
            {
                string addinId  = Path.GetFileName(stagingDir);
                string addinDir = Path.Combine(_addinsRoot, addinId);

                try
                {
                    // Staged uninstall — marker file means delete the addin folder entirely
                    if (File.Exists(Path.Combine(stagingDir, UninstallMarker)))
                    {
                        if (Directory.Exists(addinDir))
                            Directory.Delete(addinDir, recursive: true);
                        Directory.Delete(stagingDir, recursive: true);
                        applied++;
                        continue;
                    }

                    // Staged update — copy files over
                    foreach (string file in Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories))
                    {
                        string relative = file.Substring(stagingDir.Length + 1);
                        string dest     = Path.Combine(addinDir, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Copy(file, dest, overwrite: true);
                    }
                    Directory.Delete(stagingDir, recursive: true);
                    applied++;
                }
                catch { /* leave staging in place if still locked */ }
            }
            return applied;
        }

        /// <summary>Returns true if the update was staged (files locked); false if applied immediately.</summary>
        public bool Install(RegistryAddin addin, out bool staged)
        {
            staged = false;
            string folder = Path.Combine(_addinsRoot, addin.Id);
            Directory.CreateDirectory(folder);

            try
            {
                WriteFiles(addin, folder);
            }
            catch (IOException)
            {
                // Files locked — stage to AppData\ClarionAddinFinder\pending\{id}\ for next startup
                staged = true;
                string pending = Path.Combine(StagingRoot, addin.Id);
                Directory.CreateDirectory(pending);
                WriteFiles(addin, pending);
            }

            _store.MarkInstalled(addin.Id, addin.Version);
            return true;
        }

        /// <summary>Returns true if uninstall was staged (files locked); false if removed immediately.</summary>
        public bool Uninstall(RegistryAddin addin, out bool staged)
        {
            staged = false;
            string folder = Path.Combine(_addinsRoot, addin.Id);
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Files locked or access denied — stage for removal on next startup
                staged = true;
                string pending = Path.Combine(StagingRoot, addin.Id);
                Directory.CreateDirectory(pending);
                File.WriteAllText(Path.Combine(pending, UninstallMarker), "");
            }
            _store.MarkUninstalled(addin.Id);
            return true;
        }

        /// <summary>
        /// Stages a self-update of AddinFinder. Always staged since the DLL is always locked.
        /// Downloads to %APPDATA%\ClarionAddinFinder\pending\AddinFinder\ for ApplyPendingUpdates to handle.
        /// </summary>
        public static void StageSelfUpdate(SelfUpdateInfo info)
        {
            string pending = Path.Combine(StagingRoot, "AddinFinder");
            Directory.CreateDirectory(pending);
            Download(info.DownloadUrl,    Path.Combine(pending, "AddinFinder.dll"));
            if (!string.IsNullOrEmpty(info.AddinFileUrl))
                Download(info.AddinFileUrl, Path.Combine(pending, "AddinFinder.addin"));
        }

        private void WriteFiles(RegistryAddin addin, string dest)
        {
            if (!string.IsNullOrEmpty(addin.DownloadZipUrl))
            {
                string tmp = Path.Combine(Path.GetTempPath(), addin.Id + "_install.zip");
                try
                {
                    Download(addin.DownloadZipUrl, tmp);
                    using (var zip = ZipFile.OpenRead(tmp))
                        foreach (var entry in zip.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;
                            string entryDest = Path.Combine(dest, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(Path.GetDirectoryName(entryDest)!);
                            entry.ExtractToFile(entryDest, overwrite: true);
                        }
                }
                finally
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
            }
            else
            {
                foreach (string url in addin.DownloadUrls)
                {
                    string fileName = Path.GetFileName(new Uri(url).LocalPath);
                    Download(url, Path.Combine(dest, fileName));
                }
                Download(addin.AddinFileUrl, Path.Combine(dest, addin.Id + ".addin"));
            }
        }

        private static void Download(string url, string dest)
        {
            if (string.IsNullOrEmpty(url)) return;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            string tmp = dest + ".tmp";
            try
            {
                // Retry up to 3 times — GitHub CDN can transiently return 404/connection errors
                // for newly-published releases
                Exception? lastEx = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0) System.Threading.Thread.Sleep(2000 * attempt);
                    try
                    {
                        using (var wc = new WebClient())
                        {
                            wc.Headers[HttpRequestHeader.UserAgent] = "ClarionAddinFinder/1.0";
                            wc.DownloadFile(url, tmp);
                        }
                        lastEx = null;
                        break;
                    }
                    catch (WebException ex) { lastEx = ex; }
                }
                if (lastEx != null) throw lastEx;
                File.Copy(tmp, dest, overwrite: true);  // throws IOException if dest locked → staging kicks in
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
    }
}
