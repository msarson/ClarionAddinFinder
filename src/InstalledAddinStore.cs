using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AddinFinder
{
    /// <summary>
    /// Tracks which addins are installed, PER CLARION ROOT.
    /// Stored at %APPDATA%\ClarionAddinFinder\installed.json
    ///
    /// Two rules govern this file:
    ///
    /// 1. It is keyed by Clarion root. A machine with Clarion 10/11/11.1/12 has four addin folders;
    ///    a store keyed by addin id alone reports an addin installed everywhere the moment it is
    ///    installed anywhere (issue #6).
    ///
    /// 2. The DISK is the source of truth; this file is a cache. Every load reconciles against the
    ///    addin folder, so a store that has drifted -- for any reason, not just ours -- corrects
    ///    itself on the next start rather than lying indefinitely.
    /// </summary>
    public class InstalledAddinStore
    {
        private static string DefaultStoreDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClarionAddinFinder");

        private readonly string _storeDir;
        private readonly string _storePath;
        private readonly string _backupPath;

        public InstalledAddinStore() : this(DefaultStoreDir) { }

        /// <summary>
        /// Overrides where the store lives. Exists so tests can point at a scratch directory:
        /// SpecialFolder.ApplicationData goes through the Win32 shell API and ignores %APPDATA%,
        /// so there is no way to redirect it from outside the process.
        /// </summary>
        public InstalledAddinStore(string storeDir)
        {
            _storeDir   = storeDir;
            _storePath  = Path.Combine(storeDir, "installed.json");
            _backupPath = Path.Combine(storeDir, "installed.json.v1.bak");
        }

        /// <summary>
        /// Entries for this Clarion root, reconciled against disk.
        ///
        /// Also performs the v1 migration and the legacy claim, both lazily. That matters: a self
        /// update lands the new DLL on disk at one startup but does not LOAD it until the next, so
        /// migration cannot be tied to install time. Doing it here means it happens the first time
        /// the new code reads the store, whenever that is, and is safe to repeat.
        /// </summary>
        public List<InstalledAddin> Load(string clarionRoot)
        {
            if (string.IsNullOrEmpty(clarionRoot)) return new List<InstalledAddin>();

            InstalledStore doc = ReadDocument();
            bool changed = ClaimLegacy(doc, clarionRoot);
            changed |= Reconcile(doc, clarionRoot);
            if (changed) Write(doc);

            return doc.Installed.Where(a => ClarionRoot.Same(a.Root, clarionRoot)).ToList();
        }

        public void MarkInstalled(string clarionRoot, string id, string version, bool staged)
        {
            InstalledStore doc = ReadDocument();
            doc.Installed.RemoveAll(a => a.Id == id && ClarionRoot.Same(a.Root, clarionRoot));
            doc.Installed.Add(new InstalledAddin
            {
                Id          = id,
                Root        = ClarionRoot.Normalise(clarionRoot),
                Version     = version,
                InstalledAt = DateTime.Today.ToString("yyyy-MM-dd"),
                Staged      = staged,
            });
            Write(doc);
        }

        /// <summary>
        /// Called once ApplyPendingUpdates has moved a staged payload into accessory/addins. The
        /// entry rejoins the disk reconciliation from here, which is also what corrects its version.
        /// </summary>
        public void ClearStaged(string clarionRoot, string id)
        {
            InstalledStore doc = ReadDocument();
            var entry = doc.Installed.FirstOrDefault(
                a => a.Id == id && ClarionRoot.Same(a.Root, clarionRoot));
            if (entry == null || !entry.Staged) return;
            entry.Staged = false;
            Write(doc);
        }

        public void MarkUninstalled(string clarionRoot, string id)
        {
            InstalledStore doc = ReadDocument();
            doc.Installed.RemoveAll(a => a.Id == id && ClarionRoot.Same(a.Root, clarionRoot));
            doc.LegacyUnclaimed.RemoveAll(a => a.Id == id);
            Write(doc);
        }

        // ---- reconciliation -------------------------------------------------------------------

        /// <summary>
        /// Drop entries whose folder no longer holds a manifest, and refresh the version from the
        /// manifest that IS there. Returns true if anything changed.
        ///
        /// Staged entries are exempt: by definition their files are still in the pending folder and
        /// have not reached accessory/addins yet, so a disk check would wrongly delete them.
        /// </summary>
        private static bool Reconcile(InstalledStore doc, string clarionRoot)
        {
            bool changed = false;
            var mine = doc.Installed.Where(a => ClarionRoot.Same(a.Root, clarionRoot)).ToList();

            foreach (var entry in mine)
            {
                if (entry.Staged) continue;

                string? manifest = FindManifest(clarionRoot, entry.Id);
                if (manifest == null)
                {
                    doc.Installed.Remove(entry);
                    changed = true;
                    continue;
                }

                // An addin whose manifest carries no version attribute is NOT unknown -- plenty of
                // manifests omit it (AddinFinder's own did until 0.7.0). Keep what we recorded
                // rather than treating the omission as a reason to distrust the entry.
                string onDisk = ReadIdentityVersion(manifest);
                if (onDisk.Length > 0 && onDisk != entry.Version)
                {
                    entry.Version = onDisk;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// Give this root any legacy (pre-v2) entry it can prove on disk.
        ///
        /// A v1 file records no root, and this process can only ever see one Clarion, so the entries
        /// cannot be attributed all at once. They wait in LegacyUnclaimed until a root that actually
        /// has the addin loads and claims them -- which each Clarion does the first time it runs.
        /// </summary>
        private static bool ClaimLegacy(InstalledStore doc, string clarionRoot)
        {
            if (doc.LegacyUnclaimed.Count == 0) return false;

            bool changed = false;
            foreach (var legacy in doc.LegacyUnclaimed.ToList())
            {
                string? manifest = FindManifest(clarionRoot, legacy.Id);
                if (manifest == null) continue;

                string onDisk = ReadIdentityVersion(manifest);
                doc.Installed.RemoveAll(a => a.Id == legacy.Id && ClarionRoot.Same(a.Root, clarionRoot));
                doc.Installed.Add(new InstalledAddin
                {
                    Id          = legacy.Id,
                    Root        = ClarionRoot.Normalise(clarionRoot),
                    Version     = onDisk.Length > 0 ? onDisk : legacy.Version,
                    InstalledAt = legacy.InstalledAt,
                    Staged      = false,
                });
                doc.LegacyUnclaimed.Remove(legacy);
                changed = true;
            }
            return changed;
        }

        /// <summary>The addin's manifest under this root, or null if the folder has none.</summary>
        private static string? FindManifest(string clarionRoot, string id)
        {
            try
            {
                string folder = Path.Combine(ClarionRoot.AddinsFolder(clarionRoot), id);
                if (!Directory.Exists(folder)) return null;
                return Directory.GetFiles(folder, "*.addin", SearchOption.TopDirectoryOnly).FirstOrDefault();
            }
            catch { return null; }
        }

        /// <summary>The version attribute of the manifest's Identity element, or "" if absent.</summary>
        private static string ReadIdentityVersion(string manifestPath)
        {
            try
            {
                string xml = File.ReadAllText(manifestPath, Encoding.UTF8);
                int i = xml.IndexOf("<Identity", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return "";
                int end = xml.IndexOf('>', i);
                if (end < 0) return "";
                string tag = xml.Substring(i, end - i);

                int v = tag.IndexOf("version=", StringComparison.OrdinalIgnoreCase);
                if (v < 0) return "";
                int q1 = tag.IndexOf('"', v);
                if (q1 < 0) return "";
                int q2 = tag.IndexOf('"', q1 + 1);
                if (q2 < 0) return "";
                return tag.Substring(q1 + 1, q2 - q1 - 1).Trim();
            }
            catch { return ""; }
        }

        // ---- persistence ----------------------------------------------------------------------

        private InstalledStore ReadDocument()
        {
            try
            {
                if (!File.Exists(_storePath)) return new InstalledStore();
                string json = File.ReadAllText(_storePath, Encoding.UTF8);
                InstalledStore doc = SimpleJsonParser.ParseStore(json);

                // v1 had no "version" and no per-entry root. Park those entries for claiming and
                // keep a copy of the old file for one release -- discarding it would show the user
                // an empty install list and invite reinstalling over folders already in place.
                if (doc.Version < 2 && doc.LegacyUnclaimed.Count > 0)
                {
                    try { File.Copy(_storePath, _backupPath, overwrite: true); } catch { }
                    doc.Version = 2;
                    Write(doc);
                }
                return doc;
            }
            catch { return new InstalledStore(); }
        }

        private void Write(InstalledStore doc)
        {
            try
            {
                doc.Version = 2;
                Directory.CreateDirectory(_storeDir);
                File.WriteAllText(_storePath, SimpleJsonParser.SerialiseStore(doc), Encoding.UTF8);
            }
            catch { /* a cache we cannot write is not worth failing an install over */ }
        }
    }
}
