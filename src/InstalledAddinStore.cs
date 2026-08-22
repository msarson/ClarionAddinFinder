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
        private readonly string _legacyPath;
        private readonly string _legacyBackupPath;

        public InstalledAddinStore() : this(DefaultStoreDir) { }

        /// <summary>
        /// Overrides where the store lives. Exists so tests can point at a scratch directory:
        /// SpecialFolder.ApplicationData goes through the Win32 shell API and ignores %APPDATA%,
        /// so there is no way to redirect it from outside the process.
        /// </summary>
        public InstalledAddinStore(string storeDir)
        {
            _storeDir = storeDir;

            // The v2 format lives in its OWN file. Versions up to 0.6.0 read installed.json by
            // looking for an "addins" key; handed a v2 document they conclude that nothing is
            // installed, and the next install they perform writes v1 back over the top, destroying
            // it. Since each Clarion carries its own copy of AddinFinder and updates on its own
            // schedule, an old and a new build routinely share this folder -- so the new format
            // must not occupy the filename the old one owns.
            _storePath        = Path.Combine(storeDir, "installed.v2.json");
            _legacyPath       = Path.Combine(storeDir, "installed.json");
            _legacyBackupPath = Path.Combine(storeDir, "installed.json.v1.bak");
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
            bool changed = Adopt(doc, clarionRoot);
            changed |= Reconcile(doc, clarionRoot);
            if (changed) Write(doc);

            return doc.Installed.Where(a => ClarionRoot.Same(a.Root, clarionRoot)).ToList();
        }

        public void MarkInstalled(string clarionRoot, string id, string version, bool staged,
                                  string publisher = "")
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
                Publisher   = publisher,
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

                // The manifest is the publisher's self-report, and it goes stale: FlattenCode has
                // shipped 1.0.1 through 1.0.3 with <Identity version="1.0"/> throughout. Taking it
                // as truth overwrote the version we actually installed with a lower one, and the
                // comparison against the registry then reported an update forever -- installing it
                // only to have the manifest reassert the old number on the next load.
                //
                // So the manifest may only ever move a version FORWARD. Higher than recorded means
                // something updated the addin behind our back, which is worth knowing. Lower or
                // equal means the publisher has not maintained the attribute, and what we installed
                // remains the better answer.
                string onDisk = ReadIdentityVersion(manifest);
                if (onDisk.Length > 0 &&
                    (entry.Version.Length == 0 || CompareDotted(onDisk, entry.Version) > 0))
                {
                    entry.Version = onDisk;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// Record every addin that is on disk under this root but missing from the store.
        ///
        /// Reconciliation alone only ever PRUNES, which makes the store a one-way cache and loses
        /// information the disk still has. Two cases need adopting, and the second is why this
        /// exists at all:
        ///
        /// - A v1 file recorded one entry per addin with no root, so an addin installed into three
        ///   Clarions appeared once. Attributing that entry to the first root to start would leave
        ///   the other two reporting the addin as not installed while it sat in their addins folder
        ///   -- and the pad would offer to install over a working copy.
        ///
        /// - Anything that put an addin there without going through us: a hand-unzipped copy, or
        ///   another installer. Reporting it as absent invites exactly the same overwrite.
        ///
        /// The id is the folder name, which is how installs are keyed, and the version comes from
        /// the manifest. A pre-v2 entry with the same id supplies the original install date, and is
        /// deliberately NOT consumed -- every root that has the addin deserves that date, and only
        /// the disk decides which roots those are.
        /// </summary>
        private static bool Adopt(InstalledStore doc, string clarionRoot)
        {
            string addinsFolder = ClarionRoot.AddinsFolder(clarionRoot);
            if (!Directory.Exists(addinsFolder)) return false;

            string[] folders;
            try { folders = Directory.GetDirectories(addinsFolder); }
            catch { return false; }

            bool changed = false;
            foreach (string folder in folders)
            {
                string id = Path.GetFileName(folder);
                if (doc.Installed.Any(a => a.Id == id && ClarionRoot.Same(a.Root, clarionRoot))) continue;

                string? manifest = FindManifest(clarionRoot, id);
                if (manifest == null) continue;

                var legacy    = doc.LegacyUnclaimed.FirstOrDefault(a => a.Id == id);
                string onDisk = ReadIdentityVersion(manifest);

                doc.Installed.Add(new InstalledAddin
                {
                    Id          = id,
                    Root        = ClarionRoot.Normalise(clarionRoot),
                    Version     = onDisk.Length > 0 ? onDisk : (legacy != null ? legacy.Version : ""),
                    InstalledAt = legacy != null
                                    ? legacy.InstalledAt
                                    : DateTime.Today.ToString("yyyy-MM-dd"),
                    Staged      = false,
                });
                changed = true;
            }
            return changed;
        }

        /// <summary>-1 if a &lt; b, 0 if equal, 1 if a &gt; b. Component-wise, so 1.10 beats 1.9.</summary>
        private static int CompareDotted(string a, string b)
        {
            string[] pa = (a ?? "").Split('.'), pb = (b ?? "").Split('.');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = i < pa.Length && int.TryParse(pa[i], out var xv) ? xv : 0;
                int y = i < pb.Length && int.TryParse(pb[i], out var yv) ? yv : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
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
                if (File.Exists(_storePath))
                    return SimpleJsonParser.ParseStore(File.ReadAllText(_storePath, Encoding.UTF8));

                // No v2 file yet: seed one from installed.json and leave that file alone from here.
                // ParseStore reads either shape, which matters because 0.7.0 wrote a v2 document
                // into installed.json -- on those machines the seed already carries roots.
                if (!File.Exists(_legacyPath)) return new InstalledStore();

                InstalledStore doc = SimpleJsonParser.ParseStore(
                    File.ReadAllText(_legacyPath, Encoding.UTF8));
                doc.Version = 2;
                Write(doc);
                RestoreLegacyFileForOlderBuilds();
                return doc;
            }
            catch { return new InstalledStore(); }
        }

        /// <summary>
        /// Undo 0.7.0's rewrite of installed.json.
        ///
        /// 0.7.0 replaced that file with a v2 document. Builds up to 0.6.0 parse it as "nothing
        /// installed" and overwrite it on their next install, so any Clarion still running an older
        /// AddinFinder was left broken by the upgrade of a different Clarion. Its backup is the
        /// original v1 content, so putting it back restores those builds. We never write that file
        /// again -- v2 lives in installed.v2.json.
        /// </summary>
        private void RestoreLegacyFileForOlderBuilds()
        {
            try
            {
                if (!File.Exists(_legacyBackupPath)) return;
                InstalledStore current = SimpleJsonParser.ParseStore(
                    File.ReadAllText(_legacyPath, Encoding.UTF8));
                if (current.Version < 2) return;   // still genuine v1, nothing to undo
                File.Copy(_legacyBackupPath, _legacyPath, overwrite: true);
            }
            catch { /* best effort -- the v2 store is already written and authoritative */ }
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
