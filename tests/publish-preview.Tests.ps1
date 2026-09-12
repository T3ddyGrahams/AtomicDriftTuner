# Offline publishing tests. All credentials and network responses are dummy data.
$ErrorActionPreference = 'Stop'
$adtTestRoot = Join-Path ([IO.Path]::GetTempPath()) ('adt-publish-tests-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path (Join-Path $adtTestRoot 'scripts'), (Join-Path $adtTestRoot 'artifacts/release') -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot '../scripts/publish-preview.ps1') (Join-Path $adtTestRoot 'scripts/publish-preview.ps1')
$adtTestScript = Join-Path $adtTestRoot 'scripts/publish-preview.ps1'
$env:ADT_RELEASE_VERSION = '0.9.0-preview.99'
$env:ADT_LINK_HOURS = '24'
$env:AWS_ACCESS_KEY_ID = 'test-access'
$env:AWS_SECRET_ACCESS_KEY = 'test-secret'
$env:R2_ACCOUNT_ID = '00000000000000000000000000000000'
$env:R2_BUCKET_NAME = 'test-preview-bucket'
$env:PREVIEW_PUBLISH_TOKEN = 'test-publisher'
$env:GITHUB_STEP_SUMMARY = Join-Path $adtTestRoot 'summary.md'
foreach ($suffix in @('setup.exe','portable.zip')) {
    [IO.File]::WriteAllText((Join-Path $adtTestRoot "artifacts/release/AtomicDriftTuner-$env:ADT_RELEASE_VERSION-$suffix"), 'dummy-package')
}
$global:adtRemote = @{}
$global:adtUploads = 0
$global:adtPosts = 0
$global:adtCorrupt = $false
function aws {
    $a = @($args)
    $global:LASTEXITCODE = 0
    if ($a[0] -eq 's3api') {
        $key = $a[[array]::IndexOf($a, '--key') + 1]
        if ($global:adtRemote.ContainsKey($key)) { return ($global:adtRemote[$key] | ConvertTo-Json -Compress) }
        $global:LASTEXITCODE = 254
        return 'An error occurred (404) when calling the HeadObject operation: Not Found'
    }
    if ($a[0] -eq 's3' -and $a[1] -eq 'cp') {
        $global:adtUploads++
        $key = $a[3].Substring("s3://$env:R2_BUCKET_NAME/".Length)
        $hash = $a[[array]::IndexOf($a, '--metadata') + 1].Substring('sha256='.Length)
        $length = (Get-Item -LiteralPath $a[2]).Length
        if ($global:adtCorrupt) { $length++ }
        $global:adtRemote[$key] = @{ContentLength=$length; Metadata=@{sha256=$hash}}
        return
    }
    throw 'Unexpected mock AWS invocation'
}
function Invoke-RestMethod {
    param($Uri, $Method, $Headers, $MaximumRedirection, $TimeoutSec, $Body, $ContentType)
    if ($Headers.Authorization -ne 'Bearer test-publisher') { throw 'Wrong publishing credential' }
    if ($Method -eq 'GET') { return }
    if ($global:adtUploads -ne 2) { throw 'Announcement attempted before both uploads' }
    $global:adtPosts++
    return @{managerUrl="https://adt-preview-downloads.atomicdrifttuner.workers.dev/admin?announcement=$env:ADT_RELEASE_VERSION"}
}
function Expect-Failure([scriptblock]$Action, [string]$Pattern) {
    $failed = $false
    try { & $Action } catch { $failed = $true; if ($_.Exception.Message -notmatch $Pattern) { throw } }
    if (-not $failed) { throw "Expected failure: $Pattern" }
}
& $adtTestScript -Preflight
if ($global:adtUploads -ne 0 -or $global:adtPosts -ne 0) { throw 'Preflight mutated remote state' }
Write-Host 'PASS: read-only preflight'
& $adtTestScript
if ($global:adtUploads -ne 2 -or $global:adtPosts -ne 1) { throw 'Expected two uploads and one announcement' }
$summary = Get-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Raw
if ($summary -match 'sig=|test-secret|test-publisher') { throw 'Private material in summary' }
Write-Host 'PASS: both uploads verified before announcement; summary is safe'
Expect-Failure { & $adtTestScript -Preflight } 'already has uploaded'
Expect-Failure { & $adtTestScript } 'Refusing to overwrite'
Write-Host 'PASS: existing previews are preserved'
$global:adtRemote.Clear(); $global:adtUploads = 0; $global:adtPosts = 0; $global:adtCorrupt = $true
Expect-Failure { & $adtTestScript } 'metadata verification failed'
if ($global:adtPosts -ne 0) { throw 'Announcement prepared despite upload failure' }
Write-Host 'PASS: verification failure prevents announcement'
$env:ADT_RELEASE_VERSION = '../../invalid'
Expect-Failure { & $adtTestScript } 'Use a preview version'
Write-Host 'PASS: invalid version rejected'
$env:ADT_RELEASE_VERSION = '0.9.0-preview.99'; $env:PREVIEW_PUBLISH_TOKEN = ''
Expect-Failure { & $adtTestScript } 'Missing publishing setting'
Write-Host 'PASS: missing configuration rejected'
