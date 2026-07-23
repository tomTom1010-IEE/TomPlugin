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
- Platform-specific API signatures and plugin identifiers use small
  `#if KK` / `#if KKS` blocks close to the differing code.
- Saved-data keys, public plugin GUIDs, assembly names, and versions remain
  compatible with the existing standalone builds.
- `shared/TomTom.KKMod.Shared` is imported at compile time. It intentionally
  produces no shared runtime assembly and remains compatible with .NET 3.5.
- FaceWeightBinder has KK and KKS runtime targets plus separate Unity 5.6.2f1
  and Unity 2019 authoring projects. Every authoring/runtime combination has an
  isolated intermediate directory, preventing projects with the shared
  `FaceWeightBinder` assembly name from reusing one another's DLL.

## Migration rule

The original standalone repositories remain untouched as a rollback and Git
history source. New development should happen in this workspace after its
build output has been validated in game.
