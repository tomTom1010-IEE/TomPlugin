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

Both runtime plugins are version `0.2.0.0`. Both authoring assemblies are
version `0.1.0.0`.

Runtime 0.1.8 excludes clothing, accessories, marked assets, and already-bound
renderers from the character-face reference search. The binding cache also
checks the renderer-to-reference matrix inside the asset every 0.25 seconds;
character motion and target facial-bone changes do not invalidate that matrix.

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
4. For accessories, **add and configure the Cha Acc component first**.
5. **Only then add `FaceWeightProcess` (Face Weight Process)** to the prefab root.
6. Assign `skeletonRoot` to the placeholder face-skeleton root.
7. Capture and validate renderer bindings, then save the prefab before building
   the AssetBundle.

**饰品制作必须先添加并配置 Cha Acc 组件，再添加 Face Weight Process 权重组件。**
顺序反过来已观察到进入游戏后模型放大 **100 倍** 的问题；当前发布流程必须遵守
上述顺序。两个组件配置完成后再执行 `Capture Renderer Bindings` 和
`Validate Bindings`，保存 prefab 后打包。此处记录的是已观察到的制作流程限制，
具体缩放触发机制仍需核实。

The editor helper only uses APIs available in Unity 5.6, so the same source is
used by both authoring workflows.

导入网格必须与目标游戏的原版面部网格使用相同模型空间单位。FBX 比例应在
Unity 导入时修正，运行时插件不会自动补偿 `100x`。

## Initial release (0.2.0)

Install the runtime `FaceWeightBinder.dll` for **your game only** into
`BepInEx/plugins`. KK and KKS DLLs have the same file/assembly name and cannot
be installed together. Do not install the Unity authoring DLL into the game.

Detailed diagnostic logging and snapshot export are **off by default**, including
when upgrading from 0.1.10. Existing snapshot shortcut settings alone do not enable
export. Actual binding failure warnings and errors remain enabled.

Configuration: `BepInEx/config/tomtom.faceweightbinder.cfg`, section `Diagnostics`:

- `Verbose logging = false`: no binding scans or coordinate reports are built.
- `Enable snapshots = false`: keyboard and public snapshot requests are disabled.
- `Snapshot shortcut`: Left Ctrl + Left Shift + F8; effective only when snapshots
  are enabled. A queued snapshot is cancelled if export is disabled before capture.

Restart/re-equip after enabling verbose logging to obtain fresh binding reports.
Bone binding does not require diagnostics, snapshots, or custom BlendShapes.

### Dependencies

Requires the matching game's BepInEx 5 / Harmony 2 environment and KKAPI or KKSAPI.
The plugin declares the API dependency by GUID without a version constraint.
AccessoryBoneBinder, MakerBlendShapeSync, ModBoneImplantor, and Coordinate Load
Option are optional; integration probes installed plugins by GUID/type/method.
ABMX and DBDE are not direct assembly dependencies.

Build references are repository-relative. Override `KKApiReferencePath` or
`KKSApiReferencePath` when compiling against another compatible API. The default
KKS reference is the repository's 1.42.2 DLL, **not** a runtime file path or an
exact-version check. Compiled assembly references still contain build-time
versions; `SpecificVersion=false` only affects build resolution and does not
guarantee compatibility with every API version. Older/incompatible APIs may lack
required members; a universal minimum version has not been established.

Release packages include only this plugin's DLL and documentation/license, not
game, Unity, BepInEx, Harmony, or API DLLs. Use the game's own compatible dependencies.

## Diagnostic snapshots (opt-in)

First enable `Diagnostics / Enable snapshots`. In Maker, equip a marked asset,
wait for binding, then press **Left Ctrl +
Left Shift + F8** once. The shortcut is configurable under Diagnostics in the
plugin's BepInEx configuration. The controller also exposes
`RequestDiagnosticSnapshot()` for runtime inspection tools.

Exports go to `<game>/BepInEx/FaceWeightSnapshots/<UTC timestamp>-<unique id>/`.
The log prints `[FaceWeightSnapshot] Saved:` only after a complete export.
Capture baseline, cheek-width-only, and chin-width-only states separately;
restore the baseline between slider changes. Keep the same character/head and
equip the template debug asset first. No new zipmod is required.

- `snapshot.xml`: frame, Unity/plugin versions, coordinate matrices, renderer
  paths, reference relationships, face slider values, and all current BlendShape
  weights. Matrices are row-major (m00 through m33).
- `asset-N/` and `reference-N/vertices.csv`: raw source positions/normals/UV,
  four bone influences, bone-only skinned positions, and baked positions.
  Both skinned outputs use the character's head-local coordinate system.
- `bones.csv`: bone names/paths, original and active bindposes, world matrices.
- `triangles.csv`: triangle indices and submesh membership.

All renderer data is captured synchronously at the end of one frame. Export
does not rebind the live asset or change its BlendShape weights. Baking uses a
temporary disabled identity-transform renderer to avoid version-dependent
BakeMesh scale behavior; temporary objects are cleaned up. The bone-only result
uses all four stored influences, while the bake uses the recorded renderer/global
skinning quality; differences are not automatically attributed to BlendShapes.
Export may briefly pause the game while writing. An incomplete XML file means
the export failed and must not be used as a complete snapshot.

Vertex indices across different meshes are **not** guaranteed to correspond;
FBX may split vertices at seams. Surface matching is a subsequent analysis step.

## Compatibility

- KK and KKS use the same platform-neutral TomPlugin GUIDs.
- Coordinate Load Option integration remains platform-specific.
- The optional bridges prefer the unified MakerBlendShapeSync and
  AccessoryBoneBinder GUIDs and can still detect their legacy platform GUIDs.
- MakerBlendShapeSync and AccessoryBoneBinder expose small optional APIs so the
  binder can request rebind/reapply operations without hard runtime references.
- Custom topology without matching BlendShapes follows facial bones but does
  not automatically inherit expression morphs.

## License

GPL-3.0.

