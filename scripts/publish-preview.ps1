param([switch]$Preflight)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$version = $env:ADT_RELEASE_VERSION
$hours = 0
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)-preview\.(0|[1-9]\d*)$') {
    throw 'Use a preview version such as 0.9.0-preview.9.'
}
if (-not [int]::TryParse($env:ADT_LINK_HOURS, [ref]$hours) -or $hours -lt 1 -or $hours -gt 168) {
    throw 'Link lifetime must be 1 through 168 hours.'
}
foreach ($name in @('AWS_ACCESS_KEY_ID', 'AWS_SECRET_ACCESS_KEY', 'R2_ACCOUNT_ID', 'R2_BUCKET_NAME', 'PREVIEW_PUBLISH_TOKEN')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Missing publishing setting: $name. See docs/PUBLISH_PREVIEW.md."
    }
}
if ($env:R2_ACCOUNT_ID -notmatch '^[a-fA-F0-9]{32}$' -or $env:R2_BUCKET_NAME -notmatch '^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$') {
    throw 'Invalid R2 account or bucket name.'
}
$manager = 'https://adt-preview-downloads.atomicdrifttuner.workers.dev'
$endpoint = "https://$($env:R2_ACCOUNT_ID).r2.cloudflarestorage.com"
$env:AWS_DEFAULT_REGION = 'auto'
$env:AWS_EC2_METADATA_DISABLED = 'true'
$env:AWS_REQUEST_CHECKSUM_CALCULATION = 'when_required'
$env:AWS_RESPONSE_CHECKSUM_VALIDATION = 'when_required'
$env:AWS_PAGER = ''
$null = Get-Command aws -ErrorAction Stop

function Get-RemoteMetadata([string]$Key) {
    $output = & aws s3api head-object --bucket $env:R2_BUCKET_NAME --key $Key --endpoint-url $endpoint --output json --no-cli-pager 2>&1
    if ($LASTEXITCODE -eq 0) { return (($output -join "`n") | ConvertFrom-Json) }
    if (($output -join "`n") -match '\(404\)|\(NoSuchKey\)|\(NotFound\)') { return $null }
    throw 'R2 metadata check failed. Check the bucket-scoped R2 credentials.'
}

function Invoke-PublishingApi([string]$Method, [string]$Body = '') {
    try {
        $params = @{
            Uri = "$manager/api/prepare-preview"
            Method = $Method
            Headers = @{ Authorization = "Bearer $env:PREVIEW_PUBLISH_TOKEN" }
            MaximumRedirection = 0
            TimeoutSec = 90
        }
        if ($Body) { $params.Body = $Body; $params.ContentType = 'application/json' }
        return Invoke-RestMethod @params
    } catch {
        # Never print HTTP request details or credentials in Actions logs.
        throw 'The preview manager could not prepare the release. Check its publishing credential and that both uploads exist.'
    }
}

$null = Invoke-PublishingApi 'GET'
$assets = @('setup.exe', 'portable.zip') | ForEach-Object {
    $name = "AtomicDriftTuner-$version-$_"
    [pscustomobject]@{ Name = $name; Key = "ADT $version/$name"; Path = Join-Path $PSScriptRoot "../artifacts/release/$name" }
}
if ($Preflight) {
    foreach ($asset in $assets) {
        if ($null -ne (Get-RemoteMetadata $asset.Key)) {
            throw 'This preview version already has uploaded files. Choose a new version; use the manager to refresh existing links.'
        }
    }
    Write-Host 'Preview configuration verified. Both package names are available.'
    exit 0
}

# Validate both files before uploading either one.
foreach ($asset in $assets) {
    if (-not (Test-Path -LiteralPath $asset.Path -PathType Leaf) -or (Get-Item -LiteralPath $asset.Path).Length -eq 0) {
        throw "Missing or empty release package: $($asset.Name)"
    }
    if ($null -ne (Get-RemoteMetadata $asset.Key)) { throw 'Refusing to overwrite an existing preview. Choose a new version.' }
}
foreach ($asset in $assets) {
    $hash = (Get-FileHash -LiteralPath $asset.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $contentType = if ($asset.Name.EndsWith('.zip')) { 'application/zip' } else { 'application/octet-stream' }
    $output = & aws s3 cp $asset.Path "s3://$($env:R2_BUCKET_NAME)/$($asset.Key)" --endpoint-url $endpoint --metadata "sha256=$hash" --content-type $contentType --no-progress --only-show-errors 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Private R2 upload failed. No announcement was prepared.' }
    $remote = Get-RemoteMetadata $asset.Key
    if ($remote.ContentLength -ne (Get-Item -LiteralPath $asset.Path).Length -or $remote.Metadata.sha256 -ne $hash) {
        throw 'Uploaded package metadata verification failed. No announcement was prepared.'
    }
    Write-Host "Uploaded and verified $($asset.Name)."
}
$result = Invoke-PublishingApi 'POST' (@{version=$version; hours=$hours} | ConvertTo-Json -Compress)
$expected = "$manager/admin?announcement=$version"
if ($result.managerUrl -ne $expected) { throw 'Unexpected manager response.' }
if ($env:GITHUB_STEP_SUMMARY) {
    @"
## ADT $version is ready
Both packages were uploaded to private R2. The announcement stays behind manager sign-in.

[Open prepared Discord announcement]($expected)

Links expire $hours hours after preparation. Copy the post into your supporter channel after testing both downloads.
"@ | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
}
Write-Host 'Private preview published. Open the prepared announcement from the workflow summary.'
