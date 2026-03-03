using System.Drawing;
using System.IO;
using System.Reflection;
using ICSharpCode.Core;

namespace AddinFinder
{
    public class RegisterIconsCommand : AbstractCommand
    {
        public override void Run()
        {
            // Register icon
            using (var stream = Assembly.GetExecutingAssembly()
                       .GetManifestResourceStream("AddinFinder.Resources.AddinFinderIcon.png"))
            {
                if (stream != null)
                    ResourceService.RegisterNeutralImages(
                        new EmbeddedIconManager("AddinFinder.AddinFinderIcon", new Bitmap(stream)));
            }

            // Apply any staged addin updates as early as possible
            try
            {
                string asmDir    = Path.GetDirectoryName(typeof(RegisterIconsCommand).Assembly.Location)!;
                string clarionRoot = Path.GetFullPath(Path.Combine(asmDir, "..", "..", ".."));
                if (Directory.Exists(Path.Combine(clarionRoot, "bin")))
                    new AddinInstaller(clarionRoot, new InstalledAddinStore()).ApplyPendingUpdates();
            }
            catch { /* non-fatal */ }
        }
    }

    internal sealed class EmbeddedIconManager : System.Resources.ResourceManager
    {
        private readonly string _key;
        private readonly Bitmap _bitmap;
        public EmbeddedIconManager(string key, Bitmap bitmap) : base(key, Assembly.GetExecutingAssembly()) { _key = key; _bitmap = bitmap; }
        public override object GetObject(string name) => name == _key ? _bitmap : null;
        public override object GetObject(string name, System.Globalization.CultureInfo culture) => GetObject(name);
    }
}
