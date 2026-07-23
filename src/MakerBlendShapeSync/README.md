# MakerBlendShapeSync

MakerBlendShapeSync adds a compact BlendShape editor to KK/KKS Maker, stores
the edits in character and coordinate cards, and synchronizes the resulting
values into KKPE/KKSPE in Studio.

MakerBlendShapeSync 为 KK/KKS 的 Maker 增加 BlendShape 编辑界面，将结果保存到
人物卡与服装卡，并在 Studio 中同步给 KKPE/KKSPE。

## Targets

| Game | Project | Output |
| --- | --- | --- |
| Koikatu | `MakerBlendShapeSync.KK.csproj` | `KK_MakerBlendShapeSync.dll` |
| Koikatsu Sunshine | `MakerBlendShapeSync.KKS.csproj` | `KKS_MakerBlendShapeSync.dll` |

Both builds use the shared plugin GUID `tomtom.makerblendshapesync`. The
extended-data key remains `MakerBlendShapeSync`, so existing cards and
coordinates remain compatible.

两个版本共用插件 GUID `tomtom.makerblendshapesync`。存档 key 仍为
`MakerBlendShapeSync`，因此旧人物卡和服装卡保持兼容。

## Build

```powershell
dotnet build MakerBlendShapeSync.KK.csproj -c Release
dotnet build MakerBlendShapeSync.KKS.csproj -c Release
```

## License

GPL-3.0.

