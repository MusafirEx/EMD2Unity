# Changelog

## 0.3.1 (Unity 2022.3 compatibility)

- Replaced Single.IsFinite with IsNaN/IsInfinity checks to avoid .NET API profile incompatibilities in Unity 2022.3.
- Preserved v0.3.0 PEMT clip decoding/export functionality and untouched v0.1.5 EMD importer.
- Offline validation: all 18,629 transform tracks have strictly increasing timestamp samples within their motion duration; Unity compile/visual evaluation is pending.


## 0.3.0 (experimental PEMT animation export)

- Identified 0x02040021 position tracks and 0x02040028 quaternion tracks: big-endian IEEE-754 float32 times and packed half-float XYZ0/XYZW values.
- The high half of each track's info word is the EMD bone-name suffix (_j7, _j150, _j320); low half is key count.
- Validated all 18,938 track descriptors and 198,419 quaternion keys from 17 original PEMT samples; quaternion norms 0.99917–1.00001, no malformed value arrays.
- Added export of selected PEMT Motion ID to .anim using a chosen EMD prefab, rest-pose translation and rotation, quaternion sign continuity and EMD axis/scale options.
- Experimental choice for rotation multiplication order. Unmatched accessory-bone tracks skipped and counted.
- EMD importer remains identical to v0.1.5; no changes to normal texture handling. Visual pose correctness and Unity compilation pending.


## 0.2.1 (fix PEMT ScriptableObject registration)

- Fixed Unity `InvalidOperationException: Failed to add object of type PemtMotionLibrary` during `*.pemt` import.
- Moved `PemtMotionLibrary` (and serializable metadata classes) into new `Editor/PemtMotionLibrary.cs` with stable Unity `.meta`; Unity requires ScriptableObject classes to be declared in a matching filename for asset registration.
- Kept `PemtImporter.cs` responsible only for importer and data parsing, bumped its `ScriptedImporter` version 1 → 2 to reimport failed PEMT assets.
- Added new file to the classic `.unitypackage` build list. EMD parser and normal-texture workflow remain unchanged from v0.1.5.
- Reviewed all 17 PEMT samples: 309 motion blocks / 18,938 track descriptors pass structural pointer checks. Live Unity retest is pending.


## 0.2.0 (PEMT read-only motion container importer)

- Added `PemtImporter` creating a `PemtMotionLibrary` ScriptableObject for `*.pemt` files.
- Validates big-endian header, absolute indexed motion pointers, 32-byte track descriptors and payload bounds.
- Read-only Inspector supports hexadecimal/decimal Motion ID search, durations, 60 Hz nominal frame estimate, track marker/info and payload offsets.
- Keeps raw track marker and info bytes as uninterpreted metadata; does NOT decode transform channels or create AnimationClips.
- Parser designed against 17 v1.02 update sample PEMT files (309 occupied motions, 18,938 track descriptors; no samples shipped).
- Leaves v0.1.5 EMD model importer behavior unchanged, including its DDS normal-map limitations.
- Unity Editor compilation and visual inspection of new PEMT inspector remain pending.


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
