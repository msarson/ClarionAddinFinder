using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AddinFinder
{
    /// <summary>Settings that belong to one Clarion installation.</summary>
    public class ClarionSettings
    {
        public string Root                   { get; set; } = "";
        public int    AcceptedTermsVersion   { get; set; }
        public string LastSeenVersion        { get; set; } = "";
        public List<string> AcknowledgedPublishers { get; set; } = new List<string>();
    }

    /// <summary>
    /// User preferences, stored at %APPDATA%\ClarionAddinFinder\settings.v2.json
    ///
    /// Consent and "what has changed" are recorded PER CLARION ROOT, for the same reason installed
    /// addins are: each Clarion has its own copy of Addin Finder, its own addins folder, and its own
    /// update schedule. Accepting something while working in Clarion 11 says nothing about Clarion
    /// 12 -- and since 12 may still be running an older build entirely, it has not even been told
    /// yet what it would be accepting.
    ///
    /// The v2 document lives in its OWN file. Builds up to 0.7.1 read settings.json by searching the
    /// raw text for the literal "suppressRestartReminder": true, so a document they cannot match
    /// leaves that preference silently off for them. Same lesson as installed.json: a new format
    /// does not get to occupy the filename an older build owns.
    /// </summary>
    public class AddinFinderSettings
    {
        private static string DefaultStoreDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClarionAddinFinder");

        /// <summary>
        /// Where the settings file lives. Overridable so tests can point at a scratch directory:
        /// SpecialFolder.ApplicationData goes through the Win32 shell API and ignores %APPDATA%,
        /// so there is no way to redirect it from outside the process -- and a test without a seam
        /// writes to the developer's own settings.
        /// </summary>
        private string _storeDir = DefaultStoreDir;

        private string StorePath  => Path.Combine(_storeDir, "settings.v2.json");
        private string LegacyPath => Path.Combine(_storeDir, "settings.json");

        /// <summary>Current disclaimer wording. Bump when the text materially changes.</summary>
        public const int CurrentTermsVersion = 1;

        /// <summary>
        /// Whether the restart reminder is suppressed. Global on purpose: it is a "stop telling me
        /// this" preference about the tool's own chattiness, not a decision about trusting anyone,
        /// and re-asking per Clarion would be the nagging it exists to prevent.
        /// </summary>
        public bool SuppressRestartReminder { get; set; }

        /// <summary>The Clarion these consent values belong to.</summary>
        public string ClarionRootPath { get; private set; } = "";

        private ClarionSettings       _mine   = new ClarionSettings();
        private List<ClarionSettings> _others = new List<ClarionSettings>();

        // ---- per-Clarion values ----------------------------------------------------------------

        public int AcceptedTermsVersion
        {
            get => _mine.AcceptedTermsVersion;
            set => _mine.AcceptedTermsVersion = value;
        }

        public string LastSeenVersion
        {
            get => _mine.LastSeenVersion;
            set => _mine.LastSeenVersion = value;
        }

        public List<string> AcknowledgedPublishers => _mine.AcknowledgedPublishers;

        public bool HasAcceptedTerms => AcceptedTermsVersion >= CurrentTermsVersion;

        public bool HasAcknowledged(string publisherId)
            => _mine.AcknowledgedPublishers.Any(
                   p => string.Equals(p, publisherId ?? "", StringComparison.OrdinalIgnoreCase));

        public void Acknowledge(string publisherId)
        {
            if (HasAcknowledged(publisherId)) return;
            _mine.AcknowledgedPublishers.Add(publisherId ?? "");
        }

        // ---- persistence -----------------------------------------------------------------------

        /// <summary>Settings for one Clarion. Seeds from the pre-v2 file when there is no v2 yet.</summary>
        public static AddinFinderSettings Load(string clarionRoot)
            => Load(clarionRoot, DefaultStoreDir);

        /// <summary>Loads from a specific directory. See the note on _storeDir.</summary>
        public static AddinFinderSettings Load(string clarionRoot, string storeDir)
        {
            var s = new AddinFinderSettings
            {
                ClarionRootPath = ClarionRoot.Normalise(clarionRoot ?? ""),
                _storeDir       = storeDir,
            };

            try
            {
                if (File.Exists(s.StorePath))
                    SimpleJsonParser.FillSettings(s, File.ReadAllText(s.StorePath, Encoding.UTF8));
                else if (File.Exists(s.LegacyPath))
                    SimpleJsonParser.FillSettings(s, File.ReadAllText(s.LegacyPath, Encoding.UTF8));
            }
            catch { }

            var mine = s._others.FirstOrDefault(c => ClarionRoot.Same(c.Root, s.ClarionRootPath));
            s._others.RemoveAll(c => ClarionRoot.Same(c.Root, s.ClarionRootPath));
            s._mine = mine ?? new ClarionSettings { Root = s.ClarionRootPath };
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(_storeDir);
                _mine.Root = ClarionRootPath;

                // Other Clarions' entries are read and written back untouched -- two IDEs can be
                // open at once, and this must not be the thing that forgets one of them.
                var all = new List<ClarionSettings>(_others) { _mine };
                File.WriteAllText(StorePath, SimpleJsonParser.SerialiseSettings(this, all), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>Used by the parser while reading a v2 document.</summary>
        internal void SetPerClarion(List<ClarionSettings> entries) => _others = entries;

        /// <summary>
        /// Adopts values from a pre-v2 document, which had no notion of a Clarion.
        ///
        /// They are attributed to the root being loaded: it is the only one visible from here, and
        /// it is the one the user was in when they set them. Other Clarions start clean, which is
        /// the correct outcome -- consent given in one was never consent for another.
        /// </summary>
        internal void AdoptLegacy(int acceptedTerms, string lastSeen, List<string> publishers)
        {
            _others.RemoveAll(c => ClarionRoot.Same(c.Root, ClarionRootPath));
            _others.Add(new ClarionSettings
            {
                Root                   = ClarionRootPath,
                AcceptedTermsVersion   = acceptedTerms,
                LastSeenVersion        = lastSeen,
                AcknowledgedPublishers = publishers,
            });
        }
    }
}
