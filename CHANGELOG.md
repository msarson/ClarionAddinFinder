# Changelog

All notable changes to Addin Finder are documented here.

## [0.9.0] - 2026-08-23

### Added
- **Addins distributed as a Windows setup installer.** Some addins ship as a setup `.exe` rather
  than as files Addin Finder can place. A registry entry with a `githubRepo` field is treated as
  one of these: the current release is resolved from GitHub, and the button becomes **Download**.

  Addin Finder downloads the installer and stops there. It does not run it. The setup elevates,
  executes code we have just told the user nobody reviews, and then chooses its own Clarion targets
  — possibly not the one running the pad. Handing over the file is the honest boundary: the user
  decides whether to run it, and Windows asks about elevation rather than us arranging it.

  Nothing is recorded as installed, and **Remove is not offered** — those files belong to the
  addin's own uninstaller, and deleting them behind its back would leave Windows believing it is
  still there. The addin still appears as installed afterwards, because the disk reconciliation
  added in 0.7.1 finds it like anything else.

  Setup addins live under their own **`setupAddins`** key rather than alongside ordinary entries,
  so builds before 0.9.0 never see them. That matters: such an entry has no download URLs, and an
  older client would list it, download nothing, create an *empty* folder under `accessory\addins`
  anyway, and record a phantom install — and an empty folder in the folder Clarion scans at start-up
  is the shape that has already stopped an IDE starting. Installing an entry with nothing to
  download is now refused outright as well, in case one is ever filed under the wrong key.

  The registry records a repository rather than a download URL, because publishers rename the asset
  every release — `ClarionAssistant-5.8-Setup.exe`, then `-5.8.1-` — so a pinned link would 404
  almost immediately. Resolving through the releases API also means **nobody maintains a version
  for these**: Clarion Assistant shipped eight releases in seven weeks, and any hand-kept entry
  would have been stale within days of each one. Answers are cached for six hours, so the pad does
  not ask GitHub on every open for a tag that changes a few times a month — and so a machine with no
  network still shows the addin and its last known version.
- **Addin Finder now warns when two addins under the same Clarion declare the same
  `<Identity name>`.** Clarion loads every subfolder of `accessory\addins` at start-up and refuses
  to start *at all* when that happens — the user gets "Identity name used by multiple addins" and
  no IDE.

  Installing through Addin Finder has been unable to cause this since 0.8.0, which refuses such an
  install. This covers every other route, and they are the common ones: another product's
  installer, a folder copied from a different Clarion, a hand-unzipped copy. The case that has
  actually cost a user their IDE is Clarion Assistant's installer writing the Markdown Editor to
  `accessory\addins\MarkdownEditor` while Addin Finder installs the same addin to
  `accessory\addins\ClarionMarkdownEditor` — two installers, each correct alone.

  It reports and never acts: removing one side would mean choosing on the user's behalf between two
  things Addin Finder may not have put there. Both full paths are given, so the choice can be made
  and acted on immediately. Detection reads each manifest rather than trusting folder names, since
  the two differ in practice — FlattenCode installs as `FlattenCode` and declares
  `<Identity name="FlattenCode.Addin"/>`.

- **You choose where a downloaded installer is saved.** Addin Finder asks before fetching one,
  starting from wherever you last saved one and from your Downloads folder the first time. What is
  being fetched is an executable you then have to find and run with elevation, and "it went
  somewhere, try Downloads" is not a good enough account of one. Cancel downloads nothing.

  Asked every time rather than set once in a screen you would have to go looking for — but the
  answer is remembered, so after the first time it is a keypress. A whole selection of setup addins
  is asked about once, not once each. The choice is per machine, not per Clarion: it is a fact about
  how you keep your downloads, not a decision about a Clarion installation.

### Fixed
- **A release published today is visible today.** The six-hour release cache was not caching for six
  hours — it was pinning the first answer of the calendar day until midnight. The pad refreshed with
  `DateTime.Today`, and that value became both the clock the cache was stamped with *and* the clock
  it was measured against, so every cached answer was perpetually nought hours old. A publisher
  could tag a release and nobody would see it that day, however many times they pressed Refresh.

  The pad now refreshes with a real clock. Two further changes make the cache immune rather than
  reliant on every caller getting that right: an entry is stamped from the actual time of the fetch
  and never from whatever the caller called "now", and an entry claiming to come from the future — a
  clock put back, a file copied between machines — is treated as stale rather than as nought hours
  old. A cache already written by an affected build corrects itself on the next refresh.
