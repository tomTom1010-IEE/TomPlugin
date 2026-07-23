# TomPlugin

A single source workspace for TomTom's Koikatu (KK) and Koikatsu Sunshine
(KKS) BepInEx plugins. Each plugin keeps one shared codebase while separate
`.KK.csproj` and `.KKS.csproj` files define game-specific frameworks,
references, constants, assembly names, and versions. Paired KK/KKS builds use
the same platform-neutral BepInEx plugin GUID.

这是 TomTom 的 KK/KKS 插件统一源码工作区。每个插件只维护一份主要源码，
通过独立的 `.KK.csproj` 与 `.KKS.csproj` 区分目标框架、游戏依赖、编译常量、
DLL 名称和版本；同一插件的 KK/KKS 版本共用平台无关的 BepInEx GUID。

## Projects

| Plugin | KK | KKS | Unity authoring |
| --- | --- | --- | --- |
| MakerBlendShapeSync | Yes | Yes | No |
| AccessoryBoneBinder | Yes | Yes | No |
| DBDECoordinateLoadBridge | Yes | Yes | No |
| FaceWeightBinder | Yes | Yes | KK 5.6.2f1 / KKS 2019 |

## Build

Local reference DLLs are stored under `lib/KK` and `lib/KKS` and are ignored by
Git. Build everything with:

```powershell
dotnet build TomPlugin.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\Verify-Assemblies.ps1
```

The optional verification command checks every output assembly name and
version. Outputs are isolated by project under `artifacts/bin/`.

See [docs/PROJECT_STRUCTURE.md](docs/PROJECT_STRUCTURE.md) for the repository
layout and platform rules.

## License

TomPlugin is licensed under the
[GNU General Public License v3.0](LICENSE).
