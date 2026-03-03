using System;
using System.IO;
using System.IO.Compression;
using System.Net;

namespace AddinFinder
{
    /// <summary>Downloads and installs addin files into the Clarion addins folder.</summary>
    public class AddinInstaller
    {
        private const string PendingFolder = ".pending";

        private readonly string _addinsRoot;
        private readonly InstalledAddinStore _store;

        public AddinInstaller(string clarionRoot, InstalledAddinStore store)
        {
            _addinsRoot = Path.Combine(clarionRoot, "accessory", "addins");
            _store      = store;
        }

        /// <summary>
        /// Apply any pending updates staged during the previous session.
        /// Call this at startup before any addin folders are loaded.
        /// </summary>
        public int ApplyPendingUpdates()
        {
            int applied = 0;
            if (!Directory.Exists(_addinsRoot)) return 0;

            foreach (string addinDir in Directory.GetDirectories(_addinsRoot))
            {
                string pending = Path.Combine(addinDir, PendingFolder);
                if (!Directory.Exists(pending)) continue;

                try
                {
                    foreach (string file in Directory.GetFiles(pending, "*", SearchOption.AllDirectories))
                    {
                        string relative = file.Substring(pending.Length + 1);
                        string dest = Path.Combine(addinDir, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Copy(file, dest, overwrite: true);
                    }
                    Directory.Delete(pending, recursive: true);
                    applied++;
                }
                catch { /* leave pending in place if still locked */ }
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
                // Files locked — stage to .pending for next startup
                staged = true;
                string pending = Path.Combine(folder, PendingFolder);
                Directory.CreateDirectory(pending);
                WriteFiles(addin, pending);
            }

            _store.MarkInstalled(addin.Id, addin.Version);
            return true;
        }

        public void Uninstall(RegistryAddin addin)
        {
            string folder = Path.Combine(_addinsRoot, addin.Id);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            _store.MarkUninstalled(addin.Id);
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
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "ClarionAddinFinder/1.0";
                    wc.DownloadFile(url, tmp);  // download to temp — dest may be locked
                }
                File.Copy(tmp, dest, overwrite: true);  // throws IOException if dest locked → staging kicks in
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
    }
}
