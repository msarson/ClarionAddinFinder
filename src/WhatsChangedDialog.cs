using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace AddinFinder
{
    /// <summary>
    /// Explains the move to publisher-hosted addin lists, once, to people upgrading into it.
    ///
    /// The change is structural rather than cosmetic -- where addins come from, and who decides
    /// what appears -- so it is worth a sentence of explanation rather than the list quietly
    /// rearranging itself into groups one day.
    ///
    /// Deliberately NOT shown to a first-time installer. They have no previous behaviour to be told
    /// about, and a "what has changed" notice to someone with nothing to compare it to is noise
    /// dressed as courtesy.
    /// </summary>
    internal class WhatsChangedDialog : Form
    {
        /// <summary>The release this notice describes. Only shown to people arriving from earlier.</summary>
        private const string FederationVersion = "0.8.0";

        private const string Body =
            "Addins are now published by their authors directly.\r\n" +
            "\r\n" +
            "Until now every addin, and every new version of one, had to be added to a single " +
            "central list before anyone could install it. That put one person in the path of " +
            "everybody else's releases — and it fell hardest on the publishers already doing the " +
            "work, who had to ask permission again for each new addin they wrote.\r\n" +
            "\r\n" +
            "The registry now records PUBLISHERS. Each keeps their own list of addins and updates " +
            "it whenever they like, without waiting on anyone. Being listed says who a publisher " +
            "is and that their addins come from them — it is not a review of their code, and it " +
            "never was.\r\n" +
            "\r\n" +
            "What you will notice:\r\n" +
            "\r\n" +
            "  •  The list is grouped by publisher, so you can see whose code you are installing.\r\n" +
            "  •  The first time you install from a publisher, you are told who they are.\r\n" +
            "  •  If a publisher cannot be reached, their addins are still listed from the last " +
            "known copy rather than vanishing.\r\n" +
            "\r\n" +
            "Nothing you have already installed is affected, and there is nothing you need to do.";

        private WhatsChangedDialog()
        {
            Text            = "What has changed in Addin Finder";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            ShowInTaskbar   = false;
            ClientSize      = new Size(540, 372);

            Controls.Add(new PictureBox
            {
                Image    = SystemIcons.Information.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point(16, 16),
            });

            Controls.Add(new Label
            {
                Text     = Body,
                Location = new Point(64, 16),
                Size     = new Size(452, 300),
            });

            var link = new LinkLabel
            {
                Text     = "How publishing works",
                Location = new Point(64, 326),
                Size     = new Size(180, 20),
            };
            link.LinkClicked += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(
                          "https://github.com/msarson/clarion-addin-registry"); }
                catch { }
            };
            Controls.Add(link);

            var ok = new Button
            {
                Text         = "OK",
                DialogResult = DialogResult.OK,
                Location     = new Point(416, 322),
                Size         = new Size(100, 26),
            };
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
        }

        /// <summary>Version of the running assembly, as major.minor.build.</summary>
        private static string CurrentVersion()
        {
            try
            {
                var v = typeof(WhatsChangedDialog).Assembly.GetName().Version;
                return v == null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { return ""; }
        }

        /// <summary>
        /// Shows the notice if this user is arriving at the federated release from an earlier one,
        /// then records the version either way so it is never shown twice.
        /// </summary>
        public static void ShowIfUpgraded(IWin32Window? owner, AddinFinderSettings settings,
                                          bool hasEarlierState)
        {
            string current = CurrentVersion();
            if (current.Length == 0) return;
            if (settings.LastSeenVersion == current) return;

            // An empty LastSeenVersion means either a first-ever install or an upgrade from a build
            // that predates the field. Prior state on disk -- addins already installed, settings
            // already saved -- is what tells those two apart.
            bool upgrading = settings.LastSeenVersion.Length > 0
                ? IsBefore(settings.LastSeenVersion, FederationVersion)
                : hasEarlierState;

            settings.LastSeenVersion = current;
            settings.Save();

            if (upgrading)
                using (var dialog = new WhatsChangedDialog())
                    dialog.ShowDialog(owner);
        }

        /// <summary>Component-wise dotted compare, so 0.10.0 is correctly after 0.9.0.</summary>
        private static bool IsBefore(string a, string b)
        {
            string[] pa = a.Split('.'), pb = b.Split('.');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = i < pa.Length && int.TryParse(pa[i], out var xv) ? xv : 0;
                int y = i < pb.Length && int.TryParse(pb[i], out var yv) ? yv : 0;
                if (x != y) return x < y;
            }
            return false;
        }
    }
}
