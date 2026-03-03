using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
        private readonly AddinFinderSettings _settings        = AddinFinderSettings.Load();
        private AddinInstaller?              _installer;

        private List<RegistryAddin>    _registryAddins  = new List<RegistryAddin>();
        private List<InstalledAddin>   _installedAddins = new List<InstalledAddin>();
        private RegistryAddin?         _selectedAddin;
        private string                 _lastError       = string.Empty;

        private bool IsInstalledTabActive => _filterTabs.SelectedIndex == 1;

        public AddinFinderPad()
        {
            InitializeComponent();
            _installedAddins = _installedStore.Load();
            _installer       = TryCreateInstaller();
            SetSplitterDistance();
        }

        private void SetSplitterDistance()
        {
            bool firstShow = true;
            _contentPanel.VisibleChanged += (s, e) =>
            {
                if (!_contentPanel.Visible) return;
                if (_mainSplitter.Height > 0)
                    _mainSplitter.SplitterDistance = (int)(_mainSplitter.Height * 0.6);
                if (firstShow)
                {
                    firstShow = false;
                    SetPadTitle();
                    OnRefreshClick(null, EventArgs.Empty);
                }
            };
        }

        private void SetPadTitle()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            string title = $"Addin Finder v{v.Major}.{v.Minor}.{v.Build}";
            Control parent = _contentPanel.Parent;
            while (parent != null)
            {
                if (parent is Form f) { f.Text = title; return; }
                parent = parent.Parent;
            }
        }

        // ── Filter tab ────────────────────────────────────────────────────

        private void OnFilterTabChanged(object? sender, EventArgs e) => PopulateList();

        // ── Refresh ──────────────────────────────────────────────────────

        private void OnRefreshClick(object? sender, EventArgs e)
        {
            _refreshButton.Enabled = false;
            _statusLabel.Text      = "Fetching registry…";
            _addinListView.Items.Clear();
            ClearDetail();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                // Run both fetches on the same background thread
                Exception?      registryEx = null;
                AddinRegistry?  registry   = null;
                SelfUpdateInfo? updateInfo = null;

                try   { registry   = _registryClient.Fetch(); }
                catch (Exception ex) { registryEx = ex; }

                try   { updateInfo = SelfUpdateChecker.Check(); }
                catch { }

                _contentPanel.BeginInvoke(new Action(() =>
                {
                    if (registryEx != null)
                    {
                        _statusLabel.Text      = $"Error: {registryEx.Message}";
                        _refreshButton.Enabled = true;
                    }
                    else
                    {
                        _registryAddins  = registry!.Addins;
                        _installedAddins = _installedStore.Load();
                        PopulateList();
                        _statusLabel.Text        = $"{registry!.Addins.Count} addin(s) available · updated {registry!.Updated}";
                        _refreshButton.Enabled   = true;
                        _copyErrorButton.Visible = false;
                    }
                    ShowUpdateBanner(updateInfo);
                }));
            });
        }

        // ── List population ───────────────────────────────────────────────

        private void PopulateList()
        {
            _addinListView.BeginUpdate();
            _addinListView.Items.Clear();

            var addins = IsInstalledTabActive
                ? _registryAddins.Where(a => GetStatus(a) == AddinStatus.Installed || GetStatus(a) == AddinStatus.UpdateAvailable).ToList()
                : _registryAddins;

            foreach (var addin in addins)
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
            var selected = GetSelectedAddins();

            if (selected.Count == 0) { ClearDetail(); return; }

            if (selected.Count > 1)
            {
                // Multi-select: show summary, aggregate buttons
                _selectedAddin = null;
                _detailName.Text        = $"{selected.Count} addins selected";
                _detailAuthor.Text      = "";
                _detailVersion.Text     = "";
                _detailDescription.Text = string.Join(", ", selected.Select(a => a.Name));
                _detailHomepage.Text    = "";
                _detailChangelog.Text   = "";

                bool anyInstallable  = selected.Any(a => GetStatus(a) == AddinStatus.NotInstalled);
                bool anyUpdatable    = selected.Any(a => GetStatus(a) == AddinStatus.UpdateAvailable);
                bool anyUninstallable = selected.Any(a => GetStatus(a) == AddinStatus.Installed || GetStatus(a) == AddinStatus.UpdateAvailable);

                _installButton.Enabled   = anyInstallable && _installer != null;
                _updateButton.Enabled    = anyUpdatable   && _installer != null;
                _uninstallButton.Enabled = anyUninstallable;
                _reinstallButton.Enabled = false;  // multi-select: no reinstall
                return;
            }

            // Single select
            _selectedAddin = selected[0];
            var status = GetStatus(_selectedAddin);
            _detailName.Text        = _selectedAddin.Name + (_selectedAddin.Fork ? "  [Fork]" : "");
            _detailAuthor.Text      = $"by {_selectedAddin.Author}  ·  {_selectedAddin.License}  ·  {_selectedAddin.TargetFramework}";
            _detailVersion.Text     = $"Version {_selectedAddin.Version}";
            _detailDescription.Text = _selectedAddin.Description +
                (!string.IsNullOrEmpty(_selectedAddin.UpstreamUrl) ? $"\r\n\r\nFork of: {_selectedAddin.UpstreamUrl}" : "");
            _detailHomepage.Text    = string.IsNullOrEmpty(_selectedAddin.HomepageUrl) ? "" : "Homepage";
            _detailHomepage.Tag     = _selectedAddin.HomepageUrl;
            _detailChangelog.Text   = string.IsNullOrEmpty(_selectedAddin.ChangelogUrl) ? "" : "Changelog";
            _detailChangelog.Tag    = _selectedAddin.ChangelogUrl;

            _installButton.Enabled   = status == AddinStatus.NotInstalled && _installer != null;
            _updateButton.Enabled    = status == AddinStatus.UpdateAvailable && _installer != null;
            _uninstallButton.Enabled = status == AddinStatus.Installed || status == AddinStatus.UpdateAvailable;
            _reinstallButton.Enabled = status == AddinStatus.Installed && _installer != null;
        }

        private void ClearDetail()
        {
            _selectedAddin           = null;
            _detailName.Text         = "";
            _detailAuthor.Text       = "";
            _detailVersion.Text      = "";
            _detailDescription.Text  = "";
            _detailHomepage.Text     = "";
            _detailHomepage.Tag      = null;
            _detailChangelog.Text    = "";
            _detailChangelog.Tag     = null;
            _installButton.Enabled   = false;
            _updateButton.Enabled    = false;
            _uninstallButton.Enabled = false;
            _reinstallButton.Enabled = false;
        }

        // ── Install / Update / Uninstall ──────────────────────────────────

        private void OnInstallClick(object? sender, EventArgs e)   => RunInstall(GetSelectedAddins().Where(a => GetStatus(a) == AddinStatus.NotInstalled).ToList(), isUpdate: false);
        private void OnUpdateClick(object? sender, EventArgs e)    => RunInstall(GetSelectedAddins().Where(a => GetStatus(a) == AddinStatus.UpdateAvailable).ToList(), isUpdate: true);
        private void OnReinstallClick(object? sender, EventArgs e) => RunInstall(GetSelectedAddins().Where(a => GetStatus(a) == AddinStatus.Installed).ToList(), isUpdate: true);

        private void RunInstall(List<RegistryAddin> addins, bool isUpdate)
        {
            if (addins.Count == 0 || _installer == null) return;
            SetButtons(false);
            _statusLabel.Text = $"{(isUpdate ? "Updating" : "Installing")} {addins.Count} addin(s)…";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var failed  = new List<string>();
                bool anyStagedUpdate = false;
                foreach (var addin in addins)
                {
                    try
                    {
                        bool staged;
                        _installer.Install(addin, out staged);
                        if (staged) anyStagedUpdate = true;
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message;
                        if (ex.InnerException != null) msg += " → " + ex.InnerException.Message;
                        failed.Add($"{addin.Name}: {msg}");
                    }
                }
                _contentPanel.BeginInvoke(new Action(() =>
                {
                    _installedAddins = _installedStore.Load();
                    PopulateList();
                    if (failed.Count > 0)
                    {
                        _lastError = string.Join(Environment.NewLine, failed);
                        _statusLabel.Text        = $"Errors: {string.Join("; ", failed)}";
                        _copyErrorButton.Visible = true;
                    }
                    else if (anyStagedUpdate)
                    {
                        _statusLabel.Text = "Update staged — restart Clarion to complete.";
                        ShowRestartReminder(addins.Select(a => a.Name).ToArray(), RestartReason.StagedUpdate);
                    }
                    else
                    {
                        string[] names = addins.Select(a => a.Name).ToArray();
                        _statusLabel.Text = $"{addins.Count} addin(s) installed. Please restart Clarion to activate.";
                        ShowRestartReminder(names, isUpdate ? RestartReason.Updated : RestartReason.Installed);
                    }
                    OnAddinSelected(null, EventArgs.Empty);
                    SetButtons(true);
                }));
            });
        }

        private void OnUninstallClick(object? sender, EventArgs e)
        {
            var addins = GetSelectedAddins()
                .Where(a => GetStatus(a) == AddinStatus.Installed || GetStatus(a) == AddinStatus.UpdateAvailable)
                .ToList();
            if (addins.Count == 0 || _installer == null) return;

            string names = string.Join(", ", addins.Select(a => a.Name));
            if (MessageBox.Show($"Uninstall {names}?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            var failed = new List<string>();
            bool anyStaged = false;
            foreach (var addin in addins)
            {
                try
                {
                    bool staged;
                    _installer.Uninstall(addin, out staged);
                    if (staged) anyStaged = true;
                }
                catch (Exception ex) { failed.Add($"{addin.Name}: {ex.Message}"); }
            }
            _installedAddins = _installedStore.Load();
            PopulateList();
            string[] addinNameArr = addins.Select(a => a.Name).ToArray();
            if (failed.Count > 0)
            {
                _lastError               = string.Join(Environment.NewLine, failed);
                _statusLabel.Text        = $"Errors: {string.Join("; ", failed)}";
                _copyErrorButton.Visible = true;
            }
            else if (anyStaged)
            {
                _statusLabel.Text = "Uninstall staged — restart Clarion to complete.";
                ShowRestartReminder(addinNameArr, RestartReason.StagedRemoval);
            }
            else
            {
                _statusLabel.Text = $"{addins.Count} addin(s) uninstalled. Please restart Clarion.";
                ShowRestartReminder(addinNameArr, RestartReason.Removed);
            }
            OnAddinSelected(null, EventArgs.Empty);
        }

        // ── Self-update banner ────────────────────────────────────────────

        private SelfUpdateInfo? _pendingSelfUpdate;

        private void ShowUpdateBanner(SelfUpdateInfo? info)
        {
            if (info == null) return;
            _pendingSelfUpdate = info;

            var txt = _updateBanner.Controls["bannerText"] as System.Windows.Forms.Label;
            var btn = _updateBanner.Controls["bannerButton"] as Button;
            if (txt != null) txt.Text = $"Addin Finder v{info.AvailableVersion} is available";
            if (btn != null)
            {
                // Remove old handlers then attach fresh
                btn.Click -= OnSelfUpdateClick;
                btn.Click += OnSelfUpdateClick;
            }
            _updateBanner.Visible = true;
        }

        private void OnSelfUpdateClick(object? sender, EventArgs e)
        {
            if (_pendingSelfUpdate == null) return;
            _updateBanner.Visible  = false;
            _refreshButton.Enabled = false;
            _statusLabel.Text      = "Downloading Addin Finder update…";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string? error = null;
                try { AddinInstaller.StageSelfUpdate(_pendingSelfUpdate); }
                catch (Exception ex)
                {
                    error = ex.InnerException?.Message ?? ex.Message;
                }
                _contentPanel.BeginInvoke(new Action(() =>
                {
                    _refreshButton.Enabled = true;
                    if (error != null)
                    {
                        _lastError               = error;
                        _statusLabel.Text        = $"Self-update failed: {error}";
                        _copyErrorButton.Visible = true;
                    }
                    else
                    {
                        _statusLabel.Text = "Addin Finder update staged — restart Clarion to complete.";
                        ShowRestartReminder(new[] { "Addin Finder" }, RestartReason.StagedUpdate);
                    }
                }));
            });
        }

        private void ShowRestartReminder(string[] addinNames, RestartReason reason)
        {
            if (_settings.SuppressRestartReminder) return;
            using (var dlg = new RestartReminderDialog(addinNames, reason))
            {
                dlg.ShowDialog(_contentPanel.FindForm());
                if (dlg.DontShowAgain)
                {
                    _settings.SuppressRestartReminder = true;
                    _settings.Save();
                }
            }
        }

        private void SetButtons(bool enabled)
        {
            _installButton.Enabled   = enabled;
            _updateButton.Enabled    = enabled;
            _uninstallButton.Enabled = enabled;
            _reinstallButton.Enabled = enabled;
            _refreshButton.Enabled   = enabled;
        }

        // ── Status helpers ────────────────────────────────────────────────

        private List<RegistryAddin> GetSelectedAddins()
        {
            var result = new List<RegistryAddin>();
            foreach (ListViewItem item in _addinListView.SelectedItems)
                if (item.Tag is RegistryAddin a) result.Add(a);
            return result;
        }

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

        private void OnCopyErrorClick(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastError))
                System.Windows.Forms.Clipboard.SetText(_lastError);
        }
    }
}
