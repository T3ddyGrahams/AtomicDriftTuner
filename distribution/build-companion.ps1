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
$files = @('README.md', 'apps/lua/ADTCompanion/ADTCompanion.lua', 'apps/lua/ADTCompanion/companion_client.lua', 'apps/lua/ADTCompanion/setup_capture.lua',
    'apps/lua/ADTCompanion/manifest.ini', 'apps/lua/ADTCompanion/icon.png')
foreach ($relative in $files) {
    $item = Get-Item -LiteralPath (Join-Path $source $relative)
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Companion payload must be an ordinary file: $relative"
    }
}
$stream = [IO.File]::Open($zip, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($relative in $files) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $source $relative), $relative,
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
Write-Output "Companion package: $zip"
Get-FileHash -LiteralPath $zip -Algorithm SHA256
