# Clarion Addin Finder

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Latest Release](https://img.shields.io/github/v/release/msarson/ClarionAddinFinder)](https://github.com/msarson/ClarionAddinFinder/releases/latest)

A dockable addin manager for the **Clarion IDE** — discover, install, update, and remove community addins without leaving the IDE.

---

## Features

- 📦 **Browse & install** addins from the community registry with one click
- 🔄 **Update** installed addins — staged automatically so Clarion never crashes
- 🗑️ **Uninstall** addins — also staged when the DLL is in use
- 🔔 **Self-updates** — checks for a new version of Addin Finder itself on every refresh
- ℹ️ **Detail panel** — description, author, version, homepage and changelog links
- ✅ **Restart reminder** — tells you which addins need a Clarion restart, with a "don't show again" option

---

## Installation

Addin Finder is bootstrapped manually the first time; after that it updates itself.

1. Download **`AddinFinder.dll`** and **`AddinFinder.addin`** from the [latest release](https://github.com/msarson/ClarionAddinFinder/releases/latest).
2. Copy both files into your Clarion addins folder, e.g.:
   ```
   C:\Clarion\Clarion11.1\accessory\addins\AddinFinder\
   ```
3. Restart Clarion — the **Addin Finder** pad will appear under *View → Pads*.

---

## Publishing an Addin

Addins are listed in the community registry at:

> **[msarson/clarion-addin-registry](https://github.com/msarson/clarion-addin-registry)**

To add your addin:

1. Fork [clarion-addin-registry](https://github.com/msarson/clarion-addin-registry) and edit `registry.json`.
2. Add an entry following the schema documented in that repo's README.
3. Open a Pull Request — once merged your addin will appear in Addin Finder on the next refresh.

---

## How It Works

- **Registry** — a JSON manifest hosted on GitHub. Addin Finder fetches it fresh on every refresh.
- **Install / Update** — DLL (and optional `.addin` file or zip) is downloaded to a staging folder in `%APPDATA%\ClarionAddinFinder\pending\` and copied into the Clarion addins directory. If the target file is locked (Clarion has it loaded), the operation is staged and applied on the next Clarion restart.
- **Self-update** — Addin Finder reads `version.json` from this repo's `main` branch. If a newer version is available, an amber banner appears with an *Update Now* button. The update is always staged (since the running DLL is always locked).

---

## Development

```
dotnet build -c Release
```

Requires .NET SDK 6+ to build (targets `net48`).

---

## License

[MIT](LICENSE) — © msarson
