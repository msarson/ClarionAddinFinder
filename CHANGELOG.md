# Changelog

All notable changes to Addin Finder are documented here.

## [0.5.17] - 2026-05-15

### Added
- **View README link** in the addin detail panel ([#2](https://github.com/msarson/ClarionAddinFinder/issues/2))
  — opens the selected registry addin's README inside Clarion Markdown Editor v1.1.0
  or later via reflection on `MarkdownEditorApi.OpenUrl`. No compile-time reference
  on the editor's DLL — purely runtime lookup. Falls back to launching the homepage
  URL in the system browser when the editor isn't installed.
- **Changelog link** now also routes through the editor-or-browser fallback — clicking
  Changelog renders the addin's `CHANGELOG.md` inline in the editor when available,
  rather than always bouncing to the browser.

### Fixed
- **Empty list when the pad isn't visible at IDE startup** ([#3](https://github.com/msarson/ClarionAddinFinder/issues/3))
  — the initial registry fetch was wired to `_contentPanel.VisibleChanged`, which only
  fires on transitions. When the pad was created lazily on first reveal, the panel was
  often already `Visible` by the time the handler attached, so the event never fired
  and the list stayed empty until the user clicked Refresh. Moved the initial fetch
  to `HandleCreated`, which is guaranteed to fire once when the control joins the
  visual tree. `VisibleChanged` is kept for splitter sizing, which legitimately needs
  the laid-out height.

## [0.5.16] - 2026-03-19

### Build
- Release process automated via MSBuild target — `dotnet build -c Release /p:DoRelease=true` builds, commits `version.json`, tags, pushes, and creates the GitHub release in one step

## [0.5.15] - 2026-03-19

### Build
- Version is now a single source of truth in `<Version>` in the csproj — `version.json` and `AssemblyFileVersion` are both derived from it automatically on Release build
- GitHub Actions release workflow: push a `v*.*.*` tag to build, publish `version.json` to master, and create the GitHub release atomically

## [0.5.14] - 2026-03-19

### Fixed
- Installed addins no longer fail to load in Clarion with `FileLoadException` / `NotSupportedException` — the NTFS `Zone.Identifier` alternate data stream (Mark of the Web) is now stripped from every downloaded file immediately after install

### Documentation
- README now includes a clear note for users who hit the "Could not be loaded" error on first install, with both GUI (right-click → Unblock) and PowerShell (`Unblock-File`) remediation steps

## [0.5.13] - 2026-03-03

### Added
- Zip release asset (`AddinFinder-vX.X.X.zip`) for easier first-time installation — extract to addins folder and restart

### Fixed
- Pad title now shows correct version after self-update (reads disk file version, not in-memory assembly)

## [0.5.11] - 2026-03-03

### Fixed
- Self-update now requires only one Clarion restart (compare disk file version vs version.json, not in-memory assembly version)

## [0.5.8] - 2026-03-03

### Fixed
- Self-update banner not appearing — version check now runs on same background thread as registry fetch

## [0.5.6] - 2026-03-03

### Fixed
- Self-update apply now works: rename-before-copy allows replacing a loaded DLL (FILE_SHARE_DELETE)
- Removed hardcoded paths from StageSelfUpdate — folder/filenames derived from Assembly.Location

## [0.5.4] - 2026-03-03

### Added
- Pad title shows installed version (e.g. Addin Finder v0.5.4)

## [0.5.0] - 2026-03-03

### Added
- Initial public release
- Browse and install addins from the community registry (msarson/clarion-addin-registry)
- One-click install, update, and uninstall of addins
- Staged update/uninstall — locked DLLs are staged and applied on next Clarion restart
- Detail panel with description, author, version, homepage and changelog links
- Reinstall button for re-applying the current version without bumping
- Restart reminder dialog — lists affected addins, with don't-show-again option
- Self-update mechanism — checks version.json from this repo on every refresh; amber banner shown when a new version is available
- Retry logic (3 attempts with backoff) for all downloads
- Icon registration at IDE startup
- MIT licence
