using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AddinFinder
{
    /// <summary>
    /// Tracks which addins are installed locally.
    /// Stored at %APPDATA%\ClarionAddinFinder\installed.json
    /// </summary>
    public class InstalledAddinStore
    {
        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClarionAddinFinder", "installed.json");

        public List<InstalledAddin> Load()
        {
            if (!File.Exists(StorePath))
                return new List<InstalledAddin>();
            return SimpleJsonParser.ParseInstalled(File.ReadAllText(StorePath, Encoding.UTF8));
        }

        public void Save(List<InstalledAddin> addins)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, SimpleJsonParser.SerialiseInstalled(addins), Encoding.UTF8);
        }

        public void MarkInstalled(string id, string version)
        {
            var list = Load();
            list.RemoveAll(a => a.Id == id);
            list.Add(new InstalledAddin
            {
                Id = id,
                Version = version,
                InstalledAt = DateTime.Today.ToString("yyyy-MM-dd")
            });
            Save(list);
        }

        public void MarkUninstalled(string id)
        {
            var list = Load();
            list.RemoveAll(a => a.Id == id);
            Save(list);
        }
    }
}