- **`1.0` and `1.0.0` are the same version.** Installed-versus-published was a text comparison, so
  an addin whose manifest wrote the version one way and whose registry entry wrote it the other
  showed "Update available" permanently, and installing could never clear it. It is now compared
  component by component.

  Ordinary addins never really hit this: the installed version is the very string Addin Finder read
  from the registry, so it matched character for character. It is addins that install themselves
  that this was waiting for — nothing is recorded for those, so the installed version is read from
  the publisher's `<Identity version>` and the published one from the release tag. Those are written
  by different hands on different days, and expecting them to agree on trailing zeroes was expecting
  too much of everybody. A copy that genuinely differs, in either direction, still reads as an
  update — a publisher who rolls back a bad release means the earlier one to be installed.
- **A publisher may only point at a setup installer in their own GitHub account.** The rule that a
  publisher can serve binaries only from their own account was checked on an entry's download URLs
  — and a setup entry has none, because the release asset is resolved from a repository instead. All
  three URL checks passed on empty strings, and the repository nobody was checking was the thing
  actually fetched. That is the worst place to have left the gap open: what arrives is an `.exe`
  that the user then runs with elevation. The repository's owner must now match the publisher id,
  and a violation drops the entry rather than the publisher, exactly as a bad URL does.

  Checked once more at the moment of download, because repositories move: GitHub follows a transfer
  or a rename transparently, so a repository that belonged to the publisher yesterday can resolve to
  assets under another account today without a character of the registry changing. The asset URL
  comes back from GitHub rather than from the entry, so that is the one worth checking last.
- **A setup addin no longer turns back into an ordinary one after a trip through the cache.** The
  registry cache copied every field of an entry except the repository — and the repository is what
  makes an addin self-installing. Any fallback went through it: a publisher who could not be
  reached, or a machine with no network at all. The addin came back looking like one Addin Finder
  could place, so the button read **Install** rather than Download, on an entry that by design has
  no URLs to install from. The refusal added earlier in 0.9.0 stopped that going anywhere harmful,
  but the offer should never have been made — and with the repository gone, the last known version
  could not be looked up either, which is the one thing the cache was there to keep showing.
- **An addin that has just been withdrawn is described, not reduced to a folder name.** The cache
  exists partly so that someone still running a withdrawn addin can be told what it is and who
  wrote it. That never actually happened: a refresh replaces a publisher's list wholesale, and it
  does so *before* anything asks what became of an installed addin — so the entry being asked about
  had already been overwritten, and the user was shown a bare id with no description, author or
  homepage, at the moment they most needed them. Entries that drop out of a list are now kept aside
  under their own `retired` key, and an addin that reappears comes back off that shelf.

  They are deliberately not kept among the publisher's own entries, which are read back as that
  publisher's current list — a withdrawn addin filed there would simply be offered for installation
  again.

  This also settles what is said about an addin nobody installed *through* Addin Finder, such as one
  that arrived via its own setup. The report is about the listing, never about a relationship:
  "a publisher we follow used to list this and no longer does" is true however the addin got there,
  while an addin nobody we follow has ever listed still says nothing at all. Remove stays unoffered
  for a withdrawn setup addin, which is the second thing the repository fix above quietly buys.

## [0.8.0] - 2026-08-22

### Added
- **Federated publisher registries.** The root registry now records **publishers** rather than
  individual addins; each publisher hosts their own `addins.json` and publishes without anyone
  else's involvement. A publisher's list URL is *derived* from their GitHub identity and the
  branch recorded for them, never taken as a free-form URL.
- **Download URLs are checked against the publisher.** An entry whose download does not come from
  `github.com/<publisher>/` is dropped and logged. Being listed once is not permission to serve
  binaries from anywhere later. A bad URL drops that entry, not the whole publisher.
