using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AddinFinder

{
    /// <summary>
    /// Consent before installing, in two parts that carry different information.
    ///
    /// The GENERAL terms -- what an addin can do, and that nobody reviews them -- describe the
    /// system, and are shown once. The PUBLISHER section names who is about to run code on this
    /// machine, and is shown the first time the user installs from each publisher, because that is
    /// the shape of the actual decision: trusting one publisher says nothing about the next, and a
    /// registry that grows must not quietly opt someone into publishers added long after.
    ///
    /// The split is deliberate. Repeating the same warning is how people are taught to click
    /// through it -- the second identical dialog makes consent weaker, not stronger. So the part
    /// that repeats is the part that is different every time: a name, an account, a link to read.
    /// </summary>
    internal class InstallDisclaimerDialog : Form
    {
        private const string GeneralTerms =
            "A Clarion addin runs INSIDE the IDE, in the same process, with the same privileges as " +
            "you. It can read and change anything you can, on this machine and on any network you " +
            "have access to.\r\n" +
            "\r\n" +
            "  •  Addin Finder does not review, test, scan or sign addin code.\r\n" +
            "  •  Publishers are listed by identity — who they are, and that the code comes from " +
            "them. That is not a judgement about quality or safety.\r\n" +
            "  •  Each addin is covered by its own licence, which will almost certainly disclaim " +
            "any warranty.\r\n" +
            "  •  Problems with an addin are for its publisher, not for Addin Finder.";

        private InstallDisclaimerDialog(string? generalTerms, string publisherHeading,
                                        string publisherDetail, string? publisherUrl)
        {
            Text            = generalTerms != null ? "Before you install an addin" : "New publisher";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            ShowInTaskbar   = false;

            int width  = 520;
            int y      = 16;
            int textX  = 64;
            int textW  = width - textX - 24;

            Controls.Add(new PictureBox
            {
                Image    = SystemIcons.Information.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point(16, y),
            });

            var heading = new Label
            {
                Text     = publisherHeading,
                Location = new Point(textX, y),
                Size     = new Size(textW, 40),
                Font     = new Font(SystemFonts.DialogFont ?? SystemFonts.DefaultFont,
                                    FontStyle.Bold),
            };
            Controls.Add(heading);
            y += 44;

            var detail = new Label
            {
                Text     = publisherDetail,
                Location = new Point(textX, y),
                Size     = new Size(textW, 58),
            };
            Controls.Add(detail);
            y += 62;

            if (!string.IsNullOrEmpty(publisherUrl))
            {
                var link = new LinkLabel
                {
                    Text     = publisherUrl,
                    Location = new Point(textX, y),
                    Size     = new Size(textW, 20),
                };
                string target = publisherUrl!;
                link.LinkClicked += (s, e) =>
                {
                    try { System.Diagnostics.Process.Start(target); } catch { }
                };
                Controls.Add(link);
                y += 26;
            }

            if (generalTerms != null)
            {
                Controls.Add(new Label
                {
                    Text        = "",
                    BorderStyle = BorderStyle.Fixed3D,
                    Location    = new Point(textX, y),
                    Size        = new Size(textW, 2),
                });
                y += 12;

                Controls.Add(new Label
                {
                    Text     = generalTerms,
                    Location = new Point(textX, y),
                    Size     = new Size(textW, 168),
                });
                y += 176;
            }

            var accept = new Button
            {
                Text         = "Install",
                DialogResult = DialogResult.OK,
                Location     = new Point(width - 232, y),
                Size         = new Size(100, 26),
            };
            var cancel = new Button
            {
                Text         = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location     = new Point(width - 124, y),
                Size         = new Size(100, 26),
            };
            Controls.Add(accept);
            Controls.Add(cancel);

            AcceptButton = accept;
            CancelButton = cancel;
            ClientSize   = new Size(width, y + 42);
        }

        /// <summary>
        /// Ensures the user has accepted the general terms, and has knowingly installed from every
        /// publisher involved. Returns false if they declined, in which case nothing is installed.
        /// </summary>
        public static bool EnsureAccepted(IWin32Window? owner, AddinFinderSettings settings,
                                          IEnumerable<RegistryAddin> addins,
                                          IEnumerable<Publisher> knownPublishers)
        {
            var publishers = knownPublishers.ToList();

            // Distinct publishers in this batch that the user has not installed from before.
            var pending = addins.Select(a => a.Publisher ?? "")
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Where(id => !settings.HasAcknowledged(id))
                                .ToList();

            bool needGeneral = !settings.HasAcceptedTerms;
            if (!needGeneral && pending.Count == 0) return true;

            // If nothing new about the publisher but the terms changed, still say who this is.
            if (pending.Count == 0) pending.Add(addins.Select(a => a.Publisher ?? "").First());

            foreach (string publisherId in pending)
            {
                var p = publishers.FirstOrDefault(x => x.Id == publisherId);
                string heading, detail;
                string? url = null;

                if (string.IsNullOrEmpty(publisherId))
                {
                    // No approved publisher stands behind this. That is worth saying plainly rather
                    // than dressing up -- it is the weakest provenance the pad can offer.
                    heading = "This addin has no identified publisher";
                    detail  = "It comes from the older registry list, or was found already installed. "
                            + "Nobody is recorded as responsible for it, and there is no publisher to "
                            + "report problems to.";
                }
                else
                {
                    string name = p != null && p.Name.Length > 0 ? p.Name : publisherId;
                    heading = "First install from " + name;
                    detail  = "This addin is published by " + name + " (" + publisherId + "), who is "
                            + "responsible for its code. You have not installed anything from this "
                            + "publisher before. Their addins are open source and can be read first.";
                    url     = "https://github.com/" + publisherId;
                }

                using (var dialog = new InstallDisclaimerDialog(
                           needGeneral ? GeneralTerms : null, heading, detail, url))
                {
                    if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
                }

                // Only the first dialog of a batch carries the general terms.
                if (needGeneral)
                {
                    settings.AcceptedTermsVersion = AddinFinderSettings.CurrentTermsVersion;
                    needGeneral = false;
                }
                settings.Acknowledge(publisherId);
            }

            settings.Save();
            return true;
        }
    }
}
