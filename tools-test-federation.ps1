# Exercises the federated-registry pieces: publisher parsing, the download-URL constraint,
# publisher health staging, and the Identity collision check.
#
# Run under 32-bit Windows PowerShell -- the assembly is PlatformTarget=x86:
#   C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -File tools-test-federation.ps1

$ErrorActionPreference = 'Stop'

$sandbox  = Join-Path $env:TEMP ('af-fed-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$storeDir = Join-Path $sandbox 'store'
$clarion  = Join-Path $sandbox 'Clarion11.1'
New-Item -ItemType Directory -Force -Path $storeDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $clarion 'bin') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $clarion 'accessory\addins') | Out-Null

$asm = [Reflection.Assembly]::LoadFrom('F:\github\ClarionAddinFinder\bin\Release\net48\AddinFinder.dll')

$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else     { Write-Host "  FAIL  $name -- $detail" -ForegroundColor Red; $script:fail++ }
}

# NOTE: the parameter must not be called $args -- that is an automatic variable inside a
# PowerShell function and silently shadows anything passed in.
function New-Instance([string]$type, [object[]]$ctorArgs, [Type[]]$ctorTypes) {
    $t = $asm.GetType($type)
    if (-not $t) { throw "type not found: $type" }
    $ctor = $t.GetConstructor($ctorTypes)
    if (-not $ctor) { throw "ctor not found on $type" }
    return $ctor.Invoke($ctorArgs)
}

function Add-FakeAddin([string]$root, [string]$folder, [string]$identity) {
    $dir = Join-Path $root "accessory\addins\$folder"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -Path (Join-Path $dir "$identity.addin") -Encoding UTF8 -Value @"
<AddIn name="$identity"><Manifest><Identity name="$identity" version="1.0.0"/></Manifest></AddIn>
"@
}

Write-Host "`n1. Publisher URL derivation (branch is recorded, not guessed)"
$pub = $asm.CreateInstance('AddinFinder.Publisher')
$pub.Id = 'msarson'; $pub.Repo = 'clarion-addins'; $pub.Branch = 'main'
Check 'addins.json URL uses the recorded branch' `
    ($pub.AddinsUrl -eq 'https://raw.githubusercontent.com/msarson/clarion-addins/main/addins.json') `
    $pub.AddinsUrl

$pubNoBranch = $asm.CreateInstance('AddinFinder.Publisher')
$pubNoBranch.Id = 'x'; $pubNoBranch.Repo = 'y'
Check 'missing branch falls back to main' ($pubNoBranch.AddinsUrl -like '*/y/main/addins.json') $pubNoBranch.AddinsUrl

Write-Host "`n2. Download URLs must belong to the publisher"
Check 'own account accepted'  ($pub.OwnsDownloadUrl('https://github.com/msarson/FlattenCode/releases/download/v1/x.dll')) 'rejected own URL'
Check 'other account refused' (-not $pub.OwnsDownloadUrl('https://github.com/someoneelse/evil/releases/download/v1/x.dll')) 'accepted foreign URL'
Check 'non-github refused'    (-not $pub.OwnsDownloadUrl('https://evil.example.com/x.dll')) 'accepted arbitrary host'
Check 'empty url tolerated'   ($pub.OwnsDownloadUrl('')) 'rejected empty'

# The same rule for an addin that installs itself, which records "owner/repo" and no URLs at all --
# so all three URL checks above pass on empty strings while the installer fetched could have come
# from anywhere. Strict: approving a publisher is not permission to point users elsewhere later.
Check 'own repository accepted'   ($pub.OwnsRepo('msarson/TestSetupAddin')) 'rejected own repo'
Check 'other account refused'     (-not $pub.OwnsRepo('someoneelse/evil')) 'accepted a foreign repository'
Check 'a bare name is refused'    (-not $pub.OwnsRepo('TestSetupAddin')) 'guessed an owner'
Check 'a leading slash is refused' (-not $pub.OwnsRepo('/msarson/x')) 'accepted an empty owner'
Check 'case is not significant'   ($pub.OwnsRepo('MSarson/x')) 'rejected on case alone'
Check 'empty repo tolerated'      ($pub.OwnsRepo('')) 'rejected empty'

