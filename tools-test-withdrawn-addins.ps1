# An addin that was listed, and is not any more.
#
# The registry cache exists partly so that someone whose addin has just been withdrawn can still be
# told what it was and who wrote it -- a bare folder name is a poor answer to "what am I running?".
# That did not work: a refresh replaces a publisher's list wholesale, and it does so BEFORE anything
# asks what became of an installed addin, so the entry being asked about had already been overwritten.
#
# Entries that drop out of a list are now kept aside instead, under their own key.
#
# The line this draws: we report what we know about the LISTING, never about our relationship with
# the addin. "A publisher we follow used to list this and no longer does" is true whether or not we
# installed it. "Nobody we follow has ever listed this" is silence -- see tools-test-foreign-addins.
#
# Run under 32-bit Windows PowerShell.

$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::LoadFrom('F:\github\ClarionAddinFinder\bin\Release\net48\AddinFinder.dll')

$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else     { Write-Host "  FAIL  $name -- $detail" -ForegroundColor Red; $script:fail++ }
}

$sandbox = Join-Path $env:TEMP ('af-withdrawn-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$store   = Join-Path $sandbox 'store'
New-Item -ItemType Directory -Force -Path $store | Out-Null

$addinType       = $asm.GetType('AddinFinder.RegistryAddin')
$listOfAddin     = [System.Collections.Generic.List`1].MakeGenericType($addinType)
$installedType   = $asm.GetType('AddinFinder.InstalledAddin')
$listOfInstalled = [System.Collections.Generic.List`1].MakeGenericType($installedType)

$cacheType  = $asm.GetType('AddinFinder.RegistryCache')
$clientType = $asm.GetType('AddinFinder.RegistryClient')

function New-Addin([string]$id, [string]$name, [string]$repo) {
    $a = $asm.CreateInstance('AddinFinder.RegistryAddin')
    $a.Id = $id; $a.Name = $name
    $a.Description = "what $id does"
    $a.Author      = 'Mark Sarson'
    $a.HomepageUrl = "https://github.com/msarson/$id"
    $a.Publisher   = 'msarson'
    if ($repo) { $a.GithubRepo = $repo }
    return $a
}

function New-Installed([string]$id, [string]$publisher) {
    $i = $asm.CreateInstance('AddinFinder.InstalledAddin')
    $i.Id = $id; $i.Publisher = $publisher; $i.Version = '1.0.0'; $i.Root = 'C:\Clarion11.1'
    return $i
}

function New-List($type, $items) {
    $l = [Activator]::CreateInstance($type)
    foreach ($i in $items) { $l.Add($i) }
    return ,$l    # comma, or PowerShell unrolls the list -- and an empty one becomes $null
}

$cachePath = Join-Path $store 'registry-cache.json'

Write-Host "`n1. A publisher lists two addins, then drops both"
$cache = $cacheType.GetConstructor([Type[]]@([string])).Invoke(@([string]$store))
$cache.Put('msarson', (New-List $listOfAddin @(
    (New-Addin 'GitPane' 'Git Pane' $null),
    (New-Addin 'SetupOnly' 'Setup Only' 'msarson/SetupOnly'))))
Check 'both cached' ($cache.Get('msarson').Count -eq 2) "got $($cache.Get('msarson').Count)"

$cache.Put('msarson', (New-List $listOfAddin @()))
Check 'the current list is now empty' ($cache.Get('msarson').Count -eq 0) 'entries lingered in the live list'

# The live list is what a cache fallback serves. A withdrawn addin filed there would be offered for
# installation all over again, which is the whole reason it goes under its own key.
$raw = Get-Content $cachePath -Raw
Check 'they were kept aside, not discarded' ($raw -match '"retired":\[\{') 'nothing retired'
Check 'and not left where a fallback would serve them' `
    ($raw -notmatch '"addins":\[\{') 'a dropped entry is still in the publisher list'

Write-Host "`n2. What the user is told about an addin that has gone"
# A fresh client, so everything below comes back off disk rather than out of the instance above.
$client = $clientType.GetConstructor([Type[]]@([string])).Invoke(@([string]$store))
$result = $asm.CreateInstance('AddinFinder.RegistryResult')

$installed = New-List $listOfInstalled @(
    (New-Installed 'GitPane' 'msarson'),          # installed through us
    (New-Installed 'SetupOnly' ''),               # its own installer put it there; we record nothing
    (New-Installed 'SomeHandUnzippedThing' ''))   # nobody we follow ever listed this

$gone = $client.DescribeWithdrawn($result, $installed)
$byId = @{}; foreach ($g in $gone) { $byId[$g.Id] = $g }

Check 'the two that were listed are reported' ($gone.Count -eq 2) `
    "got $($gone.Count): $(($gone | ForEach-Object { $_.Id }) -join ', ')"
Check 'and the one nobody ever listed is not' (-not $byId.ContainsKey('SomeHandUnzippedThing')) `
    'claimed an addin we know nothing about'

$git = $byId['GitPane']
Check 'it is named, not just identified' ($git.Name -eq 'Git Pane') "got '$($git.Name)'"
Check 'described'  ($git.Description -eq 'what GitPane does') "got '$($git.Description)'"
Check 'attributed' ($git.Author -eq 'Mark Sarson') "got '$($git.Author)'"
Check 'and still points somewhere the user can read about it' `
    ($git.HomepageUrl -eq 'https://github.com/msarson/GitPane') "got '$($git.HomepageUrl)'"
Check 'publisher survives' ($git.Publisher -eq 'msarson') "got '$($git.Publisher)'"
Check 'flagged as no longer published' ($git.NoLongerPublished) 'flag not set'

Write-Host "`n3. A withdrawn setup addin is still a setup addin"
# It matters: Remove is suppressed for these, because the files belong to the addin's own
# uninstaller. Losing the repo through the cache would have put Remove back in front of the user at
# exactly the moment the addin looks abandoned.
$setup = $byId['SetupOnly']
Check 'reported'                      ($null -ne $setup) 'not reported'
Check 'repository survived the cache' ($setup.GithubRepo -eq 'msarson/SetupOnly') "got '$($setup.GithubRepo)'"
Check 'so it still reads as self-installing' ($setup.IsSetup) 'came back as one we could place'
Check 'and is not offered on the All tab'    (-not $setup.IsOffered) 'still being offered'

Write-Host "`n4. A publisher we could not reach is not a withdrawal"
$outage  = $asm.CreateInstance('AddinFinder.RegistryResult')
$outcome = [Enum]::Parse($asm.GetType('AddinFinder.FetchOutcome'), 'Unreachable')
$outage.Outcomes.Add('msarson', $outcome)
$quiet = $client.DescribeWithdrawn($outage, (New-List $listOfInstalled @((New-Installed 'GitPane' 'msarson'))))
Check 'nothing is said while the publisher is merely unreachable' ($quiet.Count -eq 0) `
    "reported $($quiet.Count) during an outage"

Write-Host "`n5. An addin that comes back stops being described as gone"
$cache2 = $cacheType.GetConstructor([Type[]]@([string])).Invoke(@([string]$store))
$cache2.Put('msarson', (New-List $listOfAddin @((New-Addin 'GitPane' 'Git Pane' $null))))
$raw2 = Get-Content $cachePath -Raw
$retiredBlock = if ($raw2 -match '"retired":(\[.*\])\}?\s*$') { $Matches[1] } else { '' }
Check 'it left the shelf' ($retiredBlock -notmatch '"id":"GitPane"') 'still retired while being served'
Check 'the other one stayed on it' ($retiredBlock -match '"id":"SetupOnly"') 'lost an entry that is still gone'

$client2 = $clientType.GetConstructor([Type[]]@([string])).Invoke(@([string]$store))
$listing = $asm.CreateInstance('AddinFinder.RegistryResult')
$listing.Addins.Add((New-Addin 'GitPane' 'Git Pane' $null))
$back = $client2.DescribeWithdrawn($listing, (New-List $listOfInstalled @((New-Installed 'GitPane' 'msarson'))))
Check 'and it is not reported as withdrawn' ($back.Count -eq 0) "reported $($back.Count)"

Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
else             { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
