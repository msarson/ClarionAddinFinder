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

$settings = $asm.CreateInstance('AddinFinder.AddinFinderSettings')

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

Write-Host "`n6. Round-trips through settings.json"
$parser = $asm.GetType('AddinFinder.SimpleJsonParser')
$ser  = $parser.GetMethod('SerialiseSettings', [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public)
$des  = $parser.GetMethod('ParseSettings',     [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public)
$settings.AcceptedTermsVersion = 1
$json = $ser.Invoke($null, @($settings))
$back = $des.Invoke($null, @([string]$json))
Check 'terms version survives'      ($back.AcceptedTermsVersion -eq 1) 'lost'
Check 'publishers survive'          ($back.HasAcknowledged('msarson') -and $back.HasAcknowledged('')) 'lost'
Check 'unrelated publisher still absent' (-not $back.HasAcknowledged('asantarelli')) 'invented'
Write-Host "  json: $json" -ForegroundColor DarkGray

Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
else             { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
