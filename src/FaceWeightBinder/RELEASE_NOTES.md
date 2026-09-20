# FaceWeightBinder 0.2.0.0 — Initial Release

Binds clothing and accessories marked with FaceWeightProcess and weighted to the
original face skeleton to the current character's face bones.

## Release changes

- Detailed binding scans, reference-search reports, and coordinate diagnostics
  are disabled by default. These reports are not generated while disabled.
- Snapshot export is disabled by default. Both keyboard and API requests require
  the snapshot setting to be enabled. An existing shortcut setting alone does not
  enable snapshots when upgrading.
- Necessary warnings and errors, including binding and compatibility-call
  failures, remain enabled.
- Retains the character-face candidate exclusions and binding cache checks for
  relative transforms inside the asset.
- The Unity authoring DLL remains at 0.1.0.0. This runtime release does not require
  rebuilding existing valid assets.

## Installation

Choose the archive for your game and merge its BepInEx folder into the game
directory. When updating, replace the existing runtime FaceWeightBinder.dll
and avoid duplicate installations.

Both KK and KKS runtime files are named FaceWeightBinder.dll, but they are not
interchangeable and must not be installed together. Do not install the Unity
authoring DLL into the game.

Configuration file: `BepInEx/config/tomtom.faceweightbinder.cfg`.
Under `Diagnostics`, `Verbose logging` and `Enable snapshots` both default to
`false`. **Left Ctrl + Left Shift + F8** exports diagnostic data only after
`Enable snapshots` has been explicitly enabled.

## Required Unity accessory authoring order

**Add and configure the Cha Acc component first, then add Face Weight Process.**
Adding them in reverse order has been observed to make the model **100 times
larger** in game. After configuring both components, click
`Capture Renderer Bindings` and `Validate Bindings`, then save the prefab before
building the AssetBundle. This is an observed authoring constraint for the current
release; the exact cause of the scaling issue is still under investigation.

## Dependency audit

| Build reference | KK | KKS |
| --- | --- | --- |
| BepInEx | 5.4.23.5 | 5.4.23.4 |
| 0Harmony | 2.9.0.0 | 2.9.0.0 |
| Game API | KKAPI 1.46.1.0 | KKSAPI 1.42.2.0 |
| .NET target | 3.5 | 4.6.2 |

These are the build reference versions recorded in the DLL metadata, not
exact-version requirements in the plugin configuration. The required API is
declared through the `marco.kkapi` GUID without a version constraint. The BepInEx,
Harmony, and game API references have no strong-name public key token. The code
does not enforce exact versions or load DLLs from absolute paths on the author's
machine. Compatibility still depends on the environment providing the required
types, methods, and events. Not all historical versions have been tested, and a
minimum supported API version has not been established.

AccessoryBoneBinder, MakerBlendShapeSync, ModBoneImplantor, and Coordinate Load
Option are optional dependencies. The first two and Coordinate Load Option are
integrated by probing the APIs of loaded plugins; no additional copies of their
DLLs are required. ABMX and DBDE are not direct assembly references. Shared code
is compiled into the plugin, so no separate TomTom.KKMod.Shared.dll is needed.

KKS builds use the repository's 1.42.2 API reference by default. Override
`KKSApiReferencePath` to compile against another compatible reference; the KK
equivalent is `KKApiReferencePath`. These are build parameters only.
`SpecificVersion=false` does not guarantee compatibility with every version.

## Validation and limitations

- KK and KKS Release builds passed with zero warnings and zero errors.
- Both binaries passed checks for assembly identity, disabled diagnostic
  defaults, snapshot entry guards, dependency declarations, and machine-specific
  runtime path strings.
- All 12 Unity 5.6 regression checks for reference selection and relative-space
  matrices passed.
- The adjusted model and asset weights were accepted in user testing before
  release preparation. The final release DLLs have not undergone a new full
  in-game validation in both games.
- Custom masks require correct weights. The plugin does not automatically create
  or synchronize missing expression BlendShapes, and does not guarantee that all
  models or extreme face settings will be free of clipping.

Packages do not include game, Unity, BepInEx, Harmony, or API DLLs. Use the
compatible dependencies already installed in the corresponding game environment.