Write-Host "`n3. A real publisher file parses (msarson/clarion-addins)"
$parser = $asm.GetType('AddinFinder.SimpleJsonParser')
$m = $parser.GetMethod('ParsePublisherAddins', [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public)
$json = (Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/msarson/clarion-addins/main/addins.json' -UseBasicParsing).Content
$addins = $m.Invoke($null, @($json, 'msarson'))
# Not an exact count: this reads the LIVE file, so pinning a number here means the suite fails the
# day msarson publishes anything. What matters is that the real file parses into entries of the
# right shape.
Check 'the real list parses into addins' ($addins.Count -ge 6) "got $($addins.Count)"
$setups  = @($addins | Where-Object { $_.IsSetup })
$ordinary = @($addins | Where-Object { -not $_.IsSetup })
Write-Host "  parsed: $($ordinary.Count) ordinary, $($setups.Count) setup" -ForegroundColor DarkGray
Check 'ordinary entries carry a version' `
    (@($ordinary | Where-Object { $_.Version.Length -eq 0 }).Count -eq 0) 'an entry has no version'
Check 'setup entries carry a repository and no version' `
    (@($setups | Where-Object { $_.GithubRepo.Length -eq 0 -or $_.Version.Length -gt 0 }).Count -eq 0) `
    'a setup entry is the wrong shape'
Check 'and every setup repository belongs to the publisher' `
    (@($setups | Where-Object { -not $pub.OwnsRepo($_.GithubRepo) }).Count -eq 0) 'a repository is not msarson-owned'
Check 'every entry stamped with its publisher' `
    (@($addins | Where-Object { $_.Publisher -ne 'msarson' }).Count -eq 0) 'publisher not stamped'
Check 'all download URLs pass the ownership check' `
    (@($addins | Where-Object { -not $pub.OwnsDownloadUrl($_.DownloadZipUrl) }).Count -eq 0) 'a URL failed'
Check 'status defaults to active' `
    (@($addins | Where-Object { $_.Status -ne 'active' }).Count -eq 0) 'unexpected status'

Write-Host "`n4. Publisher health -- a failed fetch must not read as a withdrawal"
$health = New-Instance 'AddinFinder.PublisherHealth' @([string]$storeDir) @([string])
$today  = [DateTime]::Parse('2026-08-22')

$unreachable = [Enum]::Parse($asm.GetType('AddinFinder.FetchOutcome'), 'Unreachable')
$notFound    = [Enum]::Parse($asm.GetType('AddinFinder.FetchOutcome'), 'NotFound')
$ok          = [Enum]::Parse($asm.GetType('AddinFinder.FetchOutcome'), 'Ok')

1..10 | ForEach-Object { $health.Record('flaky', $unreachable, $today) }
Check 'ten unreachable results never imply withdrawal' `
    (-not $health.IsPresumedWithdrawn('flaky', $today)) 'network failures were treated as withdrawal'

$health.Record('gone', $notFound, [DateTime]::Parse('2026-08-01'))
$health.Record('gone', $notFound, [DateTime]::Parse('2026-08-02'))
Check 'two 404s is not yet enough' (-not $health.IsPresumedWithdrawn('gone', $today)) 'concluded too early'

$health.Record('gone', $notFound, [DateTime]::Parse('2026-08-03'))
Check 'three 404s spread over weeks reads as withdrawn' `
    ($health.IsPresumedWithdrawn('gone', $today)) 'never concluded'

Check 'but not on the same day they started' `
    (-not $health.IsPresumedWithdrawn('gone', [DateTime]::Parse('2026-08-03'))) 'concluded without elapsed time'

$health.Record('gone', $ok, $today)
Check 'one success clears it' (-not $health.IsPresumedWithdrawn('gone', $today)) 'success did not reset'

Write-Host "`n5. Identity collision check before install"
$store     = New-Instance 'AddinFinder.InstalledAddinStore' @([string]$storeDir) @([string])
$installer = New-Instance 'AddinFinder.AddinInstaller' @([string]$clarion, $store) @([string], $asm.GetType('AddinFinder.InstalledAddinStore'))

# The exact shape that breaks Clarion: same Identity, two differently-named folders.
Add-FakeAddin $clarion 'MarkdownEditor' 'ClarionMarkdownEditor'

$clash = $installer.FindConflictingIdentity('ClarionMarkdownEditor', 'ClarionMarkdownEditor')
Check 'clash found in a differently-named folder' `
    ($null -ne $clash -and $clash.EndsWith('MarkdownEditor')) "got '$clash'"

Check 'an addin does not conflict with its own folder' `
    ($null -eq $installer.FindConflictingIdentity('MarkdownEditor', 'ClarionMarkdownEditor')) `
    'reported a clash against itself'

Check 'an unrelated Identity is not a clash' `
    ($null -eq $installer.FindConflictingIdentity('GitPane', 'GitPane')) 'false positive'

Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
else             { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
