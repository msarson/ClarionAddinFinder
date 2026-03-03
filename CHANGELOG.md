# Changelog

All notable changes to Addin Finder are documented here.

## [0.5.0] - 2026-03-03

### Added
- Initial public release
- Browse and install addins from the community registry ([msarson/clarion-addin-registry](https://github.com/msarson/clarion-addin-registry))
- One-click install, update, and uninstall of addins
- Staged update/uninstall — locked DLLs are staged and applied on next Clarion restart
- Detail panel with description, author, version, homepage and changelog links
- Reinstall button for re-applying the current version without bumping
- Restart reminder dialog — lists affected addins, with "don't show again" option
- Self-update mechanism — checks `version.json` from this repo on every refresh; amber banner shown when a new version is available
- Retry logic (3 attempts with backoff) for all downloads
- Icon registration at IDE startup
- MIT licence
