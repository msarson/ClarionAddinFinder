# Detecting two addins that declare the same Identity.
#
# Clarion refuses to start at all when this happens. It cannot be caused by installing through
# Addin Finder -- that is refused beforehand -- so this covers every other route: another product's
# installer, a folder copied from a different Clarion, a hand-unzipped copy.
#
# Run under 32-bit Windows PowerShell.

$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::LoadFrom('F:\github\ClarionAddinFinder\bin\Release\net48\AddinFinder.dll')

$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else     { Write-Host "  FAIL  $name -- $detail" -ForegroundColor Red; $script:fail++ }
}

$sandbox = Join-Path $env:TEMP ('af-audit-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$clarion = Join-Path $sandbox 'Clarion11.1'
New-Item -ItemType Directory -Force -Path (Join-Path $clarion 'accessory\addins') | Out-Null

function Add-Folder([string]$folder, [string]$identity) {
    $dir = Join-Path $clarion "accessory\addins\$folder"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    if ($identity) {
        Set-Content -Path (Join-Path $dir "$folder.addin") -Encoding UTF8 -Value @"
<AddIn name="$folder"><Manifest><Identity name="$identity" version="1.0"/></Manifest></AddIn>
"@
    }
}

$audit = $asm.GetType('AddinFinder.IdentityAudit')
$scan  = $audit.GetMethod('Scan', [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public)
# The unary comma stops PowerShell unwrapping the returned List<IdentityClash> into an Object[],
# which the ShortWarning/FullWarning overloads would then refuse.
function Scan-Now { return ,$scan.Invoke($null, @([string]$clarion)) }

Write-Host "`n1. A healthy machine reports nothing"
Add-Folder 'GitPane'     'GitPane'
Add-Folder 'FlattenCode' 'FlattenCode.Addin'
Check 'no clashes' ((Scan-Now).Count -eq 0) "reported $((Scan-Now).Count)"

Write-Host "`n2. The real-world case: same addin, two folder names"
# Exactly what breaks a machine: Clarion Assistant's installer writes MarkdownEditor,
# Addin Finder writes ClarionMarkdownEditor, both declaring ClarionMarkdownEditor.
Add-Folder 'MarkdownEditor'       'ClarionMarkdownEditor'
Add-Folder 'ClarionMarkdownEditor' 'ClarionMarkdownEditor'
$clashes = Scan-Now
Check 'one clash reported' ($clashes.Count -eq 1) "got $($clashes.Count)"
Check 'names the identity' ($clashes.Count -eq 1 -and $clashes[0].IdentityName -eq 'ClarionMarkdownEditor') 'wrong identity'
Check 'names BOTH folders' ($clashes.Count -eq 1 -and $clashes[0].Folders.Count -eq 2) `
    "listed $($clashes[0].Folders.Count) folder(s) -- the user cannot choose without both"

Write-Host "`n3. The healthy addins are not dragged in"
Check 'still only one clash' ((Scan-Now).Count -eq 1) 'false positives'

Write-Host "`n4. Folder name is irrelevant -- only the manifest counts"
Add-Folder 'SomethingElse' 'FlattenCode.Addin'   # id differs, Identity collides
$c2 = Scan-Now
Check 'clash found via the manifest, not the folder name' `
    (@($c2 | Where-Object { $_.IdentityName -eq 'FlattenCode.Addin' }).Count -eq 1) `
    'missed a clash between differently-named folders'

Write-Host "`n5. Folders with nothing to say are ignored"
Add-Folder 'EmptyFolder' $null            # no manifest at all
$before = (Scan-Now).Count
Add-Folder 'AnotherEmpty' $null
Check 'a manifest-less folder is not a clash' ((Scan-Now).Count -eq $before) 'empty folders reported'

Write-Host "`n6. The warnings are usable"
$short = $audit.GetMethod('ShortWarning', [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public)
$full  = $audit.GetMethod('FullWarning',  [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public)
$all   = Scan-Now
# The unary comma is load-bearing: @($all) would splat the list into separate arguments and
# Invoke would report a parameter count mismatch.
$shortText = $short.Invoke($null, @(,$all))
$fullText  = $full.Invoke($null, @(,$all))
Check 'short warning says Clarion will not start' ($shortText -match 'will not start') "got: $shortText"
Check 'full warning gives whole paths' ($fullText -match [regex]::Escape($clarion)) 'paths missing - user cannot find the folders'
Check 'full warning names both sides' `
    (([regex]::Matches($fullText, [regex]::Escape('accessory\addins'))).Count -ge 4) 'not enough paths listed'
Write-Host "  short: $shortText" -ForegroundColor DarkGray

Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
else             { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
