using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AddinFinder
{
    /// <summary>
    /// Persistent user preferences stored at %APPDATA%\ClarionAddinFinder\settings.json
    /// </summary>
    public class AddinFinderSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClarionAddinFinder", "settings.json");

        public bool SuppressRestartReminder { get; set; } = false;

        /// <summary>
        /// Version of the install disclaimer the user has accepted; 0 means never shown.
        ///
        /// Versioned rather than a plain bool so the disclaimer can be shown again if what it says
        /// materially changes. An acknowledgement is only meaningful for the wording it was given
        /// for, and silently treating an old acceptance as covering new terms would make it
        /// worthless.
        /// </summary>
        public int AcceptedTermsVersion { get; set; } = 0;

        /// <summary>Current disclaimer wording. Bump when the text materially changes.</summary>
        public const int CurrentTermsVersion = 1;

        public bool HasAcceptedTerms => AcceptedTermsVersion >= CurrentTermsVersion;

        /// <summary>
        /// Publisher ids the user has knowingly installed from before. "" is a real member of this
        /// set: it stands for the unidentified source behind legacy and hand-placed addins.
        ///
        /// Tracked per publisher because that is the shape of the actual decision -- trusting one
        /// publisher says nothing about the next, and a registry that grows must not quietly opt a
        /// user into publishers added long after they accepted anything.
        /// </summary>
        public List<string> AcknowledgedPublishers { get; set; } = new List<string>();

        public bool HasAcknowledged(string publisherId)
            => AcknowledgedPublishers.Any(p => string.Equals(p, publisherId ?? "",
                                                             StringComparison.OrdinalIgnoreCase));

        public void Acknowledge(string publisherId)
        {
            if (HasAcknowledged(publisherId)) return;
            AcknowledgedPublishers.Add(publisherId ?? "");
        }

        public static AddinFinderSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return SimpleJsonParser.ParseSettings(File.ReadAllText(SettingsPath, Encoding.UTF8));
            }
            catch { }
            return new AddinFinderSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, SimpleJsonParser.SerialiseSettings(this), Encoding.UTF8);
            }
            catch { }
        }
    }
}
