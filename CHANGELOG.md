# Changelog

## 0.1.3 (EMD index buffer fix)

- Fixed EMD triangle index count: the field stores 16-bit index count, not triangle count.
- Honor declared index count while ignoring trailing alignment padding in the index buffer.
- Validate triangle alignment and report buffer capacity with mesh name when malformed.
- Bumped ScriptedImporter version to 2 to trigger reimport of existing EMD assets.
- Checked index counts and vertex references across 254 first mesh groups from 19 real DLC EMD files (not included in this repository).
- Unity reimport/runtime mesh and rig validation remain pending.

## 0.1.2 (package maintenance)

- Added missing Unity `.meta` files for package manifest, README, changelog, license and classic package builder, preventing immutable Git package warnings.
- Confirmed Editor assembly compiles in Unity 2022.3.62f3; import of real EMD game models remains unverified.
- Updated package version and classic `.unitypackage` build instructions.

## 0.1.1 (experimental)

- Install directly through Unity Package Manager using a Git URL.
- Added Editor assembly definition, stable Unity asset IDs and classic .unitypackage builder.
- Prefer material shaders for the active render pipeline (Built-in, URP or HDRP).
- Correct orientation handedness when swapping Y/Z while preserving a reverse-winding toggle.
- Added README, attribution and build workflow.
- Still awaiting real-file and live Unity Editor verification.

## 0.1.0

- Initial C# EMD parser ported from Random Talking Bush format research.
- Mesh, UV, colour, material, bones and up-to-four-influence weights.
