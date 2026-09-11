param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repo 'companion'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repo 'artifacts/release' }
$manifest = Get-Content -LiteralPath (Join-Path $source 'apps/lua/ADTCompanion/manifest.ini') -Raw
if ($manifest -notmatch '(?m)^\[WINDOW_\.\.\.\]\s*$' -or $manifest -match '(?m)^\[WINDOW_[A-Za-z]') {
    throw 'CSP app windows must use the numbered/window-list manifest format [WINDOW_...], not a named section such as [WINDOW_MAIN].'
}
if ($manifest -notmatch '(?m)^VERSION\s*=\s*([0-9A-Za-z.-]+)\s*$') { throw 'Missing companion version.' }
$version = $Matches[1]
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$zip = Join-Path $OutputDirectory "ADTCompanion-$version.zip"
Compress-Archive -Path (Join-Path $source 'apps'), (Join-Path $source 'README.md') -DestinationPath $zip -CompressionLevel Optimal -Force
Write-Output "Companion package: $zip"
Get-FileHash -LiteralPath $zip -Algorithm SHA256
