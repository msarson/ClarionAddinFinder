using System;
using System.IO;
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

        public static AddinFinderSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    var s = new AddinFinderSettings();
                    // Simple parse — only one field for now
                    if (json.Contains("\"suppressRestartReminder\": true"))
                        s.SuppressRestartReminder = true;
                    return s;
                }
            }
            catch { }
            return new AddinFinderSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                string json = "{\r\n  \"suppressRestartReminder\": "
                    + (SuppressRestartReminder ? "true" : "false")
                    + "\r\n}\r\n";
                File.WriteAllText(SettingsPath, json, Encoding.UTF8);
            }
            catch { }
        }
    }
}
