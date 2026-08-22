using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AddinFinder
{
    /// <summary>
    /// Decides who should be told about the move to publisher-hosted addin lists.
    ///
    /// Separated from the panel that shows it so the decision can be reasoned about, and tested,
    /// without a window.
    /// </summary>
    internal static class WhatsChangedNotice
    {
        /// <summary>The release this notice describes. Only shown to people arriving from earlier.</summary>
        public const string FederationVersion = "0.8.0";

        /// <summary>Version of the running assembly, as major.minor.build.</summary>
        public static string CurrentVersion()
        {
            try
            {
                var v = typeof(WhatsChangedNotice).Assembly.GetName().Version;
                return v == null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { return ""; }
        }

        /// <summary>
        /// Whether to show the notice, recording the current version either way so it is never shown
        /// twice.
        ///
        /// An empty LastSeenVersion means either a first-ever install or an upgrade from a build
        /// that predates the field. Prior state on disk is what tells those apart -- someone
        /// installing for the first time has no previous behaviour to be told about, and a "what has
        /// changed" notice to them is noise dressed as courtesy.
        /// </summary>
        public static bool ShouldShow(AddinFinderSettings settings, bool hasEarlierState)
        {
            string current = CurrentVersion();
            if (current.Length == 0) return false;
            if (settings.LastSeenVersion == current) return false;

            bool upgrading = settings.LastSeenVersion.Length > 0
                ? IsBefore(settings.LastSeenVersion, FederationVersion)
                : hasEarlierState;

            settings.LastSeenVersion = current;
            settings.Save();
            return upgrading;
        }

        /// <summary>Component-wise dotted compare, so 0.10.0 is correctly after 0.9.0.</summary>
        public static bool IsBefore(string a, string b)
        {
            string[] pa = (a ?? "").Split('.'), pb = (b ?? "").Split('.');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = i < pa.Length && int.TryParse(pa[i], out var xv) ? xv : 0;
                int y = i < pb.Length && int.TryParse(pb[i], out var yv) ? yv : 0;
                if (x != y) return x < y;
            }
            return false;
        }

        /// <summary>Whether this user was using Addin Finder before the version that records it.</summary>
        public static bool HasEarlierState(int installedCount)
        {
            if (installedCount > 0) return true;
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ClarionAddinFinder");
                return File.Exists(Path.Combine(dir, "installed.json"))
                    || File.Exists(Path.Combine(dir, "installed.v2.json"))
                    || File.Exists(Path.Combine(dir, "settings.json"));
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// The federation explanation, shown INSIDE the pad in place of the usual contents.
    ///
    /// Not a modal dialog. SharpDevelop restores whichever pads were open when Clarion last closed,
    /// so a pad that is left docked is created during start-up -- and a modal there interrupts the
    /// IDE coming up, before the user has asked for anything. Occupying the pad instead means the
    /// message is waiting when they look at it, and dismissing it reveals the normal contents,
    /// already loaded underneath.
    /// </summary>
    internal class WhatsChangedPanel : Panel
    {
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
            "     •  The list is grouped by publisher, so you can see whose code you are " +
            "installing.\r\n" +
            "     •  The first time you install from a publisher, you are told who they are.\r\n" +
            "     •  If a publisher cannot be reached, their addins are still listed from the " +
            "last known copy rather than disappearing.\r\n" +
            "\r\n" +
            "Nothing you have already installed is affected, and there is nothing you need to do.";

        /// <summary>
        /// Puts the notice over a pad's contents. Dismissing removes it, revealing what was
        /// underneath -- which has been loading in the meantime, so nothing is waited for twice.
        /// </summary>
        public static void ShowOver(Panel host)
        {
            var panel = new WhatsChangedPanel();
            host.Controls.Add(panel);
            panel.BringToFront();
        }

        private WhatsChangedPanel()
        {
            Dock      = DockStyle.Fill;
            BackColor = SystemColors.Window;
            Padding   = new Padding(24, 20, 24, 16);
            AutoScroll = true;

            var heading = new Label
            {
                Text      = "What has changed in Addin Finder",
                Dock      = DockStyle.Top,
                Height    = 30,
                Font      = new Font((SystemFonts.DialogFont ?? SystemFonts.DefaultFont).FontFamily,
                                     11f, FontStyle.Bold),
                ForeColor = SystemColors.WindowText,
            };

            var body = new Label
            {
                Text     = Body,
                Dock     = DockStyle.Top,
                Height   = 300,
                AutoSize = false,
            };

            var link = new LinkLabel
            {
                Text   = "How publishing works",
                Dock   = DockStyle.Top,
                Height = 26,
            };
            link.LinkClicked += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(
                          "https://github.com/msarson/clarion-addin-registry"); }
                catch { }
            };

            var buttonRow = new Panel { Dock = DockStyle.Top, Height = 44 };
            var dismiss = new Button
            {
                Text     = "Continue",
                Size     = new Size(110, 28),
                Location = new Point(0, 8),
            };
            dismiss.Click += (s, e) =>
            {
                var host = Parent;
                host?.Controls.Remove(this);
                Dispose();
            };
            buttonRow.Controls.Add(dismiss);

            // Added bottom-up: with DockStyle.Top the last added sits nearest the top.
            Controls.Add(buttonRow);
            Controls.Add(link);
            Controls.Add(body);
            Controls.Add(heading);
        }
    }
}
