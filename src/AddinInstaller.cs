using System;
using System.IO;
using System.IO.Compression;
using System.Net;

namespace AddinFinder
{
    /// <summary>
    /// Raised when installing would put a second addin declaring the same Identity under one
    /// Clarion. Carries what the user needs told: which addin, which Identity, and where the copy
    /// already holding it lives.
    /// </summary>
    public class IdentityConflictException : Exception
    {
        public string AddinId      { get; }
        public string IdentityName { get; }
        public string ExistingPath { get; }

        public IdentityConflictException(string addinId, string identityName, string existingPath)
            : base($"{addinId} declares the identity '{identityName}', which is already installed at {existingPath}")
        {
            AddinId      = addinId;
            IdentityName = identityName;
            ExistingPath = existingPath;
        }
    }

    /// <summary>Downloads and installs addin files into the Clarion addins folder.</summary>
    public class AddinInstaller
    {
        private static readonly string StagingRoot =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "ClarionAddinFinder", "pending");

        // Set TLS 1.2 once — .NET 4.x defaults to TLS 1.0, GitHub requires 1.2+
        static AddinInstaller()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
        }

        private readonly string _clarionRoot;
        private readonly string _addinsRoot;
        private readonly InstalledAddinStore _store;

        /// <summary>The Clarion this installer acts on. Every store write is keyed by it.</summary>
        public string ClarionRootPath => _clarionRoot;

        public AddinInstaller(string clarionRoot, InstalledAddinStore store)
        {
            _clarionRoot = ClarionRoot.Normalise(clarionRoot);
            _addinsRoot  = ClarionRoot.AddinsFolder(_clarionRoot);
            _store       = store;
        }

        private const string UninstallMarker = "_uninstall";

        /// <summary>
        /// Suffix for the download scratch folder. It sits under the staging root -- outside the
        /// folder Clarion scans -- so a failed or interrupted download can never present itself to
        /// the IDE as a broken addin.
        /// </summary>
        private const string ScratchSuffix = "_dl";

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

                // Download scratch folders live here too. They are not staged work, and treating
                // one as such would create an addin folder literally named "<id>_dl".
                if (addinId.EndsWith(ScratchSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    SafeDeleteDirectory(stagingDir);
                    continue;
                }

                string addinDir = Path.Combine(_addinsRoot, addinId);

                try
                {
                    // Staged uninstall — marker file means delete the addin folder entirely
                    if (File.Exists(Path.Combine(stagingDir, UninstallMarker)))
                    {
                        if (Directory.Exists(addinDir))
                            Directory.Delete(addinDir, recursive: true);
                        Directory.Delete(stagingDir, recursive: true);
                        _store.MarkUninstalled(_clarionRoot, addinId);
                        applied++;
                        continue;
                    }

                    // Staged update — copy files over
                    string stagingDirNorm = stagingDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    foreach (string file in Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories))
                    {
                        string relative = file.Substring(stagingDirNorm.Length + 1);
                        string dest     = Path.Combine(addinDir, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                        // .NET holds DLLs with FILE_SHARE_DELETE — we can rename (not overwrite) a loaded DLL.
                        // Rename the old file out of the way first, then copy the new one in.
                        if (File.Exists(dest))
                        {
                            string backup = dest + ".old";
                            if (File.Exists(backup)) File.Delete(backup);
                            File.Move(dest, backup);
                        }

                        File.Copy(file, dest, overwrite: false);
                    }
                    Directory.Delete(stagingDir, recursive: true);

                    // The payload is now under accessory\addins, so the entry is no longer staged
                    // and rejoins the disk reconciliation. Version comes from the manifest we just
                    // wrote, which Load() reads -- passing "" here would only be overwritten.
                    _store.ClearStaged(_clarionRoot, addinId);
                    applied++;
                }
                catch { /* leave staging in place if still locked */ }
            }
            return applied;
        }

        /// <summary>
        /// A folder under accessory\addins, other than this addin's own, that already declares the
        /// same Identity. Returns its path, or null.
        ///
        /// Clarion loads every subfolder of accessory\addins at startup and refuses to start at all
        /// if two of them declare the same &lt;Identity name&gt; -- the user gets "Identity name used by
        /// multiple addins" and the IDE will not open. Since the folder name is the addin id, two
        /// publishers listing the same id would also silently overwrite each other.
        ///
        /// Checking here is what lets the registry avoid tracking which publisher owns which id: the
        /// clash surfaces once, to the one user who would actually hit it, at the only moment it can
        /// be prevented.
        /// </summary>
        public string? FindConflictingIdentity(string addinId, string identityName)
        {
            if (string.IsNullOrEmpty(identityName)) return null;
            if (!Directory.Exists(_addinsRoot)) return null;

            string[] folders;
            try { folders = Directory.GetDirectories(_addinsRoot); }
            catch { return null; }

            foreach (string folder in folders)
            {
                if (string.Equals(Path.GetFileName(folder), addinId, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    foreach (string manifest in Directory.GetFiles(folder, "*.addin",
                                                                   SearchOption.TopDirectoryOnly))
                        if (string.Equals(ReadIdentityName(manifest), identityName,
                                          StringComparison.OrdinalIgnoreCase))
                            return folder;
                }
                catch { /* an unreadable folder is not evidence of a clash */ }
            }
            return null;
        }

        /// <summary>The Identity declared by the payload in a folder, or "" if there is no manifest.</summary>
        private static string ReadIdentityNameFrom(string folder)
        {
            try
            {
                foreach (string manifest in Directory.GetFiles(folder, "*.addin",
                                                               SearchOption.TopDirectoryOnly))
                {
                    string name = ReadIdentityName(manifest);
                    if (name.Length > 0) return name;
                }
            }
            catch { }
            return "";
        }

        /// <summary>The &lt;Identity name&gt; of a manifest, or "" if it cannot be read.</summary>
        public static string ReadIdentityName(string manifestPath)
        {
            try
            {
                string xml = File.ReadAllText(manifestPath, System.Text.Encoding.UTF8);
                int i = xml.IndexOf("<Identity", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return "";
                int end = xml.IndexOf('>', i);
                if (end < 0) return "";
                string tag = xml.Substring(i, end - i);

                int n = tag.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
                if (n < 0) return "";
                int q1 = tag.IndexOf('"', n);
                if (q1 < 0) return "";
                int q2 = tag.IndexOf('"', q1 + 1);
                if (q2 < 0) return "";
                return tag.Substring(q1 + 1, q2 - q1 - 1).Trim();
            }
            catch { return ""; }
        }

        /// <summary>Returns true if the update was staged (files locked); false if applied immediately.</summary>
        public bool Install(RegistryAddin addin, out bool staged)
        {
            staged = false;
            string folder = Path.Combine(_addinsRoot, addin.Id);

            // Clarion scans every subfolder of accessory\addins at startup and reports one that has
            // no usable manifest as a broken addin. So the folder must not exist until its contents
            // do: download to a scratch folder OUTSIDE the scanned root, then move it into place.
            // Creating it first and downloading into it leaves an empty folder behind on any
            // failure -- and WebException does not derive from IOException, so the catch below
            // never covered a failed download.
            string scratch = Path.Combine(StagingRoot, addin.Id + ScratchSuffix);
            bool preexisting = Directory.Exists(folder);
            try
            {
                SafeDeleteDirectory(scratch);
                Directory.CreateDirectory(scratch);
                WriteFiles(addin, scratch);

                // Checked HERE, against the manifest just downloaded, rather than earlier from the
                // registry entry. The addin id is NOT the Identity: FlattenCode is published with
                // <Identity name="FlattenCode.Addin"/>, so assuming they match would look for the
                // wrong name and miss a real clash. Only the file itself knows.
                string identity = ReadIdentityNameFrom(scratch);
                string? clash   = FindConflictingIdentity(addin.Id, identity);
                if (clash != null)
                    throw new IdentityConflictException(addin.Id, identity, clash);

                try
                {
                    MoveIntoPlace(scratch, folder);
                }
                catch (IOException)
                {
                    // Files locked by the running IDE — hand the already-downloaded payload to
                    // ApplyPendingUpdates, which runs before the addins load next startup.
                    staged = true;
                    string pending = Path.Combine(StagingRoot, addin.Id);
                    SafeDeleteDirectory(pending);
                    Directory.Move(scratch, pending);
                }
            }
            catch
            {
                // Never leave a half-written folder under the scanned root. A folder that was
                // already there before we started is the user's, not ours, so it stays.
                if (!preexisting) SafeDeleteDirectory(folder);
                throw;
            }
            finally
            {
                SafeDeleteDirectory(scratch);
            }

            // Record the version only once the files are where they belong. On the staged path the
            // payload is still in pending, so the entry is marked staged and exempted from the disk
            // reconciliation until ApplyPendingUpdates moves it.
            _store.MarkInstalled(_clarionRoot, addin.Id, addin.Version, staged, addin.Publisher);
            return true;
        }

        /// <summary>Copy everything from src into dest, creating dest if needed.</summary>
        private static void MoveIntoPlace(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(src.Length).TrimStart(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(dest, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);   // throws IOException if locked
            }
        }

        private static void SafeDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch { /* best effort */ }
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
            _store.MarkUninstalled(_clarionRoot, addin.Id);
            return true;
        }

        /// <summary>
        /// Stages a self-update of AddinFinder. Always staged since the DLL is always locked.
        /// Downloads to %APPDATA%\ClarionAddinFinder\pending\{addinFolderName}\ for ApplyPendingUpdates to handle.
        /// </summary>
        public static void StageSelfUpdate(SelfUpdateInfo info)
        {
            // Derive names from the actual assembly location — never hardcode
            string asmPath     = typeof(AddinInstaller).Assembly.Location;
            string addinId     = Path.GetFileName(Path.GetDirectoryName(asmPath)!);
            string dllFileName = Path.GetFileName(asmPath);
            string addinFileName = Path.GetFileNameWithoutExtension(asmPath) + ".addin";

            string pending = Path.Combine(StagingRoot, addinId);
            Directory.CreateDirectory(pending);
            Download(info.DownloadUrl,    Path.Combine(pending, dllFileName));
            if (!string.IsNullOrEmpty(info.AddinFileUrl))
                Download(info.AddinFileUrl, Path.Combine(pending, addinFileName));
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
                            string entryDest = Path.GetFullPath(
                                Path.Combine(dest, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                            // Guard against zip slip path traversal
                            if (!entryDest.StartsWith(Path.GetFullPath(dest) + Path.DirectorySeparatorChar))
                                throw new InvalidOperationException($"Zip entry blocked (path traversal): {entry.FullName}");
                            Directory.CreateDirectory(Path.GetDirectoryName(entryDest)!);
                            entry.ExtractToFile(entryDest, overwrite: true);
                            try { File.Delete(entryDest + ":Zone.Identifier"); } catch { }
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
                try { File.Delete(dest + ":Zone.Identifier"); } catch { }
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
    }
}
