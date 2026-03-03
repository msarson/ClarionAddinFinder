# Changelog

All notable changes to Addin Finder are documented here.

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