- **Identity collision check before installing.** Clarion refuses to start if two folders under
  `accessory\addins` declare the same `<Identity name>`. An install that would create one is now
  refused, naming the folder and publisher already holding it, rather than breaking the IDE. This
  is what lets the registry avoid tracking which publisher owns which addin id.
- **Addin and publisher lifecycle.** A publisher can mark an addin `deprecated` with a note, or be
  recorded as `abandoned`; the registry can `revoke` a publisher. None of these are offered to new
  users, and all stay visible to anyone who already has them installed.
- **The list is grouped by publisher**, with each publisher's state in the group header. Entries
  from the legacy list, adopted from disk, or placed by another installer group under **Unknown
  publisher** -- never under a publisher we cannot actually vouch for.
- **Consent before installing, in two parts.** The general terms -- what an addin can do, and that
  nobody reviews them -- describe the system and are shown once, with a versioned acknowledgement
  so they can return if the wording materially changes. A short publisher section names who is
  about to run code on the machine, and appears the first time you install from *each* publisher:
  trusting one publisher says nothing about the next, and a registry that grows must not quietly
  opt a user into publishers added long afterwards. An addin with no identified publisher -- from
  the legacy list, or found already installed -- says so plainly rather than implying provenance
  it does not have.

  The split is deliberate. Repeating the same warning is how people are taught to click through
  it, so the part that recurs is the part that differs every time: a name, an account, a link to
  read before trusting it.

  One case is never remembered: an addin with **no identified publisher**. There is no party to
  have made a decision about, so accepting one unattributed addin carries no information about the
  next, and the warning appears every single time one is installed. It names the addins in
  question, which is what keeps a repeated warning informative rather than wallpaper.

- **A one-time explanation on upgrading**, shown inside the pad in place of its usual contents
  rather than as a dialog. Clarion restores whichever pads were open last time, so a docked pad is
  created during start-up and a modal there interrupts the IDE coming up before the user has asked
  for anything. The list loads underneath while the notice is up, so dismissing it reveals a pad
  that is already populated. Anyone arriving from an earlier version is told what changed and why: publishing used to require
  every addin, and every new version of one, to be added to a single central list, which put one
  person in the path of everybody else's releases and fell hardest on publishers already doing the
  work. Someone installing Addin Finder for the first time is deliberately not shown it -- they
  have no previous behaviour to be told about.

- **Consent and the change notice are recorded per Clarion installation.** They were global, so
  accepting in Clarion 11 silently accepted in Clarion 12 -- which may still be running an older
  build that has not been told what it would be accepting. Same shape as the installed-addin fix
  in 0.7.0. The restart reminder stays global on purpose: that is a preference about the tool's
  own chattiness, not a decision about trusting anyone.
- **The new settings format lives in `settings.v2.json`.** Builds up to 0.7.1 read `settings.json`
  by searching the raw text for the literal `"suppressRestartReminder": true`, so writing the new
  document there would leave that preference silently off for any Clarion still on an older build.
- **Addins that Addin Finder never published are no longer listed as installed.** Since 0.7.1 the
  store adopts every addin folder found on disk, which is right for collision checks and for not
  overwriting other people's work -- but Clarion Assistant, a hand-unzipped copy, or anything
  another installer placed is not ours to list. Doing so claimed a relationship that never existed
  and offered actions we have no business offering.

- **A stale `<Identity version>` no longer causes a permanent phantom "update available".** The
  manifest is the publisher's self-report and it goes stale -- FlattenCode has shipped 1.0.1
  through 1.0.3 all declaring `version="1.0"`. Reconciliation took that as truth, overwrote the
  version actually installed with a lower one, and the comparison against the registry then
  offered the update again on every load. The manifest may now only move a recorded version
  *forward*: higher means something updated the addin behind our back and is worth knowing; lower
  means the attribute was not maintained, and what we installed remains the better answer.
- **The addin id is no longer assumed to be its `<Identity name>`.** FlattenCode is published as
  `FlattenCode` with `<Identity name="FlattenCode.Addin"/>`, so the collision check was looking for
  a name no manifest declares and would have missed a real clash. The check now reads the identity
  out of the payload it has just downloaded, and runs at install time rather than from the registry
  entry -- only the file itself knows what it declares.

