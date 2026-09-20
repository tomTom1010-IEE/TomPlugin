[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$PackageId,

    [string]$ReleaseNotesPath,

    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($PackageId)) {
    $PackageId = (& git -C $repoRoot rev-parse --short HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($PackageId)) {
        throw "Unable to determine the current Git commit for the package ID."
    }
}

if (-not $SkipBuild) {
    & dotnet build (Join-Path $repoRoot "TomPlugin.sln") -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "TomPlugin solution build failed."
    }
}

& (Join-Path $PSScriptRoot "Verify-Assemblies.ps1") -Configuration $Configuration
if (-not $?) {
    throw "Assembly verification failed."
}

$runtimePlugins = @{
    KK = @(
        @{ Project = "MakerBlendShapeSync.KK"; File = "KK_MakerBlendShapeSync.dll" },
        @{ Project = "AccessoryBoneBinder.KK"; File = "KK_AccessoryBoneBinder.dll" },
        @{ Project = "DBDECoordinateLoadBridge.KK"; File = "KK_DBDECoordinateLoadBridge.dll" },
        @{ Project = "FaceWeightBinder.KK"; File = "FaceWeightBinder.dll" }
    )
    KKS = @(
        @{ Project = "MakerBlendShapeSync.KKS"; File = "KKS_MakerBlendShapeSync.dll" },
        @{ Project = "AccessoryBoneBinder.KKS"; File = "KKS_AccessoryBoneBinder.dll" },
        @{ Project = "DBDECoordinateLoadBridge.KKS"; File = "KKS_DBDECoordinateLoadBridge.dll" },
        @{ Project = "FaceWeightBinder.KKS"; File = "FaceWeightBinder.dll" }
    )
}

$outputRoot = Join-Path $repoRoot ("artifacts\packages\{0}" -f $PackageId)
$stagingRoot = Join-Path $outputRoot "staging"
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$resolvedReleaseNotes = $null
if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
    $resolvedReleaseNotes = (Resolve-Path -LiteralPath $ReleaseNotesPath).Path
    Copy-Item -LiteralPath $resolvedReleaseNotes `
        -Destination (Join-Path $outputRoot "TomPlugin-RELEASE_NOTES.md")
}

$createdArchives = @()
foreach ($platform in @("KK", "KKS")) {
    $platformStage = Join-Path $stagingRoot $platform
    $pluginDirectory = Join-Path $platformStage "BepInEx\plugins"
    $archivePath = Join-Path $outputRoot ("TomPlugin-{0}-{1}.zip" -f $platform, $PackageId)

    if (Test-Path -LiteralPath $platformStage) {
        Remove-Item -LiteralPath $platformStage -Recurse -Force
    }
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    New-Item -ItemType Directory -Force -Path $pluginDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $platformStage
    Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination $platformStage
    if ($resolvedReleaseNotes -ne $null) {
        Copy-Item -LiteralPath $resolvedReleaseNotes `
            -Destination (Join-Path $platformStage "RELEASE_NOTES.md")
    }

    $manifest = @(
        "# TomPlugin package contents",
        "",
        "- Platform: $platform",
        "- Package ID: $PackageId",
        "- Configuration: $Configuration",
        "",
        "## Runtime plugins",
        ""
    )

    foreach ($entry in $runtimePlugins[$platform]) {
        $source = Join-Path $repoRoot ("artifacts\bin\{0}\{1}\{2}" -f
            $entry.Project, $Configuration, $entry.File)
        if (-not (Test-Path -LiteralPath $source)) {
            throw "Missing runtime plugin output: $source"
        }

        Copy-Item -LiteralPath $source -Destination $pluginDirectory
        $version = (Get-Item -LiteralPath $source).VersionInfo.FileVersion
        $manifest += "- $($entry.File) $version"
    }

    $manifest += @(
        "",
        "Unity authoring assemblies are distributed separately and are not game runtime plugins."
    )
    Set-Content -LiteralPath (Join-Path $platformStage "PACKAGE_CONTENTS.md") `
        -Value $manifest -Encoding UTF8

    Compress-Archive -Path (Join-Path $platformStage "*") `
        -DestinationPath $archivePath -CompressionLevel Optimal
    $createdArchives += $archivePath
    Write-Host ("[OK] Created {0}" -f $archivePath)
}

$checksums = foreach ($archivePath in $createdArchives) {
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    "{0}  {1}" -f $hash, (Split-Path $archivePath -Leaf)
}
Set-Content -LiteralPath (Join-Path $outputRoot "TomPlugin-SHA256SUMS.txt") `
    -Value $checksums -Encoding ASCII

Write-Host "All-platform runtime packages are complete."
