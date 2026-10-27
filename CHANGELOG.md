# Changelog

## 0.5.0 — Stage EMD 0x200 multi-mesh support

- STG30/AREA01.emd has five independently validated mesh descriptors within one type-0x200 polygon: 11,243 vertices and 6,769 triangles. Headers: 0x5B0, 0x9BE0, 0x13110, 0x1C770, and 0x25CB0.
- Five 20-byte/vertex UV streams use 224,860 of the 224,880 remaining file bytes after the UV offset 0x2B870.
- The parser now consumes all validated type-0x200 static mesh groups rather than treating the polygon preamble as an empty mesh; UV blocks advance sequentially.
- Existing guarded 0x4000 position-stream correction applies to these groups. Other header types, character import, PEMT Preview/Export All, SEQ, and CCM remain unchanged.
- EMD ScriptedImporter revision 9 reimports files. Live Unity compilation and mesh/UV appearance need verification.


## 0.4.9 — investigate Stage EMD headerType 0x200 zero-mesh records

- Extends the **Diagnose Selected Stage EMD** report when a polygon has `headerType=0x200` and the conventional parser reads zero vertices.
- Reports polygon record end/size, group-start bytes, first nonzero group word, relative pointer target bytes/words, and global UV-header bytes.
- Adds a bounded, **read-only** scan for candidate conventional mesh-descriptor headers within the polygon. Every candidate must pass UV stride, index/position continuity, vertex bounds, triangle index and polygon bounds checks. Candidates are *not* automatically converted.
- Includes companion `.emdi` file presence, size and initial signature (when present). Existing assets are not modified.
- The `STG30/AREA01.emd` sample log shows a 403,168-byte file with one `0x200` polygon and a zero-vertex group; the previous diagnostics interpreted the first nonzero word as a relative index pointer. Its semantics remain unknown. Model bytes or this extended report are required before implementing a decoder.
- Existing Stage EMD/Character EMD parser, Preview/Export All, SEQ and CCM unchanged.


## 0.4.8 — guarded contiguous Stage EMD XYZ streams

- STG31/AREA01 diagnosis shows `obj_10` (2220 vertices, index maximum 2219) and `obj_11` (1916 vertices, maximum 1915) each report 0x4000 vertex bytes even though their XYZ triples plausibly continue beyond that boundary. For `obj_10`, the complete float3 stream ends 48 bytes before the next polygon.
- Only when **headerType=0, one group, group 0, vertexBytes=0x4000, the index buffer ends at the vertex stream, all indices are valid, some indices exceed the nominal chunk capacity, and the entire full XYZ stream fits inside the polygon record** does the parser accept a longer contiguous position stream.
- Other cases still fail safely with extra polygon/index boundary diagnostics. A warning identifies every accepted stage polygon; visual verification remains required.
- Bumps EMD ScriptedImporter revision from 7 to 8 to reimport stage models. Ordinary character EMD files, PEMT decoder/Preview/Export All, SEQ and CCM code are unchanged.
- This is a conservative layout extension based on the supplied STG31/AREA01 report, not a claim that every Stage EMD variant is solved. Unity Editor mesh/compile verification pending.


## 0.4.7 — Stage EMD buffer/index diagnostics

- Added **Tools > EMD2Unity > Diagnose Selected Stage EMD**, also available in the EMD Model Inspector.
- Performs read-only structural inspection of each polygon's FIRST mesh group: point count, position chunk bytes/capacity, vertex/index offsets, UV length, maximum index, and whether indexed faces refer beyond the advertised 16 KiB position chunk.
- Displays raw bytes and a float3 candidate immediately following the declared position chunk, to distinguish contiguous data from a split/alternate vertex layout. Reports both index-count and possible face-count interpretations where their buffers permit.
- Based on recurring stage errors with vertexBytes=0x4000 while pointCount requires >16 KiB. No blind geometry reconstruction or speculative stream merging; other mesh groups are explicitly NOT decoded in this diagnostic.
- Verified equivalent Python first-group field-offset scanner on all 19 DLC character EMDs without a vertex-count mismatch. Stage sample bytes still needed to establish the correct repair.
- **No changes** to EmdParser, character geometry, PEMT Animation Preview/Export All, SEQ or CCM.


