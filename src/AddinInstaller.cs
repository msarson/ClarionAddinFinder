using System;
using System.IO;
using System.IO.Compression;
using System.Net;

namespace AddinFinder
{
    /// <summary>Downloads and installs addin files into the Clarion addins folder.</summary>
    public class AddinInstaller
    {
        private readonly string _addinsRoot;
        private readonly InstalledAddinStore _store;

        public AddinInstaller(string clarionRoot, InstalledAddinStore store)
        {
            _addinsRoot = Path.Combine(clarionRoot, "accessory", "addins");
            _store      = store;
        }

        public void Install(RegistryAddin addin)
        {
            string folder = Path.Combine(_addinsRoot, addin.Id);
            Directory.CreateDirectory(folder);

            if (!string.IsNullOrEmpty(addin.DownloadZipUrl))
            {
                // Download zip and extract flat into the addin folder
                string tmp = Path.Combine(Path.GetTempPath(), addin.Id + "_install.zip");
                try
                {
                    Download(addin.DownloadZipUrl, tmp);
                    using (var zip = ZipFile.OpenRead(tmp))
                        foreach (var entry in zip.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
                            string dest = Path.Combine(folder, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            entry.ExtractToFile(dest, overwrite: true);
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
                    Download(url, Path.Combine(folder, fileName));
                }
                Download(addin.AddinFileUrl, Path.Combine(folder, addin.Id + ".addin"));
            }

            _store.MarkInstalled(addin.Id, addin.Version);
        }

        public void Uninstall(RegistryAddin addin)
        {
            string folder = Path.Combine(_addinsRoot, addin.Id);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            _store.MarkUninstalled(addin.Id);
        }

        private static void Download(string url, string dest)
        {
            if (string.IsNullOrEmpty(url)) return;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "ClarionAddinFinder/1.0";
                wc.DownloadFile(url, dest);
            }
        }
    }
}
