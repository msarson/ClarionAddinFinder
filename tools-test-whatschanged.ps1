# Who gets told about the federation change, and who does not.
# Exercises the version bookkeeping only -- the dialog itself is not shown.
# Run under 32-bit Windows PowerShell.

$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::LoadFrom('F:\github\ClarionAddinFinder\bin\Release\net48\AddinFinder.dll')

$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else     { Write-Host "  FAIL  $name -- $detail" -ForegroundColor Red; $script:fail++ }
}

$t = $asm.GetType('AddinFinder.WhatsChangedNotice')
$isBefore = $t.GetMethod('IsBefore', [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Public)
$current  = $t.GetMethod('CurrentVersion', [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Public)

Write-Host "`n1. Version comparison"
Check '0.7.1 is before 0.8.0'  ([bool]$isBefore.Invoke($null, @('0.7.1', '0.8.0'))) 'wrong'
Check '0.8.0 is not before itself' (-not [bool]$isBefore.Invoke($null, @('0.8.0', '0.8.0'))) 'wrong'
Check '0.9.0 is not before 0.8.0'  (-not [bool]$isBefore.Invoke($null, @('0.9.0', '0.8.0'))) 'wrong'
Check '0.9.0 is before 0.10.0 (not a string compare)' `
    ([bool]$isBefore.Invoke($null, @('0.9.0', '0.10.0'))) 'string comparison bug'
Check '0.6 equals 0.6.0'      (-not [bool]$isBefore.Invoke($null, @('0.6', '0.6.0'))) 'padding bug'
Check 'garbage does not throw' (-not [bool]$isBefore.Invoke($null, @('', ''))) 'threw or wrong'

Write-Host "`n2. Running assembly reports its version"
$v = $current.Invoke($null, @())
Check 'version readable and is 0.8.0' ($v -eq '0.8.0') "got '$v'"

Write-Host "`n3. Who should be told (the decision ShowIfUpgraded makes)"
# upgrading = LastSeenVersion set ? IsBefore(LastSeen, 0.8.0) : hasEarlierState
function ShouldTell([string]$lastSeen, [bool]$hasEarlierState) {
    if ($lastSeen.Length -gt 0) { return [bool]$isBefore.Invoke($null, @($lastSeen, '0.8.0')) }
    return $hasEarlierState
}

Check 'upgrading from 0.7.1 -> told'            (ShouldTell '0.7.1' $false) 'not told'
Check 'fresh install, no prior state -> NOT told' (-not (ShouldTell '' $false)) 'told a new user about a change they never saw'
Check 'old user, pre-dates the field -> told'     (ShouldTell '' $true) 'not told'
Check 'already on 0.8.0 -> NOT told'              (-not (ShouldTell '0.8.0' $true)) 'told twice'
Check 'arriving from a later build -> NOT told'   (-not (ShouldTell '0.9.0' $true)) 'told on downgrade'

Write-Host "`n4. LastSeenVersion round-trips, per Clarion"
$sandbox = Join-Path $env:TEMP ('af-wc-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$store   = Join-Path $sandbox 'store'
New-Item -ItemType Directory -Force -Path $store | Out-Null

$st   = $asm.GetType('AddinFinder.AddinFinderSettings')
$load = $st.GetMethod('Load', [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public,
                      $null, [Type[]]@([string],[string]), $null)

$s11 = $load.Invoke($null, @([string]'C:\Clarion11.1', [string]$store))
$s11.LastSeenVersion = '0.8.0'
$s11.Save()

$again = $load.Invoke($null, @([string]'C:\Clarion11.1', [string]$store))
Check 'survives a save and load' ($again.LastSeenVersion -eq '0.8.0') "got '$($again.LastSeenVersion)'"

$s12 = $load.Invoke($null, @([string]'C:\Clarion12', [string]$store))
Check 'the OTHER Clarion has not seen it' ($s12.LastSeenVersion -eq '') `
    "Clarion 12 inherited '$($s12.LastSeenVersion)' -- the notice would be skipped there"

$fresh = $load.Invoke($null, @([string]'C:\Nowhere', [string]$store))
Check 'an unknown root reads as empty, not null' ($fresh.LastSeenVersion -eq '') 'null or wrong'

Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
else             { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
