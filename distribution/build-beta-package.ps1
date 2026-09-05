param(
    [Parameter(Mandatory = $true)]
    [string]$SimHubPath,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$InnoSetupPath = ""
)

$ErrorActionPreference = "Stop"

function ConvertTo-WindowsVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SemanticVersion
    )

    $pattern =
        '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)' +
        '(?:-(?<prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?' +
        '(?:\+(?<metadata>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$'

    if ($SemanticVersion -notmatch $pattern) {
        throw "Version '$SemanticVersion' is not in a supported semantic-version format such as '0.8.2-beta.1' or '0.8.2'."
    }

    $major = [int]$Matches["major"]
    $minor = [int]$Matches["minor"]
    $patch = [int]$Matches["patch"]
    $prerelease = $Matches["prerelease"]

    $revision = 0

    if (-not [string]::IsNullOrWhiteSpace($prerelease)) {
        $numericIdentifiers =
            @(
                $prerelease.Split(".") |
                Where-Object { $_ -match '^\d+$' }
            )

        if ($numericIdentifiers.Count -gt 0) {
            $revision =
                [int]$numericIdentifiers[-1]
        }
    }

    foreach ($component in @($major, $minor, $patch, $revision)) {
        if ($component -lt 0 -or $component -gt 65534) {
            throw "Version '$SemanticVersion' contains a Windows assembly/file version component outside the supported 0..65534 range."
        }
    }

    return "$major.$minor.$patch.$revision"
}

$Version = $Version.Trim()

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "ADT package version cannot be blank."
}

$versionInfoVersion =
    ConvertTo-WindowsVersion `
        -SemanticVersion $Version

$repo = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repo "artifacts"
$publish = Join-Path $artifacts "publish\win-x64"
$staging = Join-Path $artifacts "staging"
$output = Join-Path $artifacts "release"

Write-Host "ADT beta packaging" -ForegroundColor Cyan
Write-Host "Version: $Version"
Write-Host "Windows assembly/file version: $versionInfoVersion"
Write-Host "SimHub build reference: $SimHubPath"

if (-not (Test-Path $SimHubPath)) {
    throw "SimHub path does not exist: '$SimHubPath'"
}

Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue

New-Item $publish -ItemType Directory -Force | Out-Null
New-Item $staging -ItemType Directory -Force | Out-Null
New-Item $output -ItemType Directory -Force | Out-Null

Write-Host "`n[1/5] Publishing self-contained Windows x64 app..." -ForegroundColor Cyan

dotnet publish (Join-Path $repo "src\AtomicDriftTuner\AtomicDriftTuner.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:Version="$Version" `
    -p:AssemblyVersion="$versionInfoVersion" `
    -p:FileVersion="$versionInfoVersion" `
    -p:InformationalVersion="$Version" `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    -o $publish

if ($LASTEXITCODE -ne 0) {
    throw "ADT publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $publish "AtomicDriftTuner.exe"

if (-not (Test-Path $publishedExe)) {
    throw "ADT publish completed without producing '$publishedExe'."
}

Write-Host "`n[2/5] Building SimHub bridge..." -ForegroundColor Cyan

& (Join-Path $repo "bridge\build-bridge.ps1") `
    -SimHubPath $SimHubPath `
    -Version $Version

if ($LASTEXITCODE -ne 0) {
    throw "SimHub bridge build failed with exit code $LASTEXITCODE."
}

$bridgeDll = Join-Path `
    $repo `
    "bridge\AtomicDriftTuner.SimHubBridge\bin\Release\AtomicDriftTuner.SimHubBridge.dll"

if (-not (Test-Path $bridgeDll)) {
    throw "Bridge build completed without producing '$bridgeDll'."
}

Write-Host "`n[3/5] Staging tester payload..." -ForegroundColor Cyan

Copy-Item (Join-Path $publish "*") $staging -Recurse -Force

$bridgePayload = Join-Path $staging "BridgePayload"
New-Item $bridgePayload -ItemType Directory -Force | Out-Null

Copy-Item `
    $bridgeDll `
    (Join-Path $bridgePayload "AtomicDriftTuner.SimHubBridge.dll") `
    -Force

$testerReadme = Join-Path $PSScriptRoot "README-BETA-TESTERS.md"

if (-not (Test-Path $testerReadme)) {
    throw "Beta tester README not found: '$testerReadme'"
}

Copy-Item `
    $testerReadme `
    (Join-Path $staging "README-BETA-TESTERS.md") `
    -Force

$portable = Join-Path $output "AtomicDriftTuner-$Version-portable.zip"

Remove-Item $portable -Force -ErrorAction SilentlyContinue

Compress-Archive `
    -Path (Join-Path $staging "*") `
    -DestinationPath $portable `
    -CompressionLevel Optimal

if (-not (Test-Path $portable)) {
    throw "Portable package was not created."
}

Write-Host "Portable package: $portable" -ForegroundColor Green

Write-Host "`n[4/5] Looking for Inno Setup..." -ForegroundColor Cyan

if ([string]::IsNullOrWhiteSpace($InnoSetupPath)) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    $InnoSetupPath = $candidates |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
}

if (
    -not [string]::IsNullOrWhiteSpace($InnoSetupPath) -and
    (Test-Path $InnoSetupPath)
) {
    Write-Host "`n[5/5] Building installer..." -ForegroundColor Cyan
    Write-Host "Windows installer version: $versionInfoVersion"

    & $InnoSetupPath `
        "/DMyAppVersion=$Version" `
        "/DMyVersionInfoVersion=$versionInfoVersion" `
        "/DRepoRoot=$repo" `
        (Join-Path $PSScriptRoot "AtomicDriftTuner.iss")

    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }

    $installer = Join-Path $output "AtomicDriftTuner-$Version-setup.exe"

    if (-not (Test-Path $installer)) {
        throw "Installer build completed without producing '$installer'."
    }

    Write-Host "Installer package: $installer" -ForegroundColor Green
}
else {
    Write-Host "`n[5/5] Inno Setup not found; installer skipped." -ForegroundColor Yellow
    Write-Host "Install Inno Setup 6 and rerun this script, or pass -InnoSetupPath."
}

Write-Host "`nADT beta distribution build finished." -ForegroundColor Green
