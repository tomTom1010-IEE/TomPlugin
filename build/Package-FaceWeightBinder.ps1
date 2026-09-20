param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = '0.2.0.0'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root "artifacts/packages/FaceWeightBinder-$version" }
if (Test-Path -LiteralPath $OutputDirectory) { throw "Output already exists; use a new directory: $OutputDirectory" }
$audit = Join-Path $root 'artifacts/FaceWeightBinder-0.2.0-dependency-audit.json'
if (-not (Test-Path -LiteralPath $audit)) { throw 'Run Verify-FaceWeightRelease.ps1 first.' }
$results = @(Get-Content -LiteralPath $audit -Raw | ConvertFrom-Json)
foreach ($game in @('KK','KKS')) {
    $dll = Join-Path $root "artifacts/bin/FaceWeightBinder.$game/Release/FaceWeightBinder.dll"
    $identity = [Reflection.AssemblyName]::GetAssemblyName($dll)
    $entry = @($results | Where-Object Platform -eq $game)
    if ($identity.Name -ne 'FaceWeightBinder' -or $identity.Version.ToString() -ne $version -or
        $entry.Count -ne 1 -or (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -ne $entry[0].SHA256) {
        throw "Build/audit mismatch: $game"
    }
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
foreach ($game in @('KK','KKS')) {
    $stage = Join-Path $OutputDirectory $game
    $plugins = Join-Path $stage 'BepInEx/plugins'
    New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root "artifacts/bin/FaceWeightBinder.$game/Release/FaceWeightBinder.dll") -Destination $plugins
    Copy-Item -LiteralPath (Join-Path $root 'src/FaceWeightBinder/README.md') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $root 'src/FaceWeightBinder/RELEASE_NOTES.md') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $stage
    Copy-Item -LiteralPath $audit -Destination (Join-Path $stage 'dependency-audit.json')
    $zip = Join-Path $OutputDirectory "FaceWeightBinder-$game-$version.zip"
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Write-Host "Created: $zip"
}
Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.zip' | Get-FileHash -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
