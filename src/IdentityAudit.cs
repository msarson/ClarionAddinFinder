using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AddinFinder
{
    /// <summary>One Identity found in more than one addin folder.</summary>
    public class IdentityClash
    {
        public string IdentityName { get; set; } = "";

        /// <summary>Every folder declaring it, in the order Clarion would scan them.</summary>
        public List<string> Folders { get; set; } = new List<string>();

        public string Summary =>
            IdentityName + ": " + string.Join(" and ",
                Folders.Select(f => Path.GetFileName(f)).ToArray());
    }

    /// <summary>
    /// Finds addins that will stop Clarion starting.
    ///
    /// Clarion loads every subfolder of accessory\addins at start-up and refuses to start AT ALL if
    /// two of them declare the same &lt;Identity name&gt; -- the user gets "Identity name used by
    /// multiple addins" and no IDE.
    ///
    /// Installing through Addin Finder cannot cause this: AddinInstaller refuses beforehand. This is
    /// for every other way a machine ends up in that state -- another product's installer, a copy
    /// taken from a different Clarion, a hand-unzipped folder. Clarion Assistant's installer writing
    /// the Markdown Editor to accessory\addins\MarkdownEditor, while Addin Finder installs the same
    /// addin to accessory\addins\ClarionMarkdownEditor, is the case that actually cost a user their
    /// IDE.
    ///
    /// Reports only. Removing one side would mean choosing on the user's behalf between two things
    /// we may not have put there, so both paths are named and the choice is theirs.
    /// </summary>
    public static class IdentityAudit
    {
        /// <summary>
        /// Identities declared by more than one folder under this Clarion. Empty when all is well,
        /// which is the normal case -- so callers can treat any result as worth saying out loud.
        /// </summary>
        public static List<IdentityClash> Scan(string clarionRoot)
        {
            var clashes = new List<IdentityClash>();
            if (string.IsNullOrEmpty(clarionRoot)) return clashes;

            string addinsFolder = ClarionRoot.AddinsFolder(clarionRoot);
            if (!Directory.Exists(addinsFolder)) return clashes;

            string[] folders;
            try { folders = Directory.GetDirectories(addinsFolder); }
            catch { return clashes; }

            // Identity -> folders declaring it. Case-insensitive: Clarion does not distinguish, and
            // two folders differing only in case would break it just the same.
            var byIdentity = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                foreach (string identity in IdentitiesIn(folder))
                {
                    List<string> found;
                    if (!byIdentity.TryGetValue(identity, out found))
                        byIdentity[identity] = found = new List<string>();

                    // A folder declaring the same Identity twice is its own problem, not a clash
                    // between folders -- do not report it as one.
                    if (!found.Contains(folder)) found.Add(folder);
                }
            }

            foreach (var pair in byIdentity)
                if (pair.Value.Count > 1)
                    clashes.Add(new IdentityClash { IdentityName = pair.Key, Folders = pair.Value });

            return clashes.OrderBy(c => c.IdentityName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>One line for the status area. Names the identity and the folders when there is
        /// only one clash, because that is enough for the user to act without opening anything.</summary>
        public static string ShortWarning(List<IdentityClash> clashes)
            => clashes.Count == 1
                ? "Warning: " + clashes[0].Summary + " — Clarion will not start until one is removed"
                : "Warning: " + clashes.Count + " duplicate addin identities — Clarion will not "
                  + "start until they are resolved";

        /// <summary>
        /// The full account, for copying out. Gives whole paths rather than folder names: the user
        /// has to go and delete one of them, and the pad is the only place that knows which.
        /// </summary>
        public static string FullWarning(List<IdentityClash> clashes)
        {
            var lines = new List<string>
            {
                "Clarion will not start while two addins declare the same identity.",
                ""
            };
            foreach (var clash in clashes)
            {
                lines.Add("Identity \"" + clash.IdentityName + "\" is declared by:");
                foreach (string folder in clash.Folders) lines.Add("    " + folder);
                lines.Add("");
            }
            lines.Add("Remove one of each pair, then restart Clarion.");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Every Identity declared by the manifests in one folder.
        ///
        /// A folder with no manifest, or one we cannot read, is not evidence of anything and is
        /// passed over silently. This runs on every refresh; it must not become a source of noise
        /// about folders that are nothing to do with us.
        /// </summary>
        private static IEnumerable<string> IdentitiesIn(string folder)
        {
            string[] manifests;
            try { manifests = Directory.GetFiles(folder, "*.addin", SearchOption.TopDirectoryOnly); }
            catch { yield break; }

            foreach (string manifest in manifests)
            {
                // The id and the Identity are not the same string -- FlattenCode publishes as
                // FlattenCode with <Identity name="FlattenCode.Addin"/> -- so only the file itself
                // can say what a folder declares.
                string name = AddinInstaller.ReadIdentityName(manifest);
                if (name.Length > 0) yield return name;
            }
        }
    }
}
