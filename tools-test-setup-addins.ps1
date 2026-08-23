# Addins distributed as a Windows setup installer.
#
# These cannot be installed by us: the setup elevates, chooses its own Clarion targets and writes
# files we did not place. Addin Finder downloads it and gets out of the way.
#
# Run under 32-bit Windows PowerShell.

$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::LoadFrom('F:\github\ClarionAddinFinder\bin\Release\net48\AddinFinder.dll')

$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else     { Write-Host "  FAIL  $name -- $detail" -ForegroundColor Red; $script:fail++ }
}

$sandbox = Join-Path $env:TEMP ('af-setup-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$store   = Join-Path $sandbox 'store'
New-Item -ItemType Directory -Force -Path $store | Out-Null

$parser = $asm.GetType('AddinFinder.SimpleJsonParser')
$static = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public
$parseRelease = $parser.GetMethod('ParseGithubRelease', $static)

Write-Host "`n1. Reading a release payload"
# The shape GitHub actually returns, with the asset name carrying the version -- which is why the
# URL cannot be pinned in the registry.
$json = @'
{ "tag_name": "v5.8.1",
  "assets": [ { "name": "ClarionAssistant-5.8.1-Setup.exe",
                "browser_download_url": "https://github.com/ClarionLive/ClarionAssistant/releases/download/v5.8.1/ClarionAssistant-5.8.1-Setup.exe" } ] }
'@
$rel = $parseRelease.Invoke($null, @([string]$json))
Check 'tag read'                ($rel.Tag -eq 'v5.8.1') "got '$($rel.Tag)'"
Check 'version strips the v'    ($rel.Version -eq '5.8.1') "got '$($rel.Version)'"
Check 'asset name read'         ($rel.AssetName -eq 'ClarionAssistant-5.8.1-Setup.exe') "got '$($rel.AssetName)'"
Check 'usable'                  ($rel.IsUsable) 'not usable'

Write-Host "`n2. Choosing the asset when a release carries several"
$multi = @'
{ "tag_name": "v2.0",
  "assets": [ { "name": "release-notes.md",  "browser_download_url": "https://example/notes" },
              { "name": "MyAddin-2.0.exe",   "browser_download_url": "https://example/setup" },
              { "name": "source.zip",        "browser_download_url": "https://example/src" } ] }
'@
$m = $parseRelease.Invoke($null, @([string]$multi))
Check 'picks the .exe, not the first asset' ($m.AssetName -eq 'MyAddin-2.0.exe') "got '$($m.AssetName)'"

Write-Host "`n3. A release with no installer is not usable"
$none = '{ "tag_name": "v1.0", "assets": [ { "name": "a.md", "browser_download_url": "x" }, { "name": "b.txt", "browser_download_url": "y" } ] }'
Check 'no asset chosen' ($null -eq $parseRelease.Invoke($null, @([string]$none))) 'invented an installer'

$single = '{ "tag_name": "v1.0", "assets": [ { "name": "Installer.bin", "browser_download_url": "https://example/x" } ] }'
$s1 = $parseRelease.Invoke($null, @([string]$single))
Check 'a lone asset of any type is taken' ($null -ne $s1 -and $s1.AssetName -eq 'Installer.bin') 'lone asset rejected'

Write-Host "`n4. githubRepo puts an addin in download-only mode"
$addin = $asm.CreateInstance('AddinFinder.RegistryAddin')
Check 'a normal addin is not setup'  (-not $addin.IsSetup) 'wrongly flagged'
$addin.GithubRepo = 'ClarionLive/ClarionAssistant'
Check 'githubRepo makes it setup'    ($addin.IsSetup) 'not flagged'

Write-Host "`n5. Installing a setup addin is refused, not attempted"
$storeType = $asm.GetType('AddinFinder.InstalledAddinStore')
$st = $storeType.GetConstructor([Type[]]@([string])).Invoke(@([string]$store))
$inst = $asm.GetType('AddinFinder.AddinInstaller').GetConstructor([Type[]]@([string], $storeType)).Invoke(@([string]$sandbox, $st))
$addin.Name = 'Clarion Assistant'
$refused = $false
try { $staged = $false; $inst.Install($addin, [ref]$staged) } catch { $refused = $true }
Check 'Install() refuses a setup addin' $refused 'it tried to install files it cannot place'

Write-Host "`n6. Downloading needs a resolved release"
$threw = $false
try { $asm.GetType('AddinFinder.AddinInstaller').GetMethod('DownloadSetup', $static).Invoke($null, @($addin, [string]$sandbox)) }
catch { $threw = $true }
Check 'no release resolved -> refuses rather than guessing a URL' $threw 'attempted a download with no asset'

Write-Host "`n7. Against the real repository (one live API call)"
$grType = $asm.GetType('AddinFinder.GithubReleases')
$gr = $grType.GetConstructor([Type[]]@([string])).Invoke(@([string]$store))
$live = $gr.Resolve('ClarionLive/ClarionAssistant', [DateTime]::Now)
if ($null -eq $live) {
    Write-Host "  SKIP  no answer from GitHub (rate limit or offline)" -ForegroundColor Yellow
} else {
    Check 'resolved a tag'        ($live.Tag -match '^v?\d') "got '$($live.Tag)'"
    Check 'resolved an installer' ($live.AssetUrl -like 'https://github.com/ClarionLive/*') "got '$($live.AssetUrl)'"
    Check 'asset name carries the version (why the URL cannot be pinned)' `
        ($live.AssetName -match '\d') "got '$($live.AssetName)'"
    Write-Host "  live: $($live.Tag) -> $($live.AssetName)" -ForegroundColor DarkGray

    Write-Host "`n8. The answer is cached, so a refresh does not spend rate limit"
    Check 'cache file written' (Test-Path (Join-Path $store 'release-cache.json')) 'nothing cached'
    $again = $gr.Resolve('ClarionLive/ClarionAssistant', [DateTime]::Now)
    Check 'same answer returned' ($again.Tag -eq $live.Tag) 'cache miss'
    $stale = $gr.Resolve('ClarionLive/ClarionAssistant', ([DateTime]::Now).AddHours(48))
    Check 'a stale entry is refreshed rather than reused blindly' ($null -ne $stale) 'lost the entry'
}

Write-Host "`n9. An unknown repository does not throw"
$missing = $gr.Resolve('msarson/definitely-not-a-real-repo-xyz', [DateTime]::Now)
Check 'returns nothing instead of failing the refresh' ($null -eq $missing) 'unexpected result'

Write-Host "`n10. A setup addin survives the registry cache"
# Every fallback path goes through here: a publisher we could not reach, and a machine with no
# network at all. The repository is what makes the addin self-installing, so losing it in the cache
# turned Download back into Install -- for an entry with no URLs to install from.
$cacheStore = Join-Path $sandbox 'cache-store'
New-Item -ItemType Directory -Force -Path $cacheStore | Out-Null

$cacheType = $asm.GetType('AddinFinder.RegistryCache')
$cache = $cacheType.GetConstructor([Type[]]@([string])).Invoke(@([string]$cacheStore))

$entry = $asm.CreateInstance('AddinFinder.RegistryAddin')
$entry.Id = 'ClarionAssistant'; $entry.Name = 'Clarion Assistant'
$entry.GithubRepo = 'ClarionLive/ClarionAssistant'
$entry.Publisher  = 'clarionlive'
Check 'a repository is what makes it self-installing' ($entry.IsSetup) 'not read as a setup addin'

$addinList = [Activator]::CreateInstance(
    [System.Collections.Generic.List`1].MakeGenericType($asm.GetType('AddinFinder.RegistryAddin')))
$addinList.Add($entry)
$cache.Put('clarionlive', $addinList)

# A second instance, so this is what the NEXT session reads rather than what this one remembers.
$reread = $cacheType.GetConstructor([Type[]]@([string])).Invoke(@([string]$cacheStore))
$back = $reread.Get('clarionlive')
Check 'the entry comes back'   ($back.Count -eq 1) "got $($back.Count)"
Check 'with its repository'    ($back[0].GithubRepo -eq 'ClarionLive/ClarionAssistant') "got '$($back[0].GithubRepo)'"
Check 'so the button still says Download' ($back[0].IsSetup) 'came back as one we could place'
Check 'and it is marked stale rather than passed off as current' ($back[0].FromCache) 'not marked as cached'

Write-Host "`n11. The installer must come from the publisher's own account"
# The repository is checked against the publisher when the list is read (tools-test-federation), but
# repositories move: GitHub follows a transfer or a rename transparently, so a name that belonged to
# the publisher yesterday can resolve to assets under another account today with nothing in the
# registry having changed. What arrives is an .exe the user then runs with elevation, so the URL
# actually about to be fetched is checked again at the last moment.
$installer = $asm.GetType('AddinFinder.AddinInstaller')
$downloadSetup = $installer.GetMethod('DownloadSetup', $static)

function New-SetupAddin([string]$publisher, [string]$assetUrl) {
    $a = $asm.CreateInstance('AddinFinder.RegistryAddin')
    $a.Id = 'Moved'; $a.Name = 'Moved Addin'
    $a.Publisher = $publisher; $a.GithubRepo = "$publisher/Moved"
    $r = $asm.CreateInstance('AddinFinder.GithubRelease')
    $r.Tag = 'v1.0.0'; $r.AssetName = 'Moved-1.0.0-Setup.exe'; $r.AssetUrl = $assetUrl
    $a.Release = $r
    return $a
}

$moved = New-SetupAddin 'msarson' 'https://github.com/someoneelse/Moved/releases/download/v1.0.0/Moved-1.0.0-Setup.exe'
$refused = $false; $why = ''
try { $downloadSetup.Invoke($null, @($moved, [string]$sandbox)) }
catch { $refused = $true; $why = $_.Exception.InnerException.Message }
Check 'a transferred repository is refused' $refused 'downloaded an installer from another account'
Check 'and the refusal names the account it would have come from' `
    ($why -like '*someoneelse*') "message was '$why'"
Check 'nothing landed in Downloads' `
    (-not (Test-Path (Join-Path $env:USERPROFILE 'Downloads\Moved-1.0.0-Setup.exe'))) `
    'the installer was written anyway'

$noRelease = $asm.CreateInstance('AddinFinder.RegistryAddin')
$noRelease.Id = 'Unresolved'; $noRelease.Name = 'Unresolved'; $noRelease.GithubRepo = 'msarson/Unresolved'
$refused2 = $false
try { $downloadSetup.Invoke($null, @($noRelease, [string]$sandbox)) } catch { $refused2 = $true }
Check 'an unresolved release is refused rather than downloaded from nowhere' $refused2 'it proceeded'

Write-Host "`n11a. A release published today is seen today"
# Regression. The pad called FetchAll(DateTime.Today) and that value became the clock for the
# release cache. Fetching stamped the entry with MIDNIGHT, and freshness was then measured against
# midnight too -- so the cached answer was perpetually nought hours old and the six-hour cache
# behaved as "the first answer of the calendar day, frozen until the next one". A publisher could
# tag a release and nobody would see it that day.
#
# Tested through FetchAll rather than Resolve, because Resolve was never wrong: every earlier test
# here handed it DateTime.Now directly and it behaved perfectly. The defect lived in what the caller
# passed, which is exactly the seam a test that calls the inner method cannot see.
$clockStore = Join-Path $sandbox 'clock-store'
New-Item -ItemType Directory -Force -Path $clockStore | Out-Null

$rc = $asm.GetType('AddinFinder.RegistryClient').GetConstructor([Type[]]@([string])).Invoke(@([string]$clockStore))
$null = $rc.FetchAll([DateTime]::Today)     # what the pad used to pass, and now never does

$cacheFile = Join-Path $clockStore 'release-cache.json'
if (-not (Test-Path $cacheFile)) {
    Write-Host "  SKIP  no release resolved (rate limit or offline)" -ForegroundColor Yellow
} else {
    $stamped = ([regex]::Match((Get-Content $cacheFile -Raw), '"checkedOn":"([^"]+)"')).Groups[1].Value
    $drift   = [Math]::Abs(([DateTime]::Now - [DateTime]::Parse($stamped)).TotalMinutes)
    Write-Host "  stamped $stamped, real clock $([DateTime]::Now.ToString('yyyy-MM-dd HH:mm'))" -ForegroundColor DarkGray
    Check 'the cache is stamped with the real time, not midnight' ($drift -lt 5) `
        "stamped '$stamped', which is $([int]$drift) minutes from now"

    # And the freshness rule it feeds: six hours means six hours.
    $grType  = $asm.GetType('AddinFinder.GithubReleases')
    $isFresh = $grType.GetMethod('IsFresh',
        [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic)
    $probe = $asm.CreateInstance('AddinFinder.GithubRelease')
    $probe.Tag = 'v1'; $probe.AssetUrl = 'https://example/x'

    $probe.CheckedOn = [DateTime]::Now.AddHours(-1).ToString('yyyy-MM-dd HH:mm')
    Check 'an hour old is still fresh'  ([bool]$isFresh.Invoke($null, @($probe, [DateTime]::Now))) 'refetched too eagerly'
    $probe.CheckedOn = [DateTime]::Now.AddHours(-7).ToString('yyyy-MM-dd HH:mm')
    Check 'seven hours old is not'      (-not [bool]$isFresh.Invoke($null, @($probe, [DateTime]::Now))) 'served a stale answer'
    $probe.CheckedOn = [DateTime]::Today.ToString('yyyy-MM-dd HH:mm')
    Check 'and a midnight stamp is judged against the real clock' `
        (([DateTime]::Now - [DateTime]::Today).TotalHours -lt 6 -or
         -not [bool]$isFresh.Invoke($null, @($probe, [DateTime]::Now))) `
        'midnight was treated as always-fresh'
}

Write-Host "`n12. The user chooses where the installer is saved"
# Asked every time, starting from wherever one was last saved. What is being fetched is an
# executable the user has to find and run with elevation, so "it went somewhere, try Downloads" is
# not good enough -- but nor is asking a question they have already answered, hence remembering.
$resolve = $installer.GetMethod('ResolveDownloadFolder', $static)
$default = $installer.GetMethod('DefaultDownloadFolder', $static)

$chosen = Join-Path $sandbox 'my-installers'
New-Item -ItemType Directory -Force -Path $chosen | Out-Null
Check 'a folder the user chose is used'  ($resolve.Invoke($null, @([string]$chosen)) -eq $chosen) 'ignored the choice'
Check 'nothing remembered falls back to the default' `
    ($resolve.Invoke($null, @([string]'')) -eq $default.Invoke($null, @())) 'did not fall back'
Check 'a folder that has since gone falls back rather than failing' `
    ($resolve.Invoke($null, @([string](Join-Path $sandbox 'on-a-usb-stick-that-is-not-there'))) -eq $default.Invoke($null, @())) `
    'a removed folder was used anyway'
Check 'the default exists' (Test-Path $default.Invoke($null, @())) 'the default is not a real folder'

# The setting itself: global, because it is about this machine and not about a Clarion.
$settingsType = $asm.GetType('AddinFinder.AddinFinderSettings')
$load = $settingsType.GetMethod('Load', $static, $null, [Type[]]@([string], [string]), $null)
$settingsStore = Join-Path $sandbox 'settings-store'
New-Item -ItemType Directory -Force -Path $settingsStore | Out-Null

$s1 = $load.Invoke($null, @([string]'C:\Clarion11.1', [string]$settingsStore))
Check 'nothing remembered to begin with' ($s1.InstallerDownloadFolder -eq '') "got '$($s1.InstallerDownloadFolder)'"
$s1.InstallerDownloadFolder = $chosen
$s1.Save()

$s2 = $load.Invoke($null, @([string]'C:\Clarion11.1', [string]$settingsStore))
Check 'it survives a save and load' ($s2.InstallerDownloadFolder -eq $chosen) "got '$($s2.InstallerDownloadFolder)'"
$s3 = $load.Invoke($null, @([string]'C:\Clarion10', [string]$settingsStore))
Check 'and is not re-asked for a different Clarion' ($s3.InstallerDownloadFolder -eq $chosen) `
    'the choice was scoped per Clarion'

Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
else             { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
