# Changelog

## 0.1.6 (DDS normal-map support)

- Fixed normal texture linking for Unity 2022.3 DDSImporter, which does not expose TextureImporterType.NormalMap.
- Materials Inspector converts each nearby _NO.dds to a separate _NO_EMD2Unity.png via GPU decompress/readback, then marks that PNG as Normal map and enables linking.
- Prefer the generated PNG when assigning Unity shader _BumpMap/_NormalMap, and preserve source DDS textures.
- The conversion action never runs inside OnImportAsset, never overwrites an existing derivative, and reports individual failures.
- Bumped ScriptedImporter version 4->5. No live Unity compile/render verification yet.
- PS3 normal-map channel packing and inverted green-channel conventions still require visual validation.


## 0.1.5 (normal texture import setup)

- Materials Inspector: **Prepare Nearby _NO Textures as Normal Maps** converts neighbouring _NO textures to Unity Normal-map importer type, enables normal linking and reimports the selected EMD.
- Leaves unrelated textures and already configured normal textures untouched.
- Improved warning when a normal texture exists but is imported as a regular texture.
- Keeps import-setting changes outside OnImportAsset to avoid re-entrant AssetDatabase imports.
- Bumped ScriptedImporter version to 4; Unity compilation and visual inspection are pending.
- Limitation: imported PS3 DDS normals may still require channel/green-axis adjustments to match original rendering.


## 0.1.4 (companion EMAT material references)

- Read bounded ASCII texture-name references from neighbouring TAME-header .emat assets under model/material/ (discovery only, not full shader or sampler decoding).
- Prefer explicitly declared EMD material colour texture; fall back to EMAT _CO only when exactly one choice exists.
- Match _NO/_SP references only to the selected colour texture stem. Expose opt-in normal/specular linking in Materials Inspector.
- Normal linking checks that the corresponding Unity texture has been imported as a Normal map, avoiding incorrect raw-DDS tangent-space interpretation.
- Track source dependencies for .emat and matched textures; bump ScriptedImporter from 2 to 3 to trigger reimport.
- Offline check of 19 game EMD and 117 EMAT files: 97/99 material colour choices resolved, 52 matching _NO and 73 matching _SP references. Two multivariant icon materials intentionally unresolved.
- Live Unity rendering, skeleton inspection and shader correctness remain unverified.


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
