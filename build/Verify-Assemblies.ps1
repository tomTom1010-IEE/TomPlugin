param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$repoRoot = Split-Path $PSScriptRoot -Parent
$expected = @(
    @{ Project = "MakerBlendShapeSync.KK"; File = "KK_MakerBlendShapeSync.dll"; Assembly = "KK_MakerBlendShapeSync"; Version = "0.5.1.0" },
    @{ Project = "MakerBlendShapeSync.KKS"; File = "KKS_MakerBlendShapeSync.dll"; Assembly = "KKS_MakerBlendShapeSync"; Version = "0.5.1.0" },
    @{ Project = "AccessoryBoneBinder.KK"; File = "KK_AccessoryBoneBinder.dll"; Assembly = "KK_AccessoryBoneBinder"; Version = "0.2.2.0" },
    @{ Project = "AccessoryBoneBinder.KKS"; File = "KKS_AccessoryBoneBinder.dll"; Assembly = "KKS_AccessoryBoneBinder"; Version = "0.2.2.0" },
    @{ Project = "DBDECoordinateLoadBridge.KK"; File = "KK_DBDECoordinateLoadBridge.dll"; Assembly = "KK_DBDECoordinateLoadBridge"; Version = "0.2.0.0" },
    @{ Project = "DBDECoordinateLoadBridge.KKS"; File = "KKS_DBDECoordinateLoadBridge.dll"; Assembly = "KKS_DBDECoordinateLoadBridge"; Version = "0.2.0.0" },
    @{ Project = "FaceWeightBinder.Authoring.KK"; File = "FaceWeightBinder.dll"; Assembly = "FaceWeightBinder"; Version = "0.1.0.0" },
    @{ Project = "FaceWeightBinder.Authoring.KKS"; File = "FaceWeightBinder.dll"; Assembly = "FaceWeightBinder"; Version = "0.1.0.0" },
    @{ Project = "FaceWeightBinder.KK"; File = "FaceWeightBinder.dll"; Assembly = "FaceWeightBinder"; Version = "0.1.7.0" },
    @{ Project = "FaceWeightBinder.KKS"; File = "FaceWeightBinder.dll"; Assembly = "FaceWeightBinder"; Version = "0.1.7.0" }
)

$failed = $false
foreach ($entry in $expected) {
    $path = Join-Path $repoRoot ("artifacts\bin\{0}\{1}\{2}" -f
        $entry.Project, $Configuration, $entry.File)
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Error "Missing build output: $path"
        $failed = $true
        continue
    }

    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($path)
    $fileVersion = (Get-Item -LiteralPath $path).VersionInfo.FileVersion
    if ($assemblyName.Name -ne $entry.Assembly -or
        $assemblyName.Version.ToString() -ne $entry.Version -or
        $fileVersion -ne $entry.Version) {
        Write-Error ("Identity mismatch for {0}: assembly={1}, version={2}, fileVersion={3}" -f
            $entry.Project, $assemblyName.Name, $assemblyName.Version, $fileVersion)
        $failed = $true
        continue
    }

    Write-Host ("[OK] {0}: {1} {2}" -f
        $entry.Project, $assemblyName.Name, $entry.Version)
}

if ($failed) {
    exit 1
}

Write-Host "All assembly identities are valid."
