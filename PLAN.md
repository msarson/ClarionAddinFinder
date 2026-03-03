# Clarion Addin Finder — Plan

## Scope (v1)

**MIT / open source addins only.** No paid, no license keys, no web server.
All registry data lives in a public GitHub repo fetched over HTTPS.

---

## How It Works

```
[Clarion IDE]
     │
     └── AddinFinder addin (this project)
              │
              ├── fetch ──► GitHub raw JSON registry
              │              (public repo: clarion-addin-registry)
              │
              ├── display ──► pane listing available addins
              │                name / description / author / version / status
              │
              ├── install ──► download DLL + .addin from GitHub Release asset
              │               copy to Clarion addins folder
              │               prompt user to restart IDE
              │
              └── update ──► compare installed version vs registry version
                             highlight outdated addins
```

---

## Registry Format

Hosted in a separate public GitHub repo: `clarion-addin-registry`
File: `registry.json`

```json
{
  "version": 1,
  "updated": "2026-03-03",
  "addins": [
    {
      "id": "GitPane",
      "name": "GitPane",
      "description": "Git integration for the Clarion IDE — stage, commit, push, branch, stash and history from a docked pane.",
      "author": "msarson",
      "license": "MIT",
      "category": "Source Control",
      "version": "1.0.7",
      "clarionMinVersion": "11",
      "downloadUrl": "https://github.com/msarson/Clarion-GitPane/releases/download/v1.0.7/GitPane.zip",
      "homepageUrl": "https://github.com/msarson/Clarion-GitPane",
      "changelogUrl": "https://github.com/msarson/Clarion-GitPane/blob/master/CHANGELOG.md"
    }
  ]
}
```

**Download package** = a `.zip` containing:
- `<AddinName>.dll`
- `<AddinName>.addin`
- (optional) any dependency DLLs

---

## Installed Version Tracking

A local file tracks what's installed:
`%APPDATA%\ClarionAddinFinder\installed.json`

```json
{
  "addins": [
    { "id": "GitPane", "version": "1.0.7", "installedAt": "2026-03-03" }
  ]
}
```

This lets the finder show: `✓ up to date`, `↑ update available`, `— not installed`.

---

## UI

A docked **pane** (same pattern as GitPane) with:
- Toolbar: **Refresh** button
- `ListView` columns: Name | Author | Category | Version | Status
- Detail panel below list: description + homepage link + changelog link
- Buttons: **Install** / **Update** / **Uninstall**
- Status bar showing last-refreshed timestamp

---

## Install Flow

1. User clicks **Install**
2. Finder downloads the `.zip` to a temp folder
3. Extracts DLL + `.addin` to `<ClarionRoot>\accessory\addins\<AddinName>\`
4. Writes entry to `installed.json`
5. Shows message: "Installed. Please restart Clarion to activate."

**Clarion root detection:** walk up from the addin DLL location (same pattern as AccuraBuildSwitcher).

---

## Bootstrapping

The AddinFinder is itself distributed as a GitHub Release:
- User downloads `AddinFinder.zip` manually once from GitHub
- Extracts to Clarion addins folder
- Thereafter AddinFinder can update itself

---

## Tech Stack

- C# targeting **.NET 4.0** (matches all other addins)
- **WinForms** pane
- **`WebClient`** for HTTP downloads (simpler than HttpClient on .NET 4.0)
- **`Newtonsoft.Json`** or manual JSON parsing (avoid heavy deps — consider manual)
- **`System.IO.Compression`** for zip extraction (available in .NET 4.5+; may need fallback)

> Note: Clarion ships with .NET 4.x — check whether `System.IO.Compression.ZipFile`
> is available. If not, use a small bundled zip helper or require DLLs distributed unzipped.

---

## Delivery Phases

### Phase 1 — Registry + Viewer
- [ ] Define and publish `registry.json` in `clarion-addin-registry` repo
- [ ] Project scaffold: `AddinFinder.csproj`, `AddinFinder.addin`
- [ ] `RegistryClient` — fetches and deserialises `registry.json`
- [ ] `AddinFinderPad` — pane with ListView showing registry contents (read-only, no install yet)
- [ ] Show installed vs available status by reading `installed.json`

### Phase 2 — Install / Update
- [ ] Download + unzip to addins folder
- [ ] Write/update `installed.json`
- [ ] "Restart required" prompt
- [ ] Self-update support

### Phase 3 — Polish
- [ ] Auto-refresh on IDE startup (background, non-blocking)
- [ ] Uninstall support (delete files, remove from `installed.json`)
- [ ] Categories / filtering
- [ ] Changelog viewer (fetch markdown, render as plain text)

---

## Open Questions

- Does Clarion's .NET runtime include `System.IO.Compression`? If not, distribute addins as flat zips or use a bundled extractor.
- Should the registry URL be hardcoded or configurable (for community forks)?
- Who can submit to the registry? PR-based for now — revisit when community grows.
