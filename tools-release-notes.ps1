# Prints one version's section from CHANGELOG.md, for use as a GitHub release body.
#
# v0.9.0 shipped with the whole file pasted in: fifteen version sections, so the release card on
# /releases truncated mid-0.8.0 and pushed Assets off the first screen. Release notes are for the
# version being released; the history already lives in CHANGELOG.md and in the older releases.
#
#   .\tools-release-notes.ps1 | Set-Content -Encoding UTF8 notes.md
#   gh release create v0.9.1 --notes-file notes.md bin\Release\AddinFinder-v0.9.1.zip
#
# Defaults to the topmost section, which is the one being released. Pass -Version to pick another.

param(
    [string]$Version,
    [string]$Path = (Join-Path $PSScriptRoot 'CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'

$lines = Get-Content -Path $Path

# Section headings look like "## [0.9.0] - 2026-08-23". Anything less specific would also match the
# "### Added" subheadings inside a section.
$headings = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s+\[(?<ver>[^\]]+)\]') {
        $headings += [pscustomobject]@{ Index = $i; Version = $Matches.ver }
    }
}

if ($headings.Count -eq 0) {
    throw "No '## [version]' sections found in $Path."
}

$start = if ($Version) {
    $wanted = $Version.TrimStart('v')
    $match = $headings | Where-Object { $_.Version -eq $wanted } | Select-Object -First 1
    if (-not $match) {
        throw "No section for $wanted in $Path. Found: $(($headings.Version) -join ', ')"
    }
    $match
} else {
    $headings[0]
}

# Runs to the next section heading, or to the end of the file for the oldest one.
$next = $headings | Where-Object { $_.Index -gt $start.Index } | Select-Object -First 1
$end  = if ($next) { $next.Index - 1 } else { $lines.Count - 1 }

# The heading itself is dropped: GitHub already titles the release with the tag, and repeating
# "## [0.9.0]" as the first line of the body just says it twice.
$body = $lines[($start.Index + 1)..$end]

# Trim the blank lines that sit either side of a section boundary.
while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[0]))                { $body = $body[1..($body.Count - 1)] }
while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[$body.Count - 1])) { $body = $body[0..($body.Count - 2)] }

$body
