# Consent bookkeeping: which publishers a user has knowingly installed from.
# The dialog itself is not exercised here -- only the decision about when it is due.
# Run under 32-bit Windows PowerShell.

$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::LoadFrom('F:\github\ClarionAddinFinder\bin\Release\net48\AddinFinder.dll')

$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else     { Write-Host "  FAIL  $name -- $detail" -ForegroundColor Red; $script:fail++ }
}

# Settings are per Clarion root, so every check below is against a specific root.
$sandbox = Join-Path $env:TEMP ('af-consent-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$c11 = Join-Path $sandbox 'Clarion11.1'
$c12 = Join-Path $sandbox 'Clarion12'
$store = Join-Path $sandbox 'store'
New-Item -ItemType Directory -Force -Path $store | Out-Null

$t = $asm.GetType('AddinFinder.AddinFinderSettings')
# The two-argument overload keeps the test out of the developer's real %APPDATA%.
$load = $t.GetMethod('Load', [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public,
                     $null, [Type[]]@([string],[string]), $null)
function Settings-For([string]$root) { return $load.Invoke($null, @([string]$root, [string]$store)) }

$settings = Settings-For $c11

Write-Host "`n1. Nothing acknowledged to begin with"
Check 'general terms not yet accepted' (-not $settings.HasAcceptedTerms) 'accepted out of nowhere'
Check 'msarson not acknowledged'       (-not $settings.HasAcknowledged('msarson')) 'already acknowledged'
Check 'unknown publisher not acknowledged' (-not $settings.HasAcknowledged('')) 'already acknowledged'

Write-Host "`n2. Acknowledging one publisher does not acknowledge another"
$settings.Acknowledge('msarson')
Check 'msarson acknowledged'      ($settings.HasAcknowledged('msarson')) 'not recorded'
Check 'asantarelli still is NOT'  (-not $settings.HasAcknowledged('asantarelli')) 'leaked across publishers'
Check 'unknown publisher still is NOT' (-not $settings.HasAcknowledged('')) 'leaked to unknown'

Write-Host "`n3. The unidentified source is tracked as its own case"
$settings.Acknowledge('')
Check 'empty publisher can be acknowledged' ($settings.HasAcknowledged('')) 'not recorded'
Check 'and is distinct from a named one'    ($settings.HasAcknowledged('msarson')) 'clobbered'

Write-Host "`n4. Acknowledging is idempotent"
$before = $settings.AcknowledgedPublishers.Count
$settings.Acknowledge('msarson')
$settings.Acknowledge('msarson')
Check 'no duplicate entries' ($settings.AcknowledgedPublishers.Count -eq $before) `
    "count went $before -> $($settings.AcknowledgedPublishers.Count)"

Write-Host "`n5. Case-insensitive, as GitHub account names are"
Check 'MSARSON matches msarson' ($settings.HasAcknowledged('MSARSON')) 'case-sensitive comparison'

Write-Host "`n6. PER-CLARION: accepting in one must not accept in another"
$settings.AcceptedTermsVersion = 1
$settings.LastSeenVersion = '0.8.0'
$settings.Save()

$other = Settings-For $c12
Check 'Clarion 12 has NOT accepted the terms'      (-not $other.HasAcceptedTerms) 'terms leaked between Clarions'
Check 'Clarion 12 has NOT acknowledged msarson'    (-not $other.HasAcknowledged('msarson')) 'consent leaked between Clarions'
Check 'Clarion 12 has not seen this version'       ($other.LastSeenVersion -eq '') 'notice state leaked between Clarions'

Write-Host "`n7. ...and reloading the first Clarion still has its own"
$again = Settings-For $c11
Check 'Clarion 11.1 kept its terms'    ($again.HasAcceptedTerms) 'lost on reload'
Check 'Clarion 11.1 kept its publisher' ($again.HasAcknowledged('msarson')) 'lost on reload'
Check 'Clarion 11.1 kept its version'   ($again.LastSeenVersion -eq '0.8.0') 'lost on reload'

Write-Host "`n8. Saving one Clarion does not forget the other"
$other.AcceptedTermsVersion = 1
$other.Acknowledge('asantarelli')
$other.Save()
$again = Settings-For $c11
Check 'Clarion 11.1 survives Clarion 12 saving' ($again.HasAcknowledged('msarson')) 'clobbered by the other root'
Check 'and did not inherit its publisher'       (-not $again.HasAcknowledged('asantarelli')) 'leaked backwards'

Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
else             { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
