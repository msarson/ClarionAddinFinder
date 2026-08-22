using System;
using System.IO;

namespace AddinFinder
{
    /// <summary>
    /// Resolves the Clarion installation this addin is loaded into.
    ///
    /// AddinFinder only ever sees ONE Clarion per process -- the one whose accessory/addins folder
    /// it was loaded from. That is deliberate and correct: every install, uninstall and store lookup
    /// must act on this Clarion, not on some other copy on the machine.
    /// </summary>
    public static class ClarionRoot
    {
        /// <summary>The Clarion root for the currently loaded assembly, or null if it cannot be determined.</summary>
        public static string? Resolve()
        {
            try
            {
                // ...\<ClarionRoot>\accessory\addins\<AddinFolder>\AddinFinder.dll -> up three
                string asmDir = Path.GetDirectoryName(typeof(ClarionRoot).Assembly.Location)!;
                string candidate = Path.GetFullPath(Path.Combine(asmDir, "..", "..", ".."));
                return Directory.Exists(Path.Combine(candidate, "bin")) ? candidate : null;
            }
            catch { return null; }
        }

        /// <summary>Canonical form for comparison: full path, no trailing separator.</summary>
        public static string Normalise(string root)
        {
            if (string.IsNullOrEmpty(root)) return "";
            try
            {
                return Path.GetFullPath(root)
                           .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch { return root; }
        }

        public static bool Same(string a, string b)
            => string.Equals(Normalise(a), Normalise(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>The accessory/addins folder for a Clarion root.</summary>
        public static string AddinsFolder(string root)
            => Path.Combine(root, "accessory", "addins");
    }
}