## 0.4.6 — conservatively accept larger, aligned PEMT declared file sizes

- Investigated the `ACL00/motion/0000.pemt` v0.4.5 diagnostics: 66 nonzero motion pointers, all within the 777,040-byte physical file, 16-byte aligned, strictly ordered, and a readable last motion descriptor table. Header declares 1,194,384 bytes, so physical and declared sizes differ.
- The importer now **conditionally** accepts a header-declared size larger than the physical file only if both are 16-byte aligned, the declaration stays within the existing 128 MiB file cap, and the original pointer/motion/track metadata validation succeeds for every block. Smaller declared sizes, malformed pointers and invalid track bounds remain errors.
- Stores the declared size/mismatch in PEMT imported metadata and displays a visible Inspector warning for accepted mismatches. Successful import is **not** proof that every ACL keyframe payload decodes correctly.
- Bumps PEMT ScriptedImporter revision to 4 to re-import formerly rejected ACL files automatically.
- Does not alter the keyframe decoder, .anim Export Selected/All, in-memory Animation Preview, EMD, SEQ or CCM paths.
- Offline validation: all 17 existing CSD/GSD/SPC sample PEMTs pass with original size headers and with synthetically enlarged declared-size headers. Live ACL Unity import and playback verification pending.


## 0.4.5 — read-only diagnostic for unsupported ACL PEMT

- Adds **Tools > EMD2Unity > Diagnose Selected PEMT Binary**. Select a PEMT source file (including one with an import error) to copy an evidence report to the clipboard and Unity Console.
- Includes raw big-endian header, declared vs physical size, pointer-table bounds, first/last pointers, out-of-bounds counts, alignment, monotonicity and first/last motion-block headers.
- Does not change the PEMT decoder, supported file formats, Animation Preview, Export All, EMD, SEQ or CCM logic.
- Built for investigating six ACL PEMT assets rejected after the v0.4.4 header-size diagnostics. Compression and truncation remain unverified.


## 0.4.4 (PEMT header error diagnostics)

- Reports separately which supported PEMT header check fails: pointer count, pointer-table offset, declared file size, or table/file boundary, including the actual values.
- Reimports PEMT metadata after changing ScriptedImporter version to 3.
- Preserves existing valid motion decoding, Animation Preview and Export All. No changes to EMD, SEQ or CCM.
- Unsupported ACL variant still needs raw header values or source file for further investigation; no speculative parsing.


## 0.4.3 (experimental numeric CCM-to-PEMT cross-reference)

- Added a read-only **CCM / PEMT Numeric Cross-Reference** section to the existing CCM Inspector and Browser. No CCM, PEMT or EMD binary readers are changed.
- Scans imported PEMT metadata in a sibling `motion/` directory for the selected character's `combo/0000.ccm`.
- Reports per-record match counts and inspectable candidate PEMT Motion IDs using four comparisons: full raw CCM code, code low16, low16+0x100, low16+0x200. Displays PEMT name, Motion ID, keyframe duration and track count; allows opening its Motion Browser.
- Labels results as **numerical hypotheses only**, not proven action-to-motion mapping. This deliberately does not create an Animator Controller, link combo transitions, or trigger animation playback.
- Retains the existing v0.4.2 CCM structural reader, v0.4.1 SEQ tool, and v0.4.0 PEMT preview/export. Stage model work remains deferred.
- Live Unity Editor compile and visual validation pending.


## 0.4.2 (read-only CCM combo record Inspector)

- Added a `CCM\\0` ScriptedImporter, `CcmComboLibrary` ScriptableObject and **Tools > EMD2Unity > CCM Browser**.
- Parses the confirmed little-endian layout: 16-byte header, 8-byte ID/pointer entries, fixed 0x80-byte records and 16-byte empty footer. Strictly validates lengths, offsets and IDs.
- Shows each raw record's byte offset, values at +0x14/+0x18, codes at +0x1C/+0x68, and all 32 raw 32-bit words. Reports observed A-B=20 and whether link indexes exist.
- Clickable low16 link navigation and clipboard raw-word export. Semantic meanings (timing units, combo inputs, motion mapping, 0x0007 code family and 0x57) remain explicitly **unverified**.
- Structure cross-checked against the 3 DLC CCM samples (CSD/GSD/SPC, 19 total records); no changes to EMD, PEMT or SEQ logic.
- Unity 2022.3 live Editor compile and GUI testing pending.


