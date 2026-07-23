# DBDECoordinateLoadBridge

DBDECoordinateLoadBridge preserves DynamicBoneDistributionEditor transfer data
while Coordinate Load Option selectively loads a coordinate card through its
temporary character.

DBDECoordinateLoadBridge 修复 Coordinate Load Option 开启 `Show Selection` 后，
DBDE 服装卡数据在临时人物与真实人物之间过早清除的问题。

## Targets

| Game | Project | Output |
| --- | --- | --- |
| Koikatu | `DBDECoordinateLoadBridge.KK.csproj` | `KK_DBDECoordinateLoadBridge.dll` |
| Koikatsu Sunshine | `DBDECoordinateLoadBridge.KKS.csproj` | `KKS_DBDECoordinateLoadBridge.dll` |

Both DBDE and Coordinate Load Option are soft dependencies. The bridge supports
the tested DBDE 1.5.1 and 2.0.0 cleanup paths and does not parse or rewrite DBDE
card data itself.

DBDE 与 Coordinate Load Option 都是软依赖。桥接插件兼容已测试的 DBDE 1.5.1
与 2.0.0，并始终让 DBDE 自己负责序列化和应用数据。

## Build

```powershell
dotnet build DBDECoordinateLoadBridge.KK.csproj -c Release
dotnet build DBDECoordinateLoadBridge.KKS.csproj -c Release
```

## License

GPL-3.0.

