using System;
using System.Drawing;
using System.Windows.Forms;
using ICSharpCode.SharpDevelop.Gui;

namespace AddinFinder
{
    partial class AddinFinderPad
    {
        #region Control Declarations

        private Panel           _contentPanel;
        private ToolStrip       _toolStrip;
        private ToolStripButton _refreshButton;
        private ToolStripButton _copyErrorButton;
        private SplitContainer  _mainSplitter;
        private Panel           _listPanel;
        private TabControl      _filterTabs;
        private ListView        _addinListView;
        private Panel           _detailPanel;
        private Label           _detailName;
        private Label           _detailDescription;
        private LinkLabel       _detailHomepage;
        private LinkLabel       _detailChangelog;
        private Label           _detailAuthor;
        private Label           _detailVersion;
        private Button          _installButton;
        private Button          _updateButton;
        private Button          _uninstallButton;
        private Button          _reinstallButton;
        private Label           _statusLabel;

        #endregion

        private void InitializeComponent()
        {
            _contentPanel   = new Panel();
            _toolStrip      = new ToolStrip();
            _refreshButton  = new ToolStripButton();
            _copyErrorButton = new ToolStripButton();
            _mainSplitter   = new SplitContainer();
            _listPanel      = new Panel();
            _filterTabs     = new TabControl();
            _addinListView  = new ListView();
            _detailPanel    = new Panel();
            _detailName     = new Label();
            _detailDescription = new Label();
            _detailHomepage = new LinkLabel();
            _detailChangelog = new LinkLabel();
            _detailAuthor   = new Label();
            _detailVersion  = new Label();
            _installButton  = new Button();
            _updateButton   = new Button();
            _uninstallButton = new Button();
            _reinstallButton = new Button();
            _statusLabel    = new Label();

            // ── ToolStrip ────────────────────────────────────────────────
            _refreshButton.Text         = "⟳ Refresh";
            _refreshButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
            _refreshButton.Click       += OnRefreshClick;

            _copyErrorButton.Text           = "📋 Copy Error";
            _copyErrorButton.DisplayStyle   = ToolStripItemDisplayStyle.Text;
            _copyErrorButton.Alignment      = ToolStripItemAlignment.Right;
            _copyErrorButton.Visible        = false;
            _copyErrorButton.Click         += OnCopyErrorClick;

            _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            _toolStrip.Dock      = DockStyle.Top;
            _toolStrip.Padding   = new Padding(5, 2, 5, 2);
            _toolStrip.Items.Add(_refreshButton);
            _toolStrip.Items.Add(_copyErrorButton);

            // ── Status label ─────────────────────────────────────────────
            _statusLabel.Dock      = DockStyle.Top;
            _statusLabel.Height    = 20;
            _statusLabel.Font      = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f);
            _statusLabel.ForeColor = SystemColors.GrayText;
            _statusLabel.Text      = "Click Refresh to load available addins.";
            _statusLabel.Padding   = new Padding(6, 0, 0, 0);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            // ── Filter tabs ───────────────────────────────────────────────
            _filterTabs.Dock        = DockStyle.Top;
            _filterTabs.Appearance  = TabAppearance.FlatButtons;
            _filterTabs.ItemSize    = new Size(70, 22);
            _filterTabs.SizeMode    = TabSizeMode.Fixed;
            _filterTabs.Height      = 26;
            _filterTabs.TabPages.Add("tabAll",       "All");
            _filterTabs.TabPages.Add("tabInstalled", "Installed");
            _filterTabs.SelectedIndexChanged += OnFilterTabChanged;

            // ── ListView (top pane) ───────────────────────────────────────
            _addinListView.Dock          = DockStyle.Fill;
            _addinListView.View          = View.Details;
            _addinListView.FullRowSelect = true;
            _addinListView.MultiSelect   = true;
            _addinListView.HideSelection = false;
            _addinListView.Font          = new Font(SystemFonts.DefaultFont.FontFamily, 9f);
            _addinListView.Columns.Add("Name",     160);
            _addinListView.Columns.Add("Author",    90);
            _addinListView.Columns.Add("Category",  90);
            _addinListView.Columns.Add("Version",   65);
            _addinListView.Columns.Add("Status",   100);
            _addinListView.SelectedIndexChanged += OnAddinSelected;

            // listPanel wraps tabs + list (Fill first, Top second)
            _listPanel.Dock = DockStyle.Fill;
            _listPanel.Controls.Add(_addinListView); // Fill
            _listPanel.Controls.Add(_filterTabs);    // Top

