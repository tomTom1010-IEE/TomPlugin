# Project structure

```text
TomPlugin/
  build/                 Shared KK/KKS reference declarations
  lib/                   Local, ignored game reference DLLs
  shared/                Compile-time shared utilities; no runtime DLL
  src/
    <Plugin>/
      Code/              One implementation shared by supported games
      Properties/        Conditional assembly metadata
      *.KK.csproj        Koikatu target (net35)
      *.KKS.csproj       Koikatsu Sunshine target (net462)
  artifacts/             Isolated build outputs and intermediates
```

## Platform boundaries

- `KK` and `KKS` are set by their project files.
- Platform-specific API signatures and dependency identifiers use small
  `#if KK` / `#if KKS` blocks close to the differing code.
- Paired KK/KKS builds share one platform-neutral `PluginGuid`. Assembly names,
  display names, game API references, and Coordinate Load Option dependencies
  remain platform-specific.
- Persisted `DataId` values are independent, platform-neutral compatibility
  contracts and must not change after release.
- Legacy public `GUID` constants remain as obsolete aliases so dependent source
  and reflection-based integrations do not break during the naming transition.
- Controllers that do not own ExtendedSave data register with a `null` data ID;
  data remains owned by the plugin that serializes it, such as ABMX or DBDE.
- Existing card and coordinate data remains compatible because saved-data keys
  are unchanged. Old platform-specific plugin GUIDs are retained only as soft
  integration probes during migration.
- `shared/TomTom.KKMod.Shared` is imported at compile time. It intentionally
  produces no shared runtime assembly and remains compatible with .NET 3.5.
- FaceWeightBinder has KK and KKS runtime targets plus separate Unity 5.6.2f1
  and Unity 2019 authoring projects. Every authoring/runtime combination has an
  isolated intermediate directory, preventing projects with the shared
  `FaceWeightBinder` assembly name from reusing one another's DLL.

## Plugin identities

| Plugin | Shared KK/KKS GUID |
| --- | --- |
| MakerBlendShapeSync | `tomtom.makerblendshapesync` |
| AccessoryBoneBinder | `tomtom.accessorybonebinder` |
| FaceWeightBinder | `tomtom.faceweightbinder` |
| DBDECoordinateLoadBridge | `tomtom.dbdecoordinateloadbridge` |

When upgrading from a standalone build, replace the old DLL instead of keeping
both versions. The old and new GUIDs are different BepInEx identities, so
installing both can initialize duplicate controllers or patches. This migration
does not change any meaningful card or coordinate data key.

## Migration rule

The original standalone repositories remain untouched as a rollback and Git
history source. New development should happen in this workspace after its
build output has been validated in game.