### Changed
- A failed publisher fetch no longer empties the pad. Each publisher is fetched independently and
  in parallel; a failure falls back to that publisher's cached list, shown as such.
- **A publisher's list vanishing is treated as a staged signal, never a fact.** "Could not reach"
  and "the server says it is not there" are kept distinct, and repeated 404s must persist across
  several days before the list is described as withdrawn. A publisher's outage must never tell
  users their addins were withdrawn, and nothing installed is ever touched on the strength of it.
- The legacy flat `addins` list in the root registry is still read and merged, so nothing breaks
  while publishers migrate. A publisher-sourced entry wins over a legacy entry with the same id.

## [0.7.1] - 2026-08-22

### Fixed
- **0.7.0 broke Addin Finder in any Clarion still running an older build.** It rewrote
  `installed.json` in the new format, and builds up to 0.6.0 read that file by looking
  for an `addins` key -- finding none, they report nothing installed, and the next
  install they perform writes the old format back over the top, destroying the new
  data. Each Clarion carries its own copy of Addin Finder and updates on its own
  schedule, so upgrading one Clarion could break another. The new format now lives in
  its **own file**, `installed.v2.json`, and `installed.json` is never written again.
  If 0.7.0 already rewrote it, 0.7.1 restores it from the backup 0.7.0 left behind.
- **An addin present in more than one Clarion was only recorded for the first one.**
  A pre-0.7.0 file recorded one entry per addin with no root, so the first Clarion to
  start claimed it and the others reported the addin as not installed while it sat in
  their addins folder -- and the pad would offer to install over a working copy. Any
  addin found on disk is now adopted for that Clarion, whether it came from a
  pre-0.7.0 entry, another Clarion, a hand-unzipped copy, or another installer.

## [0.7.0] - 2026-08-22

### Fixed
- **Installed addins are now tracked per Clarion installation** ([#6](https://github.com/msarson/ClarionAddinFinder/issues/6)).
  `installed.json` recorded addins by id alone, so on a machine with Clarion 10, 11,
  11.1 and 12 — four separate addin folders — installing into one reported the addin
  as installed in all of them, and uninstalling from one marked it gone everywhere
  while the files remained. Entries are now keyed by Clarion root.
- **The store is reconciled against disk on every load.** It recorded what we *believed*
  we had done rather than what is actually installed, and never checked. An entry now
  survives only if the addin folder really holds a manifest, and the version is read
  from that manifest's `<Identity version="…"/>` rather than trusted from JSON. A store
  that has drifted for any reason now corrects itself at the next start.
- **A failed install no longer leaves an empty addin folder behind.** The folder was
  created before anything was downloaded, and only `IOException` was caught — but a
  failed download throws `WebException`, which does not derive from it. A 404 or dropped
  connection left an empty folder that Clarion reports at startup as a broken addin.
  Downloads now land in a scratch folder outside the scanned root and are moved into
  place only once complete.
- **A staged install no longer claims a version that is not on disk yet.** When files are
  locked the payload goes to the pending folder, but the store recorded it as installed
  immediately. Such entries are now marked staged and confirmed once applied.

### Changed
- `installed.json` moves to **format version 2**. Migration is automatic and happens on
  first load of the new version, so it cannot run before the new code is live. A v1 file
  is copied to `installed.json.v1.bak` first, and its entries are attributed to a Clarion
  root only where the addin is actually present on disk — each Clarion claims its own the
  first time it runs. Nothing is discarded on the assumption that it is gone.
- AddinFinder's own manifest now carries a version in its `<Identity>` element.

## [0.6.0] - 2026-07-27

### Added
- **Clickable author** in the addin detail panel — when a registry entry supplies
  the new optional `authorUrl` field, the author name in the "by …" line becomes a
  link to the developer's page (e.g. their GitHub profile). Entries without
  `authorUrl` render exactly as before (plain gray text), so this is fully backward
  compatible. Implemented by promoting the author label to a `LinkLabel` and linking
  only the name portion via `LinkArea`.

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
