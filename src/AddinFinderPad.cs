using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        // Consent and the change notice are per Clarion, so the settings must be loaded for
        // the root this pad is running in -- not once for the machine.
        private readonly AddinFinderSettings _settings        =
            AddinFinderSettings.Load(ClarionRoot.Resolve() ?? string.Empty);
        private AddinInstaller?              _installer;

        /// <summary>The Clarion this pad acts on. Empty if it could not be resolved.</summary>
        private string ClarionRootPath => ClarionRoot.Resolve() ?? string.Empty;

        private List<RegistryAddin>    _registryAddins  = new List<RegistryAddin>();
        private List<InstalledAddin>   _installedAddins = new List<InstalledAddin>();
        private RegistryResult         _lastResult      = new RegistryResult();
        private RegistryAddin?         _selectedAddin;
        private string                 _lastError       = string.Empty;

        private bool IsInstalledTabActive => _filterTabs.SelectedIndex == 1;

        public AddinFinderPad()
        {
            InitializeComponent();
            _installedAddins = _installedStore.Load(ClarionRootPath);
            _installer       = TryCreateInstaller();
            SetSplitterDistance();
            HookInitialLoad();
        }

        // Splitter sizing legitimately needs the panel's height, which is only
        // known once it's been laid out — VisibleChanged is the right signal.
        private void SetSplitterDistance()
        {
            _contentPanel.VisibleChanged += (s, e) =>
            {
                if (_contentPanel.Visible && _mainSplitter.Height > 0)
                    _mainSplitter.SplitterDistance = (int)(_mainSplitter.Height * 0.6);
            };
        }

        // Initial registry fetch + title — fired exactly once when the control
        // is added to the visual tree. VisibleChanged is unreliable for this
        // because it only fires on transitions, and when the pad is created
        // lazily the panel may already be Visible by the time we attach.
        private void HookInitialLoad()
        {
            EventHandler? onHandleCreated = null;
            onHandleCreated = (s, e) =>
            {
                _contentPanel.HandleCreated -= onHandleCreated;
                SetPadTitle();

                // Shown IN the pad rather than as a modal. SharpDevelop restores whichever pads
                // were open last time, so a docked pad is created during Clarion's start-up -- a
                // dialog there interrupts the IDE coming up, before the user has asked for
                // anything. The refresh below still runs underneath, so dismissing the notice
                // reveals a list that is already loaded.
                if (WhatsChangedNotice.ShouldShow(_settings,
                        WhatsChangedNotice.HasEarlierState(_installedAddins.Count)))
                    WhatsChangedPanel.ShowOver(_contentPanel);

                OnRefreshClick(null, EventArgs.Empty);
            };

            if (_contentPanel.IsHandleCreated)
            {
                // Handle already exists — defer so the constructor can finish.
                _contentPanel.BeginInvoke(new Action(() =>
                {
                    SetPadTitle();
                    OnRefreshClick(null, EventArgs.Empty);
                }));
            }
            else
            {
                _contentPanel.HandleCreated += onHandleCreated;
            }
        }

        private void SetPadTitle()
        {
            // Read from disk (FileVersionInfo) not the in-memory assembly version.
            // After a self-update apply, the disk has the new version even though
            // the old assembly is still loaded — this shows the correct version.
            string asmPath = typeof(AddinFinderPad).Assembly.Location;
            var    fvi     = System.Diagnostics.FileVersionInfo.GetVersionInfo(asmPath);
            string title   = $"Addin Finder v{fvi.FileMajorPart}.{fvi.FileMinorPart}.{fvi.FileBuildPart}";
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
                RegistryResult? registry   = null;
                SelfUpdateInfo? updateInfo = null;

                try   { registry   = _registryClient.FetchAll(DateTime.Today); }
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
                        _lastResult      = registry!;
                        _installedAddins = _installedStore.Load(ClarionRootPath);

                        // An addin the user has, that no publisher lists any more, still has to be
                        // visible to them -- described from cache rather than vanishing silently.
                        _registryAddins = new List<RegistryAddin>(registry!.Addins);
                        _registryAddins.AddRange(
                            _registryClient.DescribeWithdrawn(registry!, _installedAddins));

                        PopulateList();
                        _statusLabel.Text        = SummariseRefresh(registry!);
                        WarnAboutIdentityClashes();
                        _refreshButton.Enabled   = true;
                        _copyErrorButton.Visible = false;
                    }
                    ShowUpdateBanner(updateInfo);
                }));
            });
        }

        // ── List population ───────────────────────────────────────────────

        /// <summary>Names the publisher of a conflicting copy, when we know it.</summary>
        private string DescribeClashOwner(string folderId)
        {
            var known = _installedAddins.FirstOrDefault(
                i => string.Equals(i.Id, folderId, StringComparison.OrdinalIgnoreCase));

            // An adopted or hand-placed copy has no recorded publisher. Say so rather than
            // implying it came from somewhere we can vouch for.
            if (known == null || known.Publisher.Length == 0) return " (publisher unknown)";
            return $" (from {known.Publisher})";
        }

        /// <summary>
        /// Warns if anything under accessory\addins would stop Clarion starting.
        ///
        /// Not a modal, and not tied to installing. By the time this is true the affected Clarion
        /// will not start, so the IDE showing the message is a different one -- a second Clarion on
        /// the same machine, or the next start after something else wrote the duplicate. A dialog
        /// would be interrupting the wrong session about a machine-level problem.
        /// </summary>
        private void WarnAboutIdentityClashes()
        {
            var clashes = IdentityAudit.Scan(ClarionRootPath);
            if (clashes.Count == 0) return;

            _lastError               = IdentityAudit.FullWarning(clashes);
            _statusLabel.Text        = IdentityAudit.ShortWarning(clashes);
            _copyErrorButton.Visible = true;
        }

        private void PopulateList()
        {
            _addinListView.BeginUpdate();
            _addinListView.Items.Clear();
            _addinListView.Groups.Clear();
            _addinListView.ShowGroups = true;

            var addins = IsInstalledTabActive
                ? _registryAddins.Where(a => GetStatus(a) == AddinStatus.Installed || GetStatus(a) == AddinStatus.UpdateAvailable).ToList()
                : _registryAddins.Where(IsListedOnAllTab).ToList();

            var groups = new Dictionary<string, ListViewGroup>();

            foreach (var addin in addins)
            {
                var status = GetStatus(addin);
                var item   = new ListViewItem(addin.Name);
                item.SubItems.Add(addin.Author);
                item.SubItems.Add(addin.Category);
                item.SubItems.Add(addin.Version);
                item.SubItems.Add(LifecycleText(addin, status));
                item.Tag = addin;
                item.ForeColor = LifecycleColour(addin, status);
                item.Group = GroupFor(groups, addin);
                _addinListView.Items.Add(item);
            }

            _addinListView.EndUpdate();
        }

        /// <summary>
        /// Whether an addin belongs on the All tab -- i.e. whether we would offer it to someone who
        /// does not have it. Deprecated addins, withdrawn addins and those from an abandoned or
        /// revoked publisher are not offered, but stay visible on the Installed tab to anyone who
        /// already has one.
        /// </summary>
        private bool IsListedOnAllTab(RegistryAddin addin)
        {
            if (!addin.IsOffered) return false;
            var publisher = PublisherOf(addin);
            if (publisher != null && publisher.Status != PublisherStatus.Active) return false;
            return true;
        }

        private Publisher? PublisherOf(RegistryAddin addin)
            => addin.Publisher.Length == 0
                ? null
                : _lastResult.Publishers.FirstOrDefault(p => p.Id == addin.Publisher);

        /// <summary>
        /// One group per publisher, headed by their state. Provenance is the point: approving
        /// publishers means nothing to a user who cannot see whose code they are about to run.
        /// </summary>
        private ListViewGroup GroupFor(Dictionary<string, ListViewGroup> groups, RegistryAddin addin)
        {
            string key = addin.Publisher.Length > 0 ? addin.Publisher : "";
            ListViewGroup existing;
            if (groups.TryGetValue(key, out existing)) return existing;

            string header;
            if (key.Length == 0)
            {
                // Entries from the legacy flat list, and anything adopted from disk or put there by
                // another installer. Never label these with a publisher we do not actually know.
                header = "Unknown publisher";
            }
            else
            {
                var p = PublisherOf(addin);
                header = p != null && p.Name.Length > 0 ? p.Name + " (" + key + ")" : key;

                string note = PublisherStateNote(key, p);
                if (note.Length > 0) header += " — " + note;
            }

            var group = new ListViewGroup(header);
            groups[key] = group;
            _addinListView.Groups.Add(group);
            return group;
        }

        /// <summary>
        /// How a publisher's situation reads in the group header. Deliberately distinguishes "we
        /// could not reach them" from "their list is gone" -- the first says nothing about the
        /// publisher, and treating it as withdrawal would turn an outage into a false alarm.
        /// </summary>
        private string PublisherStateNote(string publisherId, Publisher? p)
        {
            if (p != null && p.Status == PublisherStatus.Revoked)   return "REVOKED — see notes before using";
            if (p != null && p.Status == PublisherStatus.Abandoned) return "no longer publishing";
            if (_lastResult.PresumedWithdrawn.Contains(publisherId)) return "list appears to have been removed";

            FetchOutcome outcome;
            if (_lastResult.Outcomes.TryGetValue(publisherId, out outcome) && outcome != FetchOutcome.Ok)
                return outcome == FetchOutcome.NotFound
                    ? "list not found — showing last known"
                    : "could not be reached — showing last known";

            return "";
        }

        private string LifecycleText(RegistryAddin addin, AddinStatus status)
        {
            if (addin.NoLongerPublished) return StatusText(status) + " · no longer published";
            if (addin.Status == AddinLifecycle.Deprecated) return StatusText(status) + " · deprecated";
            if (addin.FromCache) return StatusText(status) + " · cached";
            return StatusText(status);
        }

        private static System.Drawing.Color LifecycleColour(RegistryAddin addin, AddinStatus status)
            => addin.NoLongerPublished || addin.Status == AddinLifecycle.Deprecated
                ? System.Drawing.Color.DarkGoldenrod
                : StatusColour(status);

        private static string SummariseRefresh(RegistryResult r)
        {
            if (r.RootFetchFailed)
                return $"Registry unavailable — showing {r.Addins.Count} addin(s) from cache";

            int degraded = r.Outcomes.Values.Count(o => o != FetchOutcome.Ok);
            string s = $"{r.Addins.Count} addin(s) from {r.Publishers.Count} publisher(s)";
            if (degraded > 0) s += $" · {degraded} publisher(s) unavailable, showing last known";
            return s;
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
                ClearAuthorLabel();
                _detailVersion.Text     = "";
                _detailDescription.Text = string.Join(", ", selected.Select(a => a.Name));
                _detailHomepage.Text    = "";
                _detailChangelog.Text   = "";

                bool anyInstallable  = selected.Any(a => GetStatus(a) == AddinStatus.NotInstalled);
                bool anyUpdatable    = selected.Any(a => GetStatus(a) == AddinStatus.UpdateAvailable);
                bool anyUninstallable = selected.Any(a => GetStatus(a) == AddinStatus.Installed || GetStatus(a) == AddinStatus.UpdateAvailable);

                bool anySetup = selected.Any(a => a.IsSetup);
                _installButton.Text      = anySetup && selected.All(a => a.IsSetup) ? "Download" : "Install";
                _installButton.Enabled   = anyInstallable && (_installer != null || anySetup);
                _updateButton.Enabled    = anyUpdatable   && (_installer != null || anySetup);
                // Never offer to remove a mixed selection: some of it is not ours to remove.
                _uninstallButton.Enabled = anyUninstallable && !anySetup;
                _reinstallButton.Enabled = false;  // multi-select: no reinstall
                return;
            }

            // Single select
            _selectedAddin = selected[0];
            var status = GetStatus(_selectedAddin);
            _detailName.Text        = _selectedAddin.Name + (_selectedAddin.Fork ? "  [Fork]" : "");
            SetAuthorLabel(_selectedAddin);
            _detailVersion.Text     = $"Version {_selectedAddin.Version}";
            _detailDescription.Text = _selectedAddin.Description +
                (!string.IsNullOrEmpty(_selectedAddin.UpstreamUrl) ? $"\r\n\r\nFork of: {_selectedAddin.UpstreamUrl}" : "");
            _detailHomepage.Text    = string.IsNullOrEmpty(_selectedAddin.HomepageUrl) ? "" : "Homepage";
            _detailHomepage.Tag     = _selectedAddin.HomepageUrl;
            _detailChangelog.Text   = string.IsNullOrEmpty(_selectedAddin.ChangelogUrl) ? "" : "Changelog";
            _detailChangelog.Tag    = _selectedAddin.ChangelogUrl;
            _detailReadme.Text      = string.IsNullOrEmpty(_selectedAddin.HomepageUrl) ? "" : "View README";
            _detailReadme.Tag       = _selectedAddin.HomepageUrl;

            // An addin that installs itself can always be downloaded, and can never be removed by
            // us: its own uninstaller owns those files, and deleting them behind its back would
            // leave Windows believing it is still installed.
            bool setup = _selectedAddin != null && _selectedAddin.IsSetup;
            bool haveInstaller = setup && _selectedAddin!.Release != null && _selectedAddin.Release!.IsUsable;

            _installButton.Text      = setup ? "Download" : "Install";
            _installButton.Enabled   = setup
                ? haveInstaller && status != AddinStatus.Incompatible
                : status == AddinStatus.NotInstalled && _installer != null;
            _updateButton.Enabled    = setup
                ? haveInstaller && status == AddinStatus.UpdateAvailable
                : status == AddinStatus.UpdateAvailable && _installer != null;
            _uninstallButton.Enabled = !setup && (status == AddinStatus.Installed || status == AddinStatus.UpdateAvailable);
            _reinstallButton.Enabled = !setup && status == AddinStatus.Installed && _installer != null;
        }

        // Render the "by {author} · {license} · {framework}" line, linking just
        // the author name to authorUrl when the registry provides one.
        private void SetAuthorLabel(RegistryAddin addin)
        {
            _detailAuthor.Text = $"by {addin.Author}  ·  {addin.License}  ·  {addin.TargetFramework}";
            if (!string.IsNullOrEmpty(addin.AuthorUrl) && !string.IsNullOrEmpty(addin.Author))
            {
                // "by " prefix is 3 chars; link covers the author name only.
                _detailAuthor.LinkArea = new LinkArea(3, addin.Author.Length);
                _detailAuthor.Tag      = addin.AuthorUrl;
            }
            else
            {
                _detailAuthor.LinkArea = new LinkArea(0, 0);
                _detailAuthor.Tag      = null;
            }
        }

        private void ClearAuthorLabel()
        {
            _detailAuthor.Text     = "";
            _detailAuthor.LinkArea = new LinkArea(0, 0);
            _detailAuthor.Tag      = null;
        }

        private void ClearDetail()
        {
            _selectedAddin           = null;
            _detailName.Text         = "";
            ClearAuthorLabel();
            _detailVersion.Text      = "";
            _detailDescription.Text  = "";
            _detailHomepage.Text     = "";
            _detailHomepage.Tag      = null;
            _detailChangelog.Text    = "";
            _detailChangelog.Tag     = null;
            _detailReadme.Text       = "";
            _detailReadme.Tag        = null;
            _installButton.Enabled   = false;
            _updateButton.Enabled    = false;
            _uninstallButton.Enabled = false;
            _reinstallButton.Enabled = false;
        }

        // ── Install / Update / Uninstall ──────────────────────────────────

        private void OnInstallClick(object? sender, EventArgs e)   => Run(GetSelectedAddins().Where(a => GetStatus(a) == AddinStatus.NotInstalled).ToList(), isUpdate: false);
        private void OnUpdateClick(object? sender, EventArgs e)    => Run(GetSelectedAddins().Where(a => GetStatus(a) == AddinStatus.UpdateAvailable).ToList(), isUpdate: true);

        /// <summary>
        /// Splits a selection between addins we install and addins that install themselves.
        ///
        /// A mixed selection is normal -- the user picked several rows -- so each half is handled
        /// on its own terms rather than refusing the lot.
        /// </summary>
        private void Run(List<RegistryAddin> addins, bool isUpdate)
        {
            var setups = addins.Where(a => a.IsSetup).ToList();
            var ours   = addins.Where(a => !a.IsSetup).ToList();

            if (ours.Count   > 0) RunInstall(ours, isUpdate);
            if (setups.Count > 0) RunDownloadSetup(setups);
        }

        /// <summary>
        /// Downloads a setup installer and shows the user where it is. Deliberately does not run
        /// it: the installer elevates and chooses its own Clarion targets, and Addin Finder has
        /// just told the user nobody reviews addin code. Starting it for them would sit badly
        /// beside that.
        /// </summary>
        private void RunDownloadSetup(List<RegistryAddin> addins)
        {
            if (!InstallDisclaimerDialog.EnsureAccepted(
                    _contentPanel, _settings, addins, _lastResult.Publishers)) return;

            SetButtons(false);
            _statusLabel.Text = $"Downloading {addins.Count} installer(s)...";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var saved  = new List<string>();
                var failed = new List<string>();
                foreach (var addin in addins)
                {
                    try   { saved.Add(AddinInstaller.DownloadSetup(addin)); }
                    catch (Exception ex) { failed.Add($"{addin.Name}: {ex.Message}"); }
                }

                _contentPanel.BeginInvoke(new Action(() =>
                {
                    if (failed.Count > 0)
                    {
                        _lastError               = string.Join(Environment.NewLine, failed);
                        _statusLabel.Text        = $"Errors: {string.Join("; ", failed)}";
                        _copyErrorButton.Visible = true;
                    }

                    if (saved.Count > 0)
                    {
                        _statusLabel.Text = saved.Count == 1
                            ? "Downloaded " + Path.GetFileName(saved[0]) + " - run it to install"
                            : saved.Count + " installers downloaded - run them to install";

                        MessageBox.Show(_contentPanel,
                            "These addins install themselves, so Addin Finder has only downloaded "
                            + "them:" + Environment.NewLine + Environment.NewLine + "    "
                            + string.Join(Environment.NewLine + "    ", saved)
                            + Environment.NewLine + Environment.NewLine
                            + "Run the installer yourself when you are ready. It will ask for "
                            + "elevation and choose which Clarion versions to install into.",
                            "Downloaded", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        // Select it in Explorer rather than launching it -- the user runs it.
                        try { Process.Start("explorer.exe", "/select,\"" + saved[0] + "\""); }
                        catch { }
                    }
                    SetButtons(true);
                }));
            });
        }
        private void OnReinstallClick(object? sender, EventArgs e) => RunInstall(GetSelectedAddins().Where(a => GetStatus(a) == AddinStatus.Installed).ToList(), isUpdate: true);

        private void RunInstall(List<RegistryAddin> addins, bool isUpdate)
        {
            if (addins.Count == 0 || _installer == null) return;

            // Informed consent before the first install of anything. Declining cancels.
            if (!InstallDisclaimerDialog.EnsureAccepted(
                    _contentPanel, _settings, addins, _lastResult.Publishers)) return;

            SetButtons(false);
            _statusLabel.Text = $"{(isUpdate ? "Updating" : "Installing")} {addins.Count} addin(s)…";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var failed  = new List<string>();
                var blocked = new List<string>();
                bool anyStagedUpdate = false;
                foreach (var addin in addins)
                {
                    try
                    {
                        bool staged;
                        _installer.Install(addin, out staged);
                        if (staged) anyStagedUpdate = true;
                    }
                    catch (IdentityConflictException conflict)
                    {
                        // Refused rather than failed: installing would leave Clarion unable to
                        // start at all, so not installing is the correct outcome, not an error.
                        blocked.Add($"{addin.Name} — identity \"{conflict.IdentityName}\" is already "
                                    + $"installed in {Path.GetFileName(conflict.ExistingPath)}"
                                    + DescribeClashOwner(Path.GetFileName(conflict.ExistingPath)));
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
                    _installedAddins = _installedStore.Load(ClarionRootPath);
                    PopulateList();

                    // Reported before anything else: a refusal is the outcome the user most
                    // needs to understand, and it is not an error -- installing would have
                    // left Clarion unable to start.
                    if (blocked.Count > 0)
                        MessageBox.Show(_contentPanel,
                            "Not installed, because Clarion refuses to start when two addins "
                            + "declare the same identity:\r\n\r\n  "
                            + string.Join("\r\n  ", blocked)
                            + "\r\n\r\nRemove the existing copy first if you want to switch.",
                            "Already installed under another identity",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);

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
                        int installed = addins.Count - blocked.Count;
                        if (installed <= 0)
                        {
                            _statusLabel.Text = "Nothing installed.";
                        }
                        else
                        {
                            string[] names = addins.Select(a => a.Name).ToArray();
                            _statusLabel.Text = $"{installed} addin(s) installed. Please restart Clarion to activate.";
                            ShowRestartReminder(names, isUpdate ? RestartReason.Updated : RestartReason.Installed);
                        }
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
            _installedAddins = _installedStore.Load(ClarionRootPath);
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

        // Open a Markdown URL (README, changelog, …) inside ClarionMarkdownEditor
        // when it's installed, otherwise fall back to launching the URL in the
        // system browser. Pure runtime lookup — no compile-time reference on
        // the editor's DLL.
        private static void OpenMarkdownUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                var apiType = Type.GetType("ClarionMarkdownEditor.MarkdownEditorApi, ClarionMarkdownEditor");
                var openUrl = apiType?.GetMethod("OpenUrl", BindingFlags.Public | BindingFlags.Static);
                if (openUrl != null)
                {
                    openUrl.Invoke(null, new object[] { url });
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OpenMarkdownUrl reflection failed: {ex.Message}");
            }

            OpenUrl(url);
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
