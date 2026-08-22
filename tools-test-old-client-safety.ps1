# A setup addin must be INVISIBLE to builds before 0.8.1.
#
# Such an entry carries no download URLs, because the release asset is renamed every version and
# there is nothing to pin. An older client walks straight through that: the URL-ownership check
# passes (nothing to check), Download returns immediately on an empty URL, and MoveIntoPlace still
# creates the destination before copying nothing into it. The user is left with an EMPTY folder
# under accessory\addins and a phantom install -- and an empty folder in the scanned root is the
# shape that has already stopped a Clarion starting.
#
# So setup entries live under their own "setupAddins" key. Older builds read only "addins".
#
# Run under 32-bit Windows PowerShell.

$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::LoadFrom('F:\github\ClarionAddinFinder\bin\Release\net48\AddinFinder.dll')

$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else     { Write-Host "  FAIL  $name -- $detail" -ForegroundColor Red; $script:fail++ }
}

$parser = $asm.GetType('AddinFinder.SimpleJsonParser')
$static = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::Public
$parsePub = $parser.GetMethod('ParsePublisherAddins', $static)

# A publisher list carrying both kinds.
$json = @'
{
  "version": 1,
  "publisher": "ClarionLive",
  "addins": [
    { "id": "NormalAddin", "name": "Normal Addin", "version": "1.0.0",
      "downloadZipUrl": "https://github.com/ClarionLive/NormalAddin/releases/download/v1.0.0/x.zip" }
  ],
  "setupAddins": [
    { "id": "ClarionAssistant", "name": "Clarion Assistant",
      "githubRepo": "ClarionLive/ClarionAssistant" }
  ]
}
'@

Write-Host "`n1. What an OLD client sees (it reads only the addins key)"
# Exactly what a pre-0.8.1 build does: deserialise, look at "addins", ignore everything else.
Add-Type -AssemblyName System.Web.Extensions
$js  = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$raw = $js.Deserialize($json, [System.Collections.Generic.Dictionary[string,object]])
Check 'the setup addin is not in the addins key' ($raw['addins'].Count -eq 1) "saw $($raw['addins'].Count) entries"
Check 'and the one there is the normal addin'   ($raw['addins'][0]['id'] -eq 'NormalAddin') 'wrong addin visible'
Check 'so an old client cannot list it at all'  ($null -eq ($raw['addins'] | Where-Object { $_['id'] -eq 'ClarionAssistant' })) `
    'a setup addin is visible to a build that would create an empty folder from it'

Write-Host "`n2. What a NEW client sees"
$addins = $parsePub.Invoke($null, @([string]$json, [string]'ClarionLive'))
Check 'both entries parsed' ($addins.Count -eq 2) "got $($addins.Count)"
$setup  = $addins | Where-Object { $_.Id -eq 'ClarionAssistant' }
$normal = $addins | Where-Object { $_.Id -eq 'NormalAddin' }
Check 'the setup addin is flagged as such' ($setup.IsSetup) 'not flagged'
Check 'the normal addin is not'            (-not $normal.IsSetup) 'wrongly flagged'
Check 'both carry the publisher'           ($setup.Publisher -eq 'ClarionLive' -and $normal.Publisher -eq 'ClarionLive') 'publisher lost'

Write-Host "`n3. A setup entry with no repository is dropped, not shown broken"
$bad = '{ "setupAddins": [ { "id": "Whatever", "name": "Whatever" } ] }'
$none = $parsePub.Invoke($null, @([string]$bad, [string]'x'))
Check 'nothing to resolve a release from -> not listed' ($none.Count -eq 0) "got $($none.Count)"

Write-Host "`n4. Installing an entry with nothing to download is refused"
# Belt and braces: if a publisher puts a setup entry in the wrong key, a CURRENT client must still
# not manufacture an empty folder from it.
$sandbox = Join-Path $env:TEMP ('af-old-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$clarion = Join-Path $sandbox 'Clarion11.1'
New-Item -ItemType Directory -Force -Path (Join-Path $clarion 'accessory\addins') | Out-Null

$storeType = $asm.GetType('AddinFinder.InstalledAddinStore')
$st   = $storeType.GetConstructor([Type[]]@([string])).Invoke(@([string](Join-Path $sandbox 'store')))
$inst = $asm.GetType('AddinFinder.AddinInstaller').GetConstructor([Type[]]@([string], $storeType)).Invoke(@([string]$clarion, $st))

$empty = $asm.CreateInstance('AddinFinder.RegistryAddin')
$empty.Id = 'NothingToGet'; $empty.Name = 'Nothing To Get'
$refused = $false
try { $staged = $false; $inst.Install($empty, [ref]$staged) } catch { $refused = $true }
Check 'refused' $refused 'it proceeded with nothing to download'
Check 'and left no empty folder behind' `
    (-not (Test-Path (Join-Path $clarion 'accessory\addins\NothingToGet'))) `
    'an empty folder was created in the folder Clarion scans'

Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
else             { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
