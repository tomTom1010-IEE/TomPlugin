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

FaceWeightBinder 初版 `0.2.0.0` 默认关闭诊断日志和状态快照，详见
[发布说明](src/FaceWeightBinder/RELEASE_NOTES.md)。制作面部权重饰品时，
**必须先添加 Cha Acc 组件，再添加 Face Weight Process 权重组件**；反序已观察到
模型放大 100 倍的问题。配置完成后再捕获并验证绑定，保存 prefab 后打包。

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
