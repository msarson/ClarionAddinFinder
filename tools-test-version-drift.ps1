# Regression: a stale <Identity version> must not reintroduce a phantom "update available".
#
# FlattenCode ships 1.0.1, 1.0.2 and 1.0.3 all declaring <Identity name="FlattenCode.Addin"
# version="1.0"/>. Reconciliation used to take that as truth, overwrite the 1.0.3 we installed, and
# the comparison against the registry then reported an update forever -- installing it only to have
# the manifest reassert 1.0 on the next load.
#
# Run under 32-bit Windows PowerShell.

$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::LoadFrom('F:\github\ClarionAddinFinder\bin\Release\net48\AddinFinder.dll')

$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else     { Write-Host "  FAIL  $name -- $detail" -ForegroundColor Red; $script:fail++ }
}

$sandbox = Join-Path $env:TEMP ('af-drift-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$store   = Join-Path $sandbox 'store'
$clarion = Join-Path $sandbox 'Clarion11.1'
New-Item -ItemType Directory -Force -Path $store | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $clarion 'bin') | Out-Null

function Add-Addin([string]$id, [string]$identity, [string]$manifestVersion) {
    $dir = Join-Path $clarion "accessory\addins\$id"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -Path (Join-Path $dir "$id.addin") -Encoding UTF8 -Value @"
<AddIn name="$id"><Manifest><Identity name="$identity" version="$manifestVersion"/></Manifest></AddIn>
"@
}

$t = $asm.GetType('AddinFinder.InstalledAddinStore')
$store1 = $t.GetConstructor([Type[]]@([string])).Invoke(@([string]$store))

Write-Host "`n1. The FlattenCode case: installed 1.0.3, manifest still says 1.0"
Add-Addin 'FlattenCode' 'FlattenCode.Addin' '1.0'
$store1.MarkInstalled($clarion, 'FlattenCode', '1.0.3', $false, 'msarson')
$loaded = $store1.Load($clarion)
$fc = $loaded | Where-Object { $_.Id -eq 'FlattenCode' }
Check 'the version we installed is kept' ($fc.Version -eq '1.0.3') `
    "manifest lowered it to '$($fc.Version)' -- the pad would offer the update again"

Write-Host "`n2. ...and it stays kept across repeated loads"
$null = $store1.Load($clarion)
$fc2 = ($store1.Load($clarion)) | Where-Object { $_.Id -eq 'FlattenCode' }
Check 'still 1.0.3 after reloading twice' ($fc2.Version -eq '1.0.3') "drifted to '$($fc2.Version)'"

Write-Host "`n3. A manifest ahead of us IS believed (updated behind our back)"
Add-Addin 'GitPane' 'GitPane' '2.0.0'
$store1.MarkInstalled($clarion, 'GitPane', '1.0.9', $false, 'msarson')
$gp = ($store1.Load($clarion)) | Where-Object { $_.Id -eq 'GitPane' }
Check 'adopts the higher on-disk version' ($gp.Version -eq '2.0.0') "stayed at '$($gp.Version)'"

Write-Host "`n4. An adopted addin with no recorded version takes the manifest's"
Add-Addin 'ListFormatParser' 'ListFormatParser' '1.3.0'
$lfp = ($store1.Load($clarion)) | Where-Object { $_.Id -eq 'ListFormatParser' }
Check 'adopted from disk with its manifest version' ($lfp.Version -eq '1.3.0') "got '$($lfp.Version)'"

Write-Host "`n5. Identity is read from the manifest, not assumed from the id"
$installerType = $asm.GetType('AddinFinder.AddinInstaller')
$installer = $installerType.GetConstructor([Type[]]@([string], $t)).Invoke(@([string]$clarion, $store1))
Check 'FlattenCode declares FlattenCode.Addin, not FlattenCode' `
    ($null -ne $installer.FindConflictingIdentity('SomethingElse', 'FlattenCode.Addin')) `
    'the real identity was not found'
Check 'and the id alone does not match it' `
    ($null -eq $installer.FindConflictingIdentity('SomethingElse', 'FlattenCode')) `
    'matched a name no manifest declares'

Write-Host "`nVersions are compared as numbers, not as text"
# The pad decides Installed vs Update available with this. For an addin Addin Finder placed, the
# installed version is the very string it read from the registry, so text comparison never showed.
# A setup addin records nothing: its installed version is read from the publisher's manifest and the
# published one from the release tag -- written by different hands, on different days. Expecting
# those to agree on trailing zeroes was expecting too much.
$cmp = $asm.GetType('AddinFinder.InstalledAddinStore').GetMethod(
           'CompareDotted', [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public)

Check '1.0 and 1.0.0 are the same version'    ($cmp.Invoke($null, @([string]'1.0',   [string]'1.0.0'))   -eq 0) 'read as different'
Check '1.0.0 and 1.0 likewise, either way up' ($cmp.Invoke($null, @([string]'1.0.0', [string]'1.0'))     -eq 0) 'not symmetric'
Check '1.0.1 is newer than 1.0'               ($cmp.Invoke($null, @([string]'1.0.1', [string]'1.0'))     -gt 0) 'real difference lost'
Check '1.9 is older than 1.10, not newer'     ($cmp.Invoke($null, @([string]'1.9',   [string]'1.10'))    -lt 0) 'string comparison bug'
Check 'and a real difference still differs'   ($cmp.Invoke($null, @([string]'1.0.0', [string]'2.0.0'))   -ne 0) 'collapsed two versions'

Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
else             { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