## 0.4.1 (SEQ binary inspector and resource reference browser)

- Added read-only `.seq` ScriptedImporter / SeqScriptLibrary, registered as a matching-file ScriptableObject type for Unity Editor.
- Displays the four big-endian header words, file size, first 128 bytes, printable null-terminated strings and resource categories.
- Indexes candidate 32-bit big-endian pointers that precede the string, separately identifying those with the observed `16 00 00 00` prefix.
- Reports literal unaligned byte-marker frequencies; **no unverified VM/opcode labels, Motion IDs, combo timings or gameplay events**.
- Added search, resource category filter, pointer-candidate toggle, pagination and path copy. Usable from SeqImporter Inspector and **Tools > EMD2Unity > SEQ Browser**.
- Format verified structurally against 12 CSD/GSD/SPC Sousei 1.02 SEQ examples (anm00/setup/cpu/assist). No character EMD parser, PEMT decoder, clip exporter, or Animation Preview modifications.
- Live Unity 2022.3 compilation and Inspector UX testing pending.


## 0.4.0 (PEMT Animation Preview)

- Added **Tools > EMD2Unity > PEMT Animation Preview** window with isolated 3D character preview.
- Play/Pause/Stop, frame stepping, time scrubbing, loop, speed adjustment, camera orbit and mouse-wheel zoom.
- Added **Preview Selected Motion** in both PEMT Inspector and PEMT Motion Browser (shared panel).
- Uses the same `CreateTransientClip` keyframe decoder as working single and batch export; no different rotation/position conversion.
- Uses a temporary copy of the imported character EMD in Unity's `PreviewRenderUtility` scene. No .anim asset, Animator Controller, active Scene modifications or destructive source edits. Preview resources are disposed when switched/closed.
- EMD stage parsing and recovery are unchanged. Live Unity rendering and compile checks still pending.


## 0.3.7 (opt-in partial EMD mesh recovery and vertex diagnostics)

- Added an opt-in **Recover Supported Meshes (skip unsupported polygons)** toggle in the EMD Model tab. Disabled by default to preserve existing successful imports.
- When enabled, polygon parsing recovers at validated polygon-record boundaries after an unsupported vertex or index layout, preserving decoded geometry from other polygons.
- Recoverable errors are limited to malformed binary data, unexpected end of file and numeric offset overflow; unknown runtime exceptions are not silently swallowed.
- `Vertex buffer smaller than declared point count` diagnostics now include mesh name, polygon/group index, required byte count, declared buffer bytes, offsets and index/UV sizes.
- Recovery warnings explicitly report skipped polygon records and successful mesh groups; a no-mesh file remains identified as empty.
- Does **not** guess vertex compression, synthesize missing geometry, alter character mesh topology, or change the PEMT animation decoder/exporter.
- The reference MaxScript treats vertamount as a byte count (12 bytes per position); unsupported stage layouts require examination of actual EMD bytes.
- Unity 2022.3 live import validation pending.


## 0.3.6 (nonstandard EMD stage index layouts)

- Fixed a newly observed failure in `AREA00.emd`: `Triangle index count 64 is not divisible by 3 in enkei`, after the v0.3.5 28-byte UV layout improvement.
- Inspect index buffer byte length before interpreting a non-multiple-of-three field: support a per-triangle field ONLY when a full 3-index-per-triangle buffer (plus under 16 bytes alignment) exists.
- Alternatively recognize one or two explicit trailing zero/0xFFFF padding indices and trim only those marker values; otherwise report that topology cannot safely be determined.
- Log a Unity import warning when the fallback is used, including the original field and mesh name. Do not silently fabricate triangles for unknown topology.
- Bumped the EMD ScriptedImporter version 5 to 6, forcing reimport. Existing EMD character paths and PEMT animation export are unchanged.
- Offline checked first index groups of 19 DLC EMD samples: all use the existing divisible-by-three case, so the fallback is isolated to unusual stage geometries.
- Visual validation of the STG01 stage geometry in Unity is still pending. The actual AREA00 binary was not available for offline inspection.


