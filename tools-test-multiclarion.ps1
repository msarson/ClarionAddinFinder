# Repro: two Clarions BOTH have the addin on disk, but they run 0.7.0 at different times.
# Mirrors the reported machine: Clarion10 started first and claimed the single v1 entry.

$ErrorActionPreference = 'Stop'

$sandbox = Join-Path $env:TEMP ('af-repro-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$storeDir = Join-Path $sandbox 'store'
$c10 = Join-Path $sandbox 'Clarion10'
$c12 = Join-Path $sandbox 'Clarion12'
New-Item -ItemType Directory -Force -Path $storeDir | Out-Null

function Add-FakeAddin([string]$root, [string]$id, [string]$version) {
    $dir = Join-Path $root "accessory\addins\$id"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'bin') | Out-Null
    Set-Content -Path (Join-Path $dir "$id.addin") -Encoding UTF8 -Value @"
<AddIn name="$id"><Manifest><Identity name="$id" version="$version"/></Manifest></AddIn>
"@
}

# The addin is installed in BOTH Clarions -- which is exactly what a v1 store could not express.
Add-FakeAddin $c10 'ClarionMarkdownEditor' '1.3.0'
Add-FakeAddin $c12 'ClarionMarkdownEditor' '1.3.0'

$storePath = Join-Path $storeDir 'installed.json'
$v2Path = Join-Path $storeDir 'installed.v2.json'
Set-Content -Path $storePath -Encoding UTF8 -Value `
    '{"addins":[{"id":"ClarionMarkdownEditor","version":"1.3.0","installedAt":"2026-08-20"}]}'

$asm = [Reflection.Assembly]::LoadFrom('F:\github\ClarionAddinFinder\bin\Release\net48\AddinFinder.dll')
$t = $asm.GetType('AddinFinder.InstalledAddinStore')
$store = $t.GetConstructor([Type[]]@([string])).Invoke([object[]]@([string]$storeDir))

Write-Host "`nClarion 10 starts first (updated first):"
$r10 = $store.Load($c10)
Write-Host "  entries: $($r10.Count)  -> $(($r10 | ForEach-Object { $_.Id }) -join ', ')"
Write-Host "  installed.json (untouched for old builds): $((Get-Content $storePath -Raw).Trim())"
Write-Host "  installed.v2.json: $((Get-Content $v2Path -Raw).Trim())"

Write-Host "`nClarion 12 starts later -- addin IS on disk there:"
$r12 = $store.Load($c12)
Write-Host "  entries: $($r12.Count)"

$onDisk = Test-Path (Join-Path $c12 'accessory\addins\ClarionMarkdownEditor\ClarionMarkdownEditor.addin')
Write-Host "  manifest present on disk in Clarion 12: $onDisk"

Write-Host ''
if ($r12.Count -eq 0 -and $onDisk) {
    Write-Host 'BUG CONFIRMED: Clarion 12 reports the addin as NOT installed while it sits on disk there.' -ForegroundColor Red
    Write-Host 'The pad would offer Install and overwrite a working copy.' -ForegroundColor Red
    $code = 1
} else {
    Write-Host 'Clarion 12 correctly reports the addin.' -ForegroundColor Green
    $code = 0
}

Write-Host "`nfinal installed.json:    $((Get-Content $storePath -Raw).Trim())"
Write-Host "final installed.v2.json: $((Get-Content $v2Path -Raw).Trim())"
Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
exit $code
