using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Gui;

namespace AddinFinder
{
    public partial class AddinFinderPad : AbstractPadContent
    {
        public override System.Windows.Forms.Control Control => _contentPanel;

        private readonly RegistryClient      _registryClient  = new RegistryClient();
        private readonly InstalledAddinStore _installedStore  = new InstalledAddinStore();
        private AddinInstaller?              _installer;

        private List<RegistryAddin>    _registryAddins  = new List<RegistryAddin>();
        private List<InstalledAddin>   _installedAddins = new List<InstalledAddin>();
        private RegistryAddin?         _selectedAddin;

        public AddinFinderPad()
        {
            InitializeComponent();
            _installedAddins = _installedStore.Load();
            _installer       = TryCreateInstaller();
            SetSplitterDistance();
        }

        private void SetSplitterDistance()
        {
            _contentPanel.VisibleChanged += (s, e) =>
            {
                if (_contentPanel.Visible && _mainSplitter.Height > 0)
                    _mainSplitter.SplitterDistance = (int)(_mainSplitter.Height * 0.6);
            };
        }

        // ── Refresh ──────────────────────────────────────────────────────

        private void OnRefreshClick(object? sender, EventArgs e)
        {
            _refreshButton.Enabled = false;
            _statusLabel.Text      = "Fetching registry…";
            _addinListView.Items.Clear();
            ClearDetail();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var registry = _registryClient.Fetch();
                    _contentPanel.BeginInvoke(new Action(() =>
                    {
                        _registryAddins  = registry.Addins;
                        _installedAddins = _installedStore.Load();
                        PopulateList();
                        _statusLabel.Text      = $"{registry.Addins.Count} addin(s) available · updated {registry.Updated}";
                        _refreshButton.Enabled = true;
                    }));
                }
                catch (Exception ex)
                {
                    _contentPanel.BeginInvoke(new Action(() =>
                    {
                        _statusLabel.Text      = $"Error: {ex.Message}";
                        _refreshButton.Enabled = true;
                    }));
                }
            });
        }

        // ── List population ───────────────────────────────────────────────

        private void PopulateList()
        {
            _addinListView.BeginUpdate();
            _addinListView.Items.Clear();

            foreach (var addin in _registryAddins)
            {
                var status = GetStatus(addin);
                var item   = new ListViewItem(addin.Name);
                item.SubItems.Add(addin.Author);
                item.SubItems.Add(addin.Category);
                item.SubItems.Add(addin.Version);
                item.SubItems.Add(StatusText(status));
                item.Tag = addin;
                item.ForeColor = StatusColour(status);
                _addinListView.Items.Add(item);
            }

            _addinListView.EndUpdate();
        }

        private void OnAddinSelected(object? sender, EventArgs e)
        {
            if (_addinListView.SelectedItems.Count == 0) { ClearDetail(); return; }
            _selectedAddin = _addinListView.SelectedItems[0].Tag as RegistryAddin;
            if (_selectedAddin == null) return;

            var status = GetStatus(_selectedAddin);
            _detailName.Text        = _selectedAddin.Name;
            _detailAuthor.Text      = $"by {_selectedAddin.Author}  ·  {_selectedAddin.License}  ·  {_selectedAddin.TargetFramework}";
            _detailVersion.Text     = $"Version {_selectedAddin.Version}";
            _detailDescription.Text = _selectedAddin.Description;
            _detailHomepage.Text    = string.IsNullOrEmpty(_selectedAddin.HomepageUrl) ? "" : "Homepage";
            _detailChangelog.Text   = string.IsNullOrEmpty(_selectedAddin.ChangelogUrl) ? "" : "Changelog";

            _installButton.Enabled   = status == AddinStatus.NotInstalled && _installer != null;
            _updateButton.Enabled    = status == AddinStatus.UpdateAvailable && _installer != null;
            _uninstallButton.Enabled = status == AddinStatus.Installed || status == AddinStatus.UpdateAvailable;
        }

        private void ClearDetail()
        {
            _selectedAddin           = null;
            _detailName.Text         = "";
            _detailAuthor.Text       = "";
            _detailVersion.Text      = "";
            _detailDescription.Text  = "";
            _detailHomepage.Text     = "";
            _detailChangelog.Text    = "";
            _installButton.Enabled   = false;
            _updateButton.Enabled    = false;
            _uninstallButton.Enabled = false;
        }

        // ── Install / Update / Uninstall ──────────────────────────────────

        private void OnInstallClick(object? sender, EventArgs e)  => RunInstall(_selectedAddin, isUpdate: false);
        private void OnUpdateClick(object? sender, EventArgs e)   => RunInstall(_selectedAddin, isUpdate: true);

        private void RunInstall(RegistryAddin? addin, bool isUpdate)
        {
            if (addin == null || _installer == null) return;
            SetButtons(false);
            _statusLabel.Text = $"{(isUpdate ? "Updating" : "Installing")} {addin.Name}…";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    _installer.Install(addin);
                    _contentPanel.BeginInvoke(new Action(() =>
                    {
                        _installedAddins = _installedStore.Load();
                        PopulateList();
                        _statusLabel.Text = $"{addin.Name} installed. Please restart Clarion to activate.";
                        OnAddinSelected(null, EventArgs.Empty);
                    }));
                }
                catch (Exception ex)
                {
                    _contentPanel.BeginInvoke(new Action(() =>
                    {
                        _statusLabel.Text = $"Install failed: {ex.Message}";
                        SetButtons(true);
                    }));
                }
            });
        }

        private void OnUninstallClick(object? sender, EventArgs e)
        {
            if (_selectedAddin == null || _installer == null) return;
            if (MessageBox.Show($"Uninstall {_selectedAddin.Name}?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try
            {
                _installer.Uninstall(_selectedAddin);
                _installedAddins = _installedStore.Load();
                PopulateList();
                _statusLabel.Text = $"{_selectedAddin.Name} uninstalled. Please restart Clarion.";
                OnAddinSelected(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Uninstall failed: {ex.Message}";
            }
        }

        private void SetButtons(bool enabled)
        {
            _installButton.Enabled   = enabled;
            _updateButton.Enabled    = enabled;
            _uninstallButton.Enabled = enabled;
            _refreshButton.Enabled   = enabled;
        }

        // ── Status helpers ────────────────────────────────────────────────

        private enum AddinStatus { NotInstalled, Installed, UpdateAvailable, Incompatible }

        private AddinStatus GetStatus(RegistryAddin addin)
        {
            // net5+ targets cannot load in Clarion's CLR v4 process
            if (!string.IsNullOrEmpty(addin.TargetFramework) &&
                addin.TargetFramework.StartsWith("net") &&
                !addin.TargetFramework.StartsWith("net4") &&
                addin.TargetFramework.Length > 3 &&
                char.IsDigit(addin.TargetFramework[3]) &&
                addin.TargetFramework[3] >= '5')
                return AddinStatus.Incompatible;

            var installed = _installedAddins.FirstOrDefault(a => a.Id == addin.Id);
            if (installed == null) return AddinStatus.NotInstalled;
            return installed.Version == addin.Version ? AddinStatus.Installed : AddinStatus.UpdateAvailable;
        }

        private static string StatusText(AddinStatus s) => s switch
        {
            AddinStatus.Installed        => "✓ Installed",
            AddinStatus.UpdateAvailable  => "↑ Update available",
            AddinStatus.Incompatible     => "✗ Incompatible",
            _                            => "— Not installed",
        };

        private static System.Drawing.Color StatusColour(AddinStatus s) => s switch
        {
            AddinStatus.Installed        => System.Drawing.Color.Green,
            AddinStatus.UpdateAvailable  => System.Drawing.Color.DarkOrange,
            AddinStatus.Incompatible     => System.Drawing.Color.Red,
            _                            => System.Drawing.SystemColors.WindowText,
        };

        private static void OpenUrl(string url)
        {
            if (!string.IsNullOrEmpty(url))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private AddinInstaller? TryCreateInstaller()
        {
            try
            {
                // Walk up from this DLL's location to find Clarion root (same pattern as AccuraBuildSwitcher)
                string asmDir   = System.IO.Path.GetDirectoryName(typeof(AddinFinderPad).Assembly.Location)!;
                string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(asmDir, "..", "..", ".."));
                if (System.IO.Directory.Exists(System.IO.Path.Combine(candidate, "bin")))
                    return new AddinInstaller(candidate, _installedStore);
            }
            catch { }
            return null;
        }
    }
}