## 0.3.5 (stage EMD UV layout and empty-model diagnostics)

- Fixed stage `AREA00.emd` import failure `Unsupported UV stride 28 (jimen_naka)` by supporting the 28-byte UV/color layout already covered by the parser's multi-UV branch.
- Retains original support and field interpretation for EMD UV record strides of 8–24 bytes.
- Improves `No polygon groups found` diagnostics with declared polygon count and zero-vertex group count; a model with no decoded geometry remains an empty root and is not presented as a successful rendered mesh.
- Adds a warning in the EMD Model/Rig/Materials/Animation inspector if the resulting model contains no valid renderable meshes.
- Bumps EMD ScriptedImporter revision 4 → 5 to reimport existing .emd assets after package update.
- The currently selected stage asset `AREA01.emd` was confirmed to import with no geometry; this update diagnoses but cannot invent missing meshes. Live 28-byte stage geometry rendering validation is still pending.
- Does not modify PEMT import, single animation export, or batch animation export.


## 0.3.4 (export all animations from selected PEMT)

- Added **Export ALL Animations in This PEMT** to the shared PEMT Motion Panel, available in both Inspector and PEMT Motion Browser.
- Reused the existing single-animation keyframe decoder to export every Motion ID containing position or rotation tracks as a separate .anim asset.
- Output placed under `<PEMT folder>/<PEMT basename>_Animations/`, e.g. `0000_Animations/0000_Motion_00C.anim`.
- Skips placeholder motions without transform data, preserves existing .anim files, continues on individual export failures, and supports cancellation through Unity's progress window.
- Consolidates results into a single summary and only saves assets after batch completes or cancels; no per-motion dialogs.
- Single-motion export and the underlying PEMT position/rotation decoding remain unchanged. EMD model importer stays at the v0.1.5 behavior.
- Live compilation and bulk-export validation in Unity 2022.3 remain pending.


## 0.3.3 (fix read-only PEMT Inspector)

- Root cause: the PEMT custom Inspector targeted the *generated* PemtMotionLibrary ScriptableObject. When selecting a source PEMT file, Unity's ScriptedImporter presents that generated asset in read-only mode.
- Added PemtImporterInspector : ScriptedImporterEditor, registered for PemtImporter, and renders motion/model controls directly in the importer's interactive inspector.
- Refactored the existing UI into PemtMotionPanel so both the ScriptedImporter inspector and standalone window share the same editing controls and export action.
- Added Tools > EMD2Unity > PEMT Motion Browser; opens a separate window and supports selecting PEMT file, Motion ID, nearby EMD model and exporting AnimationClips even if the Inspector is locked.
- No changes to PEMT binary parser, clip decoder, or EMD model importer (which retains v0.1.5 behavior).
- Compiling and live UI usability still need verification in Unity 2022.3.


## 0.3.2 (PEMT Inspector model and motion selection)

- Fixed EMD Model ObjectField rejecting prefab instances from the Unity Hierarchy (allowSceneObjects=true).
- Resolve an EMD prefab instance back to the imported .emd source to access its original armature/rest pose/importer settings; unpacked scene-only objects are rejected with a clear error.
- Added a Nearby EMD Models dropdown scanning the sibling character/model/ directory, avoiding the need to drag game assets into an ObjectField.
- Replaced the scrolling GUILayout.Toggle list with a Select Motion ID dropdown, automatically selecting the first non-placeholder motion and showing a message when filters match nothing.
- EMD model selector is visible before a motion is selected.
- EMD model importer remains unchanged from v0.1.5; experimental PEMT transform decoder remains unchanged from v0.3.1.
- Unity visual and compile test pending; RTS MCP currently unavailable.


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
