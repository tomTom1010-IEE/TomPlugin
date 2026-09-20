# TomPlugin

A single source workspace for TomTom's Koikatu (KK) and Koikatsu Sunshine
(KKS) BepInEx plugins. Each plugin keeps one shared codebase while separate
`.KK.csproj` and `.KKS.csproj` files define game-specific frameworks,
references, constants, assembly names, and versions. Paired KK/KKS builds use
the same platform-neutral BepInEx plugin GUID.

## Projects

| Plugin | KK | KKS | Unity authoring |
| --- | --- | --- | --- |
| MakerBlendShapeSync | Yes | Yes | No |
| AccessoryBoneBinder | Yes | Yes | No |
| DBDECoordinateLoadBridge | Yes | Yes | No |
| FaceWeightBinder | Yes | Yes | KK 5.6.2f1 / KKS 2019 |

FaceWeightBinder initial release `0.2.0.0` disables diagnostic logging and
snapshots by default. See the [release notes](src/FaceWeightBinder/RELEASE_NOTES.md).
When authoring face-weighted accessories, **add and configure Cha Acc before
adding Face Weight Process**. Adding them in reverse order has been observed to
make the model 100 times larger in game. After configuration, capture and validate
the bindings, then save the prefab before building the AssetBundle.

## Build

Local reference DLLs are stored under `lib/KK` and `lib/KKS` and are ignored by
Git. Build everything with:

```powershell
dotnet build TomPlugin.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\Verify-Assemblies.ps1
```

The optional verification command checks every output assembly name and
version. Outputs are isolated by project under `artifacts/bin/`.

## Release Packages

Every public release is packaged as a complete runtime bundle, even when only
one plugin changed. The KK and KKS archives each contain the platform build of:

- MakerBlendShapeSync
- AccessoryBoneBinder
- DBDECoordinateLoadBridge
- FaceWeightBinder

Build, verify, and package both platforms with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\Package-AllPlugins.ps1
```

Use `-PackageId <name>` to override the default short Git commit used in archive
names, and `-ReleaseNotesPath <file>` to include release notes. Unity authoring
assemblies are distributed separately because they are not game runtime
plugins.

See [docs/PROJECT_STRUCTURE.md](docs/PROJECT_STRUCTURE.md) for the repository
layout and platform rules.

## License

TomPlugin is licensed under the
[GNU General Public License v3.0](LICENSE).
