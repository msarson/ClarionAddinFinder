using System.Drawing;
using System.Windows.Forms;

namespace AddinFinder
{
    /// <summary>
    /// One-time modal informing the user a Clarion restart is needed to activate addin changes.
    /// </summary>
    internal class RestartReminderDialog : Form
    {
        private CheckBox _dontShowAgain;

        public bool DontShowAgain => _dontShowAgain.Checked;

        public RestartReminderDialog(string addinNames)
        {
            Text            = "Restart Required";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            Width           = 420;
            Height          = 160;
            ShowInTaskbar   = false;

            var icon = new PictureBox
            {
                Image    = SystemIcons.Information.ToBitmap(),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Size     = new Size(32, 32),
                Location = new Point(12, 16)
            };

            var message = new Label
            {
                Text      = $"In order to use {addinNames}, you will need to restart the Clarion IDE.",
                Location  = new Point(52, 12),
                Size      = new Size(342, 40),
                AutoSize  = false
            };

            _dontShowAgain = new CheckBox
            {
                Text     = "Don't show this message again",
                Location = new Point(52, 60),
                AutoSize = true
            };

            var ok = new Button
            {
                Text         = "OK",
                DialogResult = DialogResult.OK,
                Location     = new Point(322, 88),
                Width        = 75
            };

            AcceptButton = ok;
            Controls.Add(icon);
            Controls.Add(message);
            Controls.Add(_dontShowAgain);
            Controls.Add(ok);
        }
    }
}
