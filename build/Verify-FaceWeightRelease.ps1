param([Parameter(Mandatory=$true)][string]$CecilPath)
$ErrorActionPreference = 'Stop'
Add-Type -Path $CecilPath
$root = Split-Path $PSScriptRoot -Parent
$audit = @()
function Get-AllTypes($types) {
    foreach ($type in $types) { $type; Get-AllTypes $type.NestedTypes }
}
foreach ($platform in @('KK','KKS')) {
    $resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
    foreach ($dir in @("lib/$platform/BepInEx/core", "lib/$platform/BepInEx/plugins", "lib/$platform/Managed", 'lib/KKS/Versions/KKSAPI/1.42.2')) {
        $resolver.AddSearchDirectory((Join-Path $root $dir))
    }
    $parameters = [Mono.Cecil.ReaderParameters]::new()
    $parameters.AssemblyResolver = $resolver
    $dll = Join-Path $root "artifacts/bin/FaceWeightBinder.$platform/Release/FaceWeightBinder.dll"
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll, $parameters)
    try {
        if ($assembly.Name.Name -ne 'FaceWeightBinder' -or $assembly.Name.Version.ToString() -ne '0.2.0.0') { throw 'Unexpected release identity' }
        $types = @(Get-AllTypes $assembly.MainModule.Types)
        $plugin = $types | Where-Object Name -eq 'FaceWeightBinderPlugin'
        $controller = $types | Where-Object Name -eq 'FaceWeightBinderController'
        $awake = $plugin.Methods | Where-Object Name -eq 'Awake'
        foreach ($key in @('Verbose logging','Enable snapshots')) {
            $instruction = @($awake.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq $key })
            if ($instruction.Count -ne 1 -or $instruction[0].Next.OpCode.Name -ne 'ldc.i4.0') { throw "Diagnostic default is not false: $key" }
        }
        foreach ($check in @(
            @($plugin,'Update','get_SnapshotEnabled'),
            @($controller,'RequestDiagnosticSnapshot','get_SnapshotEnabled'),
            @($controller,'LogScanSummary','get_DiagnosticLoggingEnabled'))) {
            $method = $check[0].Methods | Where-Object Name -eq $check[1]
            $firstCall = $method.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'call' } | Select-Object -First 1
            if ($firstCall.Operand.Name -ne $check[2]) { throw "Missing entry guard: $($check[1])" }
        }
        $iterator = $types | Where-Object Name -Like '<CaptureSnapshotAtEndOfFrame>*'
        $moveNext = $iterator.Methods | Where-Object Name -eq 'MoveNext'
        if (-not ($moveNext.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'call' -and $_.Operand.Name -eq 'get_SnapshotEnabled' })) { throw 'Missing queued snapshot guard' }
        $literals = foreach ($type in $types) { foreach ($method in $type.Methods) { if ($method.HasBody) {
            $method.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' } | ForEach-Object { [string]$_.Operand }
        } } }
        if ($literals | Where-Object { $_ -match '^[A-Za-z]:[\\/]|TomXu|KoikatuSunshine[\\/]' }) { throw 'Machine-specific runtime string detected' }
        $dependencies = @($plugin.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'BepInDependency' } | ForEach-Object {
            $argsList = @($_.ConstructorArguments)
            if ($argsList.Count -ne 2 -or $argsList[1].Type.FullName -ne 'BepInEx.BepInDependency/DependencyFlags') { throw 'Unexpected versioned plugin dependency' }
            [ordered]@{ Guid = [string]$argsList[0].Value; Flags = [int]$argsList[1].Value }
        })
        $unexpected = $assembly.MainModule.AssemblyReferences | Where-Object { $_.Name -match 'AccessoryBoneBinder|MakerBlendShapeSync|DBDE|ABMX|CoordinateLoad|TomTom.KKMod.Shared' }
        if ($unexpected) { throw 'Optional integration became a hard assembly reference' }
        $audit += [ordered]@{
            Platform=$platform; Version=$assembly.Name.Version.ToString()
            SHA256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
            References=@($assembly.MainModule.AssemblyReferences | ForEach-Object { $_.FullName })
            PluginDependencies=$dependencies
            Checks='Diagnostic defaults false; shortcut/API/queued export guarded; no machine-specific runtime string; optional integrations have no hard DLL reference.'
        }
        Write-Host "[OK] $platform release defaults, entry guards, runtime strings and dependencies"
    } finally { $assembly.Dispose(); $resolver.Dispose() }
}
$path = Join-Path $root 'artifacts/FaceWeightBinder-0.2.0-dependency-audit.json'
$audit | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding utf8
Write-Host "Audit: $path"
