# Exercises InstalledAddinStore against a fake two-Clarion machine.
# Must run under 32-bit Windows PowerShell: AddinFinder.dll is PlatformTarget=x86.

$ErrorActionPreference = 'Stop'

$sandbox = Join-Path $env:TEMP ('af-store-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$appdata = Join-Path $sandbox 'AppData'
$c111    = Join-Path $sandbox 'Clarion11.1'
$c12     = Join-Path $sandbox 'Clarion12'

# SpecialFolder.ApplicationData goes through the Win32 shell API and ignores %APPDATA%, so the store
# is pointed at this sandbox explicitly via its test constructor.
$storeDir = Join-Path $appdata 'ClarionAddinFinder'
New-Item -ItemType Directory -Force -Path $storeDir | Out-Null

function New-FakeClarion([string]$root) {
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'bin') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'accessory\addins') | Out-Null
}

function Add-FakeAddin([string]$root, [string]$id, [string]$version) {
    $dir = Join-Path $root "accessory\addins\$id"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $ver = if ($version) { " version=""$version""" } else { '' }
    Set-Content -Path (Join-Path $dir "$id.addin") -Encoding UTF8 -Value @"
<AddIn name="$id">
  <Manifest>
    <Identity name="$id"$ver/>
  </Manifest>
</AddIn>
"@
}

New-FakeClarion $c111
New-FakeClarion $c12

# 11.1 has the addin on disk at 1.3.0; 12 has nothing.
Add-FakeAddin $c111 'ClarionMarkdownEditor' '1.3.0'

# A v1 store: flat list, no root, and a stale version.
$storePath = Join-Path $storeDir 'installed.json'
Set-Content -Path $storePath -Encoding UTF8 -Value `
    '{"addins":[{"id":"ClarionMarkdownEditor","version":"1.0.2","installedAt":"2026-05-01"}]}'

# LoadFrom, not Add-Type: Add-Type resolves EVERY type in the assembly up front, which drags in the
# ICSharpCode references that only exist inside a real Clarion install. LoadFrom is lazy, so the
# store types -- which touch nothing from SharpDevelop -- load fine on their own.
$asm = [Reflection.Assembly]::LoadFrom('F:\github\ClarionAddinFinder\bin\Release\net48\AddinFinder.dll')
$storeType = $asm.GetType('AddinFinder.InstalledAddinStore')
if (-not $storeType) { throw 'InstalledAddinStore type not found in the assembly' }
# Explicit constructor lookup: passing the arg array to Activator lets PowerShell hand the
# binder a PSObject wrapper rather than a String, and the (String) ctor then looks absent.
$ctor = $storeType.GetConstructor([Type[]]@([string]))
if (-not $ctor) { throw 'InstalledAddinStore(string) constructor not found' }
$store = $ctor.Invoke([object[]]@([string]$storeDir))
if (-not $store) { throw 'Could not create InstalledAddinStore' }

$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else     { Write-Host "  FAIL  $name -- $detail" -ForegroundColor Red; $script:fail++ }
}

Write-Host "`n1. v1 migration + claim by the root that has it on disk"
$r = $store.Load($c111)
Check 'claimed for 11.1' ($r.Count -eq 1) "got $($r.Count) entries"
Check 'version re-read from the manifest, not the stale JSON' `
    ($r.Count -eq 1 -and $r[0].Version -eq '1.3.0') "version=$(if($r.Count){$r[0].Version})"
Check 'v1 backup written' (Test-Path (Join-Path $storeDir 'installed.json.v1.bak')) 'no .v1.bak'

Write-Host "`n2. the other Clarion must NOT inherit it (the #6 bug)"
$r12 = $store.Load($c12)
Check 'Clarion 12 reports nothing installed' ($r12.Count -eq 0) "got $($r12.Count) entries"

Write-Host "`n3. 11.1 still holds its entry after 12 loaded"
$r = $store.Load($c111)
Check '11.1 unaffected' ($r.Count -eq 1) "got $($r.Count) entries"

Write-Host "`n4. install into 12 is independent"
$store.MarkInstalled($c12, 'ClarionMarkdownEditor', '1.3.0', $false)
Add-FakeAddin $c12 'ClarionMarkdownEditor' '1.3.0'
Check 'both roots now report it' `
    (($store.Load($c111)).Count -eq 1 -and ($store.Load($c12)).Count -eq 1) 'roots disagree'

Write-Host "`n5. uninstall from 12 leaves 11.1 alone"
$store.MarkUninstalled($c12, 'ClarionMarkdownEditor')
Remove-Item (Join-Path $c12 'accessory\addins\ClarionMarkdownEditor') -Recurse -Force
Check '12 empty' (($store.Load($c12)).Count -eq 0) 'still present in 12'
Check '11.1 intact' (($store.Load($c111)).Count -eq 1) 'lost from 11.1'

Write-Host "`n6. folder deleted behind our back -> entry drops (disk is the truth)"
Remove-Item (Join-Path $c111 'accessory\addins\ClarionMarkdownEditor') -Recurse -Force
Check 'entry reconciled away' (($store.Load($c111)).Count -eq 0) 'stale entry survived'

Write-Host "`n7. a manifest with NO version attribute keeps the recorded version"
Add-FakeAddin $c111 'GitPane' $null
$store.MarkInstalled($c111, 'GitPane', '1.0.9', $false)
$r = $store.Load($c111)
Check 'version preserved, entry kept' `
    ($r.Count -eq 1 -and $r[0].Version -eq '1.0.9') "got $($r.Count) entries, version=$(if($r.Count){$r[0].Version})"

Write-Host "`n8. staged entry is exempt from reconciliation"
$store.MarkInstalled($c111, 'FlattenCode', '1.0.3', $true)   # no folder on disk
$r = $store.Load($c111)
Check 'staged entry survives with no folder' `
    (($r | Where-Object { $_.Id -eq 'FlattenCode' }).Count -eq 1) 'staged entry was dropped'

Write-Host "`nFinal installed.json:" -ForegroundColor DarkGray
Get-Content $storePath

Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
else             { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
