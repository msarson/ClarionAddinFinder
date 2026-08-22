# An addin Addin Finder never published must not be listed as one of ours.
#
# Since 0.7.1 the store adopts every addin folder found on disk -- correct for collision checks and
# for not overwriting other people's work, but those are not our addins. Clarion Assistant, a
# hand-unzipped copy, anything another installer placed: listing them claims a relationship that
# never existed and offers actions we have no business offering.
#
# Run under 32-bit Windows PowerShell.

$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::LoadFrom('F:\github\ClarionAddinFinder\bin\Release\net48\AddinFinder.dll')

$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else     { Write-Host "  FAIL  $name -- $detail" -ForegroundColor Red; $script:fail++ }
}

$sandbox = Join-Path $env:TEMP ('af-foreign-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$store   = Join-Path $sandbox 'store'
New-Item -ItemType Directory -Force -Path $store | Out-Null

$clientType = $asm.GetType('AddinFinder.RegistryClient')
$client = $clientType.GetConstructor([Type[]]@([string])).Invoke(@([string]$store))

# An empty result: nothing is listed by any publisher this refresh.
$result = $asm.CreateInstance('AddinFinder.RegistryResult')

function New-Installed([string]$id, [string]$publisher) {
    $i = $asm.CreateInstance('AddinFinder.InstalledAddin')
    $i.Id = $id; $i.Publisher = $publisher; $i.Version = '1.0.0'; $i.Root = 'C:\Clarion11.1'
    return $i
}

$listType = [System.Collections.Generic.List[object]]
$installedType = $asm.GetType('AddinFinder.InstalledAddin')
$listOfInstalled = [System.Collections.Generic.List`1].MakeGenericType($installedType)

Write-Host "`n1. A foreign addin adopted from disk is NOT reported"
$list = [Activator]::CreateInstance($listOfInstalled)
$list.Add((New-Installed 'ClarionAssistant' ''))          # another product entirely
$list.Add((New-Installed 'SomeHandUnzippedThing' ''))     # dropped in by hand
$gone = $client.DescribeWithdrawn($result, $list)
Check 'neither is listed as ours' ($gone.Count -eq 0) "reported $($gone.Count): $(($gone | ForEach-Object { $_.Id }) -join ', ')"

Write-Host "`n2. Something WE installed is still accounted for"
$list2 = [Activator]::CreateInstance($listOfInstalled)
$list2.Add((New-Installed 'GitPane' 'msarson'))           # publisher recorded = we installed it
$gone2 = $client.DescribeWithdrawn($result, $list2)
Check 'ours is reported as no longer published' ($gone2.Count -eq 1) "got $($gone2.Count)"
Check 'and carries its publisher' `
    ($gone2.Count -eq 1 -and $gone2[0].Publisher -eq 'msarson') 'publisher lost'
Check 'and is flagged as withdrawn' `
    ($gone2.Count -eq 1 -and $gone2[0].NoLongerPublished) 'flag not set'

Write-Host "`n3. A mixed folder reports only ours"
$list3 = [Activator]::CreateInstance($listOfInstalled)
$list3.Add((New-Installed 'ClarionAssistant' ''))
$list3.Add((New-Installed 'GitPane' 'msarson'))
$list3.Add((New-Installed 'ClarionMarkdownEditor' ''))    # adopted, not installed by us
$gone3 = $client.DescribeWithdrawn($result, $list3)
Check 'exactly one reported' ($gone3.Count -eq 1) "got $($gone3.Count): $(($gone3 | ForEach-Object { $_.Id }) -join ', ')"
Check 'and it is GitPane' ($gone3.Count -eq 1 -and $gone3[0].Id -eq 'GitPane') 'wrong addin'

Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
else             { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
