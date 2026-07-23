# Local reference assemblies

`lib/KK` and `lib/KKS` contain local game and plugin reference DLLs used only at
build time. DLL files are ignored by Git and must not be redistributed from this
repository.

The expected layout mirrors the game folders:

```text
lib/<game>/BepInEx/core/
lib/<game>/BepInEx/plugins/
lib/<game>/Managed/
```

`lib/<game>/Versions/` stores explicitly pinned compile-time references when an
existing plugin must preserve its previous assembly-reference contract. For
example, FaceWeightBinder remains compiled against KKSAPI 1.42.2 while other
KKS projects use the current shared KKSAPI reference.
