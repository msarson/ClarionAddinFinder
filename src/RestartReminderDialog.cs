using System.Drawing;
using System.Windows.Forms;

namespace AddinFinder
{
    internal enum RestartReason { Installed, Updated, Removed, StagedUpdate, StagedRemoval }

    /// <summary>
    /// Informs the user that a Clarion restart is needed to activate addin changes.
    /// Shown after any operation that requires a restart. Suppressed once "don't show again" is ticked.
    /// </summary>
    internal class RestartReminderDialog : Form
    {
        private CheckBox _dontShowAgain;

        public bool DontShowAgain => _dontShowAgain.Checked;

        public RestartReminderDialog(string[] addinNames, RestartReason reason)
        {
            string body = BuildMessage(addinNames, reason);

            Text            = "Restart Required";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(440, 170);
            ShowInTaskbar   = false;
            Padding         = new Padding(0);

            // ── Blue header bar ───────────────────────────────────────────
            var header = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 56,
                BackColor = Color.FromArgb(0, 120, 215)   // Windows accent blue
            };

            var headerIcon = new PictureBox
            {
                Image    = SystemIcons.Information.ToBitmap(),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Size     = new Size(28, 28),
                Location = new Point(16, 14),
                BackColor= Color.Transparent
            };

            var headerTitle = new Label
            {
                Text      = "Restart Required",
                Font      = new Font(SystemFonts.MessageBoxFont.FontFamily, 11f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize  = true,
                Location  = new Point(52, 17)
            };

            header.Controls.Add(headerIcon);
            header.Controls.Add(headerTitle);

            // ── Body ──────────────────────────────────────────────────────
            var bodyLabel = new Label
            {
                Text      = body,
                Font      = new Font(SystemFonts.MessageBoxFont.FontFamily, 9f),
                Location  = new Point(20, 72),
                Size      = new Size(400, 44),
                AutoSize  = false
            };

            // ── Separator ─────────────────────────────────────────────────
            var sep = new Panel
            {
                BackColor = SystemColors.ControlLight,
                Location  = new Point(0, 122),
                Size      = new Size(440, 1)
            };

            // ── Footer ────────────────────────────────────────────────────
            _dontShowAgain = new CheckBox
            {
                Text     = "Don't show this message again",
                Font     = new Font(SystemFonts.MessageBoxFont.FontFamily, 8.5f),
                AutoSize = true,
                Location = new Point(16, 135)
            };

            var ok = new Button
            {
                Text         = "OK",
                DialogResult = DialogResult.OK,
                Size         = new Size(80, 26),
                Location     = new Point(344, 131),
                Font         = SystemFonts.MessageBoxFont
            };

            AcceptButton = ok;
            Controls.Add(header);
            Controls.Add(bodyLabel);
            Controls.Add(sep);
            Controls.Add(_dontShowAgain);
            Controls.Add(ok);
        }

        private static string BuildMessage(string[] names, RestartReason reason)
        {
            string list   = FormatNames(names);
            bool   plural = names.Length > 1;

            switch (reason)
            {
                case RestartReason.Installed:
                    return plural
                        ? $"In order to use {list}, you will need to restart the Clarion IDE."
                        : $"In order to use {list}, you will need to restart the Clarion IDE.";
                case RestartReason.Updated:
                    return plural
                        ? $"The updates to {list} will take effect after you restart the Clarion IDE."
                        : $"The update to {list} will take effect after you restart the Clarion IDE.";
                case RestartReason.Removed:
                    return plural
                        ? $"{list} have been removed. Please restart the Clarion IDE to complete the uninstall."
                        : $"{list} has been removed. Please restart the Clarion IDE to complete the uninstall.";
                case RestartReason.StagedUpdate:
                    return plural
                        ? $"Updates to {list} have been downloaded and will be applied the next time the Clarion IDE starts."
                        : $"The update to {list} has been downloaded and will be applied the next time the Clarion IDE starts.";
                case RestartReason.StagedRemoval:
                    return plural
                        ? $"{list} will be fully removed the next time the Clarion IDE starts."
                        : $"{list} will be fully removed the next time the Clarion IDE starts.";
                default:
                    return $"Please restart the Clarion IDE for changes to {list} to take effect.";
            }
        }

        /// <summary>Formats ["A"] → "A", ["A","B"] → "A and B", ["A","B","C"] → "A, B and C"</summary>
        private static string FormatNames(string[] names)
        {
            if (names.Length == 1) return names[0];
            if (names.Length == 2) return $"{names[0]} and {names[1]}";
            return string.Join(", ", names, 0, names.Length - 1) + " and " + names[names.Length - 1];
        }
    }
}
