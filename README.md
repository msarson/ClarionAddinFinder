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
- ℹ️ **Detail panel** — description, clickable **author** (links to the developer's page when the registry provides one), version, homepage and changelog links
- ✅ **Restart reminder** — tells you which addins need a Clarion restart, with a "don't show again" option

---

## Installation

Addin Finder is bootstrapped manually the first time; after that it updates itself.

1. Download the latest **`AddinFinder-v*.zip`** (e.g. `AddinFinder-v0.9.0.zip`) from the [latest release](https://github.com/msarson/ClarionAddinFinder/releases/latest).
2. Extract the zip into your Clarion addins folder, e.g.:
   ```
   C:\Clarion\Clarion11.1\accessory\addins\AddinFinder\
   ```
3. Restart Clarion — the **Addin Finder** pad will appear under *View → Pads*.

> The zip contains `AddinFinder.dll` and `AddinFinder.addin`. Both files must be in the same folder.

> **⚠️ "Could not be loaded" error?**  
> Windows marks files downloaded from the internet as untrusted. If Clarion shows a `FileLoadException` / `NotSupportedException` on startup, right-click **`AddinFinder.dll`** → *Properties* → tick **Unblock** → OK, then restart Clarion.  
> Alternatively, run this in PowerShell from the addin folder:
> ```powershell
> Unblock-File .\AddinFinder.dll
> ```
> This only affects the initial manual install — subsequent self-updates are handled automatically.

---

## Publishing an Addin

Publishers list their own addins. Ask to be added to the
[root registry](https://github.com/msarson/clarion-addin-registry), then keep your own
`addins.json` — see [msarson/clarion-addins](https://github.com/msarson/clarion-addins) for a
working example.

Two rules are enforced by the client rather than by review: downloads must come from your own
GitHub account, and an addin's `<Identity name>` must not already be in use — Clarion refuses
to start when two addin folders declare the same one.

### Legacy: submitting to the flat list

Addins are listed in the community registry at:

> **[msarson/clarion-addin-registry](https://github.com/msarson/clarion-addin-registry)**

To add your addin:

1. Fork [clarion-addin-registry](https://github.com/msarson/clarion-addin-registry) and edit `registry.json`.
2. Add an entry following the schema documented in that repo's README.
3. Open a Pull Request — once merged your addin will appear in Addin Finder on the next refresh.

---

## How It Works

- **Registry** — the root registry records **publishers**. Each publisher keeps their own addin
  list in their own repository, and Addin Finder fetches every publisher's list on refresh. A
  publisher's list URL is derived from their GitHub identity, and their downloads must come from
  their own account — so being listed once does not become permission to serve anything from
  anywhere later. The older flat list is still read while publishers migrate.
- **If a publisher cannot be reached**, their addins are shown from the last known list, marked
  as such. A publisher's outage never empties the pad, and never suggests their addins were
  withdrawn — that takes repeated "not found" answers over several days, and even then nothing
  already installed is touched.
- **Publishers and addins have a lifecycle.** A publisher can mark an addin as deprecated, or
  stop publishing altogether. Those are no longer offered, but stay visible to anyone who
  already has them, with the publisher's own note.
- **Install / Update** — the payload is downloaded to `%APPDATA%\ClarionAddinFinder\` first and moved into the Clarion addins directory only once it is complete, so a failed download never leaves a half-written folder where Clarion would find it. If the target file is locked (Clarion has it loaded), the operation is staged and applied on the next Clarion restart.
- **Self-update** — Addin Finder reads `version.json` from this repo's `master` branch. If a newer version is available, an amber banner appears with an *Update Now* button. The update is always staged (since the running DLL is always locked), so the new build is written at one restart and runs from the next.
- **What is installed** — tracked per Clarion installation in `%APPDATA%\ClarionAddinFinder\installed.v2.json`. If you run Clarion 10, 11, 11.1 and 12, each has its own `accessory\addins` folder and its own set of installed addins; installing into one does not affect the others. The file is a cache, not the record: every load checks the addin folder on disk and reads the version from the addin's own manifest, so if something changes behind Addin Finder's back the list corrects itself the next time Clarion starts. An addin already sitting in a Clarion's addins folder is picked up automatically, however it got there.
- **Older builds** — the file that versions up to 0.6.0 use, `installed.json`, is left alone. Each Clarion has its own copy of Addin Finder and updates when it is next started, so an old and a new build can share this folder for a while; keeping the formats in separate files means neither overwrites the other.

---

## Development

```
dotnet build -c Release
```

Requires .NET SDK 6+ to build (targets `net48`). Set `CLARION_ROOT` if Clarion is not at
`C:\Clarion\Clarion11.1` — the build references `ICSharpCode.Core.dll` and
`ICSharpCode.SharpDevelop.dll` from its `bin` folder.

`tools-test-store.ps1` exercises the installed-addin store against a simulated two-Clarion
machine. Run it under **32-bit** Windows PowerShell, since the assembly is `PlatformTarget=x86`:

```
C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -File tools-test-store.ps1
```

---

## License

[MIT](LICENSE) — © msarson
