using System;
using System.Drawing;
using System.Windows.Forms;

namespace AddinFinder
{
    /// <summary>
    /// Shown once, before the user's first addin install.
    ///
    /// This is informed consent, not legal cover -- every addin here is MIT and already disclaims
    /// warranty. The point is that someone can see what they are trusting before they trust it: the
    /// registry approves publishers by identity, nobody reads their code, and a Clarion addin runs
    /// in-process with the user's full privileges.
    ///
    /// Accepting is recorded as a version (AddinFinderSettings.AcceptedTermsVersion) so the dialog
    /// can return if the wording materially changes.
    /// </summary>
    internal class InstallDisclaimerDialog : Form
    {
        private const string Body =
            "Addins are written and published by third parties, not by Addin Finder.\r\n" +
            "\r\n" +
            "A Clarion addin runs INSIDE the IDE, in the same process, with the same privileges as " +
            "you. It can read and change anything you can, on this machine and on any network you " +
            "have access to.\r\n" +
            "\r\n" +
            "Before you install anything, understand that:\r\n" +
            "\r\n" +
            "  •  Addin Finder does not review, test, scan or sign addin code.\r\n" +
            "  •  Publishers are approved by identity — who they are, and that the code comes " +
            "from them. That is not a judgement about quality or safety.\r\n" +
            "  •  Each addin is covered by its own licence, which will almost certainly disclaim " +
            "any warranty.\r\n" +
            "  •  Problems with an addin are for its publisher, not for Addin Finder.\r\n" +
            "\r\n" +
            "Every addin listed is open source, so its code can be read before you install it. The " +
            "publisher is shown against each one.";

        public InstallDisclaimerDialog()
        {
            Text            = "Before you install an addin";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(520, 340);
            ShowInTaskbar   = false;

            var icon = new PictureBox
            {
                Image    = SystemIcons.Information.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point(16, 16),
            };

            var text = new Label
            {
                Text      = Body,
                Location  = new Point(64, 16),
                Size      = new Size(440, 250),
                AutoSize  = false,
            };

            var accept = new Button
            {
                Text         = "I understand",
                DialogResult = DialogResult.OK,
                Location     = new Point(292, 288),
                Size         = new Size(100, 26),
            };

            var cancel = new Button
            {
                Text         = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location     = new Point(402, 288),
                Size         = new Size(100, 26),
            };

            Controls.Add(icon);
            Controls.Add(text);
            Controls.Add(accept);
            Controls.Add(cancel);

            AcceptButton = accept;
            CancelButton = cancel;
        }

        /// <summary>
        /// Ensures the user has seen the current disclaimer. Returns false if they declined, in which
        /// case the install must not proceed.
        /// </summary>
        public static bool EnsureAccepted(IWin32Window? owner, AddinFinderSettings settings)
        {
            if (settings.HasAcceptedTerms) return true;

            using (var dialog = new InstallDisclaimerDialog())
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
            }

            settings.AcceptedTermsVersion = AddinFinderSettings.CurrentTermsVersion;
            settings.Save();
            return true;
        }
    }
}
