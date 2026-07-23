# FaceWeightBinder

FaceWeightBinder lets KK/KKS clothing and accessory `SkinnedMeshRenderer`
objects authored with the original face skeleton use the current character's
real face bones. It does not own ABMX, DBDE, or BlendShape save data.

FaceWeightBinder 让使用原版面部骨架权重制作的 KK/KKS 衣服或饰品绑定到当前
人物的真实面部骨骼，并与 AccessoryBoneBinder、ABMX、DBDE、
MakerBlendShapeSync 和 KKPE/KKSPE 保持职责分离。

## Projects

| Purpose | Project | Framework / Unity |
| --- | --- | --- |
| KK runtime | `FaceWeightBinder.KK.csproj` | .NET Framework 3.5 |
| KKS runtime | `FaceWeightBinder.KKS.csproj` | .NET Framework 4.6.2 |
| KK authoring | `FaceWeightBinder.Authoring.KK.csproj` | Unity 5.6.2f1 / .NET 3.5 |
| KKS authoring | `FaceWeightBinder.Authoring.KKS.csproj` | Unity 2019 / .NET 4.6.2 |

Both runtime plugins are version `0.1.7.0`. Both authoring assemblies are
version `0.1.0.0`.

The authoring and runtime DLLs intentionally share the assembly name
`FaceWeightBinder`, allowing Unity's serialized `FaceWeightProcess` component
to resolve to the runtime implementation in game. Their output and intermediate
directories are isolated so one project cannot replace another project's DLL.

Authoring 与 Runtime DLL 有意使用相同程序集名，以便 Unity 序列化组件在游戏中
解析；四个项目使用独立输出和中间目录，不会互相覆盖。

## Unity setup

1. Build the Authoring project matching the target game.
2. Import its `FaceWeightBinder.dll` into the Unity project.
3. Copy `Unity/Editor/FaceWeightProcessEditor.cs` into `Assets/Editor`.
4. Add `FaceWeightProcess` to the prefab root.
5. Assign `skeletonRoot` to the placeholder face-skeleton root.
6. Capture and validate renderer bindings before building the AssetBundle.

The editor helper only uses APIs available in Unity 5.6, so the same source is
used by both authoring workflows.

导入网格必须与目标游戏的原版面部网格使用相同模型空间单位。FBX 比例应在
Unity 导入时修正，运行时插件不会自动补偿 `100x`。

## Compatibility

- KK uses the `tomtom.kk.*` plugin GUIDs and KK Coordinate Load Option bridge.
- KKS keeps the existing `tomtom.kks.*` GUIDs and behavior.
- MakerBlendShapeSync and AccessoryBoneBinder expose small optional APIs so the
  binder can request rebind/reapply operations without hard runtime references.
- Custom topology without matching BlendShapes follows facial bones but does
  not automatically inherit expression morphs.

## License

GPL-3.0.

