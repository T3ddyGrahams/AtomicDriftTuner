param(
    [Parameter(Mandatory = $true)]
    [string]$SimHubPath,

    [string]$Version = "0.8.1-beta.1"
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

if ([string]::IsNullOrWhiteSpace($SimHubPath)) {
    throw "SimHub path cannot be blank."
}

$SimHubPath =
    [System.IO.Path]::GetFullPath(
        $SimHubPath.Trim()
    ).TrimEnd(
        [char[]]@('\', '/')
    )

$Version = $Version.Trim()

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "ADT bridge version cannot be blank."
}

$versionInfoVersion =
    ConvertTo-WindowsVersion `
        -SemanticVersion $Version

$project = Join-Path `
    $PSScriptRoot `
    "AtomicDriftTuner.SimHubBridge\AtomicDriftTuner.SimHubBridge.csproj"

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "ADT SimHub Bridge project was not found at '$project'."
}

if (-not (Test-Path -LiteralPath $SimHubPath -PathType Container)) {
    throw "SimHub folder was not found at '$SimHubPath'. Pass the correct folder with -SimHubPath."
}

$requiredAssemblies = @(
    "SimHub.Plugins.dll",
    "GameReaderCommon.dll",
    "SimHub.Logging.dll",
    "Newtonsoft.Json.dll",
    "log4net.dll"
)

$missingAssemblies =
    @(
        foreach ($assemblyName in $requiredAssemblies) {
            $assemblyPath =
                Join-Path `
                    $SimHubPath `
                    $assemblyName

            if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
                $assemblyPath
            }
        }
    )

if ($missingAssemblies.Count -gt 0) {
    $missingText =
        $missingAssemblies -join [Environment]::NewLine

    throw "Required SimHub bridge reference(s) were not found:`n$missingText"
}

Write-Host "Building ADT SimHub Bridge against:" -ForegroundColor Cyan
Write-Host $SimHubPath
Write-Host "Bridge version: $Version"
Write-Host "Windows assembly/file version: $versionInfoVersion"

dotnet build `
    $project `
    -c Release `
    -p:SimHubInstallPath="$SimHubPath" `
    -p:Version="$Version" `
    -p:AssemblyVersion="$versionInfoVersion" `
    -p:FileVersion="$versionInfoVersion" `
    -p:InformationalVersion="$Version" `
    -p:IncludeSourceRevisionInInformationalVersion=false

if ($LASTEXITCODE -ne 0) {
    throw "ADT SimHub Bridge build failed with exit code $LASTEXITCODE."
}

$outputDll = Join-Path `
    $PSScriptRoot `
    "AtomicDriftTuner.SimHubBridge\bin\Release\AtomicDriftTuner.SimHubBridge.dll"

if (-not (Test-Path -LiteralPath $outputDll -PathType Leaf)) {
    throw "Bridge build completed without producing '$outputDll'."
}

$versionInfo =
    [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
        $outputDll)

if ($versionInfo.FileVersion -ne $versionInfoVersion) {
    throw "Bridge file version mismatch. Expected '$versionInfoVersion', got '$($versionInfo.FileVersion)'."
}

if ($versionInfo.ProductVersion -ne $Version) {
    throw "Bridge product version mismatch. Expected '$Version', got '$($versionInfo.ProductVersion)'."
}

Write-Host "`nADT SimHub Bridge build complete." -ForegroundColor Green
Write-Host "Output: $outputDll"
Write-Host "ProductVersion: $($versionInfo.ProductVersion)"
Write-Host "FileVersion: $($versionInfo.FileVersion)"