            // ── Detail panel (bottom pane) ────────────────────────────────
            // Name (large)
            _detailName.Dock      = DockStyle.Top;
            _detailName.Height    = 24;
            _detailName.Font      = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold);
            _detailName.Padding   = new Padding(6, 4, 0, 0);
            _detailName.Text      = "";

            // Author + version row
            _detailAuthor.Dock    = DockStyle.Top;
            _detailAuthor.Height  = 18;
            _detailAuthor.Font    = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f);
            _detailAuthor.ForeColor = SystemColors.GrayText;
            _detailAuthor.Padding = new Padding(6, 0, 0, 0);
            _detailAuthor.Text    = "";

            _detailVersion.Dock   = DockStyle.Top;
            _detailVersion.Height = 18;
            _detailVersion.Font   = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f);
            _detailVersion.ForeColor = SystemColors.GrayText;
            _detailVersion.Padding = new Padding(6, 0, 0, 0);
            _detailVersion.Text   = "";

            // Description (fills remaining space)
            _detailDescription.Dock      = DockStyle.Fill;
            _detailDescription.Font      = new Font(SystemFonts.DefaultFont.FontFamily, 9f);
            _detailDescription.Padding   = new Padding(6, 4, 6, 4);
            _detailDescription.Text      = "";

            // Links row
            var linkPanel = new Panel { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(4, 2, 4, 2) };
            _detailHomepage.AutoSize  = true;
            _detailHomepage.Text      = "";
            _detailHomepage.Location  = new Point(6, 4);
            _detailHomepage.LinkClicked += (s, e) => OpenUrl(_detailHomepage.Tag as string ?? "");

            _detailChangelog.AutoSize = true;
            _detailChangelog.Text     = "";
            _detailChangelog.Location = new Point(120, 4);
            _detailChangelog.LinkClicked += (s, e) => OpenUrl(_detailChangelog.Tag as string ?? "");

            linkPanel.Controls.Add(_detailHomepage);
            linkPanel.Controls.Add(_detailChangelog);

            // Action buttons row
            var buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(4, 4, 4, 4) };
            _installButton.Text     = "Install";
            _installButton.Width    = 80;
            _installButton.Height   = 26;
            _installButton.Location = new Point(6, 4);
            _installButton.Enabled  = false;
            _installButton.Click   += OnInstallClick;

            _updateButton.Text      = "Update";
            _updateButton.Width     = 80;
            _updateButton.Height    = 26;
            _updateButton.Location  = new Point(92, 4);
            _updateButton.Enabled   = false;
            _updateButton.Click    += OnUpdateClick;

            _uninstallButton.Text   = "Uninstall";
            _uninstallButton.Width  = 80;
            _uninstallButton.Height = 26;
            _uninstallButton.Location = new Point(178, 4);
            _uninstallButton.Enabled = false;
            _uninstallButton.Click += OnUninstallClick;

            _reinstallButton.Text     = "Reinstall";
            _reinstallButton.Width    = 80;
            _reinstallButton.Height   = 26;
            _reinstallButton.Location = new Point(264, 4);
            _reinstallButton.Enabled  = false;
            _reinstallButton.Click   += OnReinstallClick;

            buttonPanel.Controls.Add(_installButton);
            buttonPanel.Controls.Add(_updateButton);
            buttonPanel.Controls.Add(_uninstallButton);
            buttonPanel.Controls.Add(_reinstallButton);

            // Assemble detail panel — Fill first, then Bottom, then Top (z-order rule)
            _detailPanel.Controls.Add(_detailDescription); // Fill
            _detailPanel.Controls.Add(buttonPanel);        // Bottom
            _detailPanel.Controls.Add(linkPanel);          // Bottom
            _detailPanel.Controls.Add(_detailVersion);     // Top (added last = topmost)
            _detailPanel.Controls.Add(_detailAuthor);
            _detailPanel.Controls.Add(_detailName);

            // ── SplitContainer ────────────────────────────────────────────
            _mainSplitter.Dock        = DockStyle.Fill;
            _mainSplitter.Orientation = Orientation.Horizontal;
            _mainSplitter.Panel1.Controls.Add(_listPanel);
            _mainSplitter.Panel2.Controls.Add(_detailPanel);
            _detailPanel.Dock = DockStyle.Fill;

            // ── Content panel — Fill first, then Top (z-order rule) ───────
            _contentPanel.Dock      = DockStyle.Fill;
            _contentPanel.AutoScroll = false;
            _contentPanel.Controls.Add(_mainSplitter);  // Fill
            _contentPanel.Controls.Add(_statusLabel);   // Top
            _contentPanel.Controls.Add(_toolStrip);     // Top (topmost)
        }
    }
}
