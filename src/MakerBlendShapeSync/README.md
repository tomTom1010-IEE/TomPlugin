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

The two builds preserve their existing GUIDs and versions. The extended-data
key remains `MakerBlendShapeSync`, so existing cards remain compatible.

两个版本保留原有 GUID、版本号和 `MakerBlendShapeSync` 存档 key，不改变旧卡兼容性。

## Build

```powershell
dotnet build MakerBlendShapeSync.KK.csproj -c Release
dotnet build MakerBlendShapeSync.KKS.csproj -c Release
```

## License

GPL-3.0.

