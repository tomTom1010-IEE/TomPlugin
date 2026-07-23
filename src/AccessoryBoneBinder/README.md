# AccessoryBoneBinder

AccessoryBoneBinder attaches accessory bone chains marked with
ModBoneImplantor's `BoneImplantProcess` directly to matching character body
bones. It does not require AccessoryClothes.

AccessoryBoneBinder 将带有 `BoneImplantProcess` 标记的饰品骨骼链直接连接到同名
人物身体骨骼，不依赖 AccessoryClothes。

## Targets

| Game | Project | Output |
| --- | --- | --- |
| Koikatu | `AccessoryBoneBinder.KK.csproj` | `KK_AccessoryBoneBinder.dll` |
| Koikatsu Sunshine | `AccessoryBoneBinder.KKS.csproj` | `KKS_AccessoryBoneBinder.dll` |

## Unity setup

1. Add `BoneImplantProcess` to the accessory prefab.
2. Assign `trfSrc` to the custom bone-chain root.
3. Assign `trfDst` to a placeholder whose name exactly matches the destination
   body bone.
4. Export the accessory normally.

运行时插件会在饰品载入、换装和坐标切换后重新接骨。安装 ABMX 时，插件使用
`ABB_Sxx_...` 槽位别名维护 per-coordinate 修改，使饰品复制、人物卡和服装卡读取
后的骨骼调整仍能继续生效。

## Dependencies

- BepInEx and KKAPI/KKSAPI
- ModBoneImplantor
- KKABMX/KKSABMX is optional but recommended for editable saved offsets
- Coordinate Load Option is optional

## Build

```powershell
dotnet build AccessoryBoneBinder.KK.csproj -c Release
dotnet build AccessoryBoneBinder.KKS.csproj -c Release
```

## License

GPL-3.0.

