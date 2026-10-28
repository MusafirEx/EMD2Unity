# EMD2Unity

Native **Unity 2022.3+** Editor package for experimenting with PS3 Kamen Rider *Battride War / Battride War Genesis (Sousei)* `.EMD` models. The parser was rewritten in C# based on the format research of [Random Talking Bush](https://github.com/RandomTBush/RTB-3DSMax-Scripts/blob/main/Scripts/KamenRiderPS3_EMD.ms).

> **Experimental:** The Editor assembly was previously compiled in Unity 2022.3.62f3. Version 0.4.3 adds an evidence-only CCM-to-PEMT Motion ID cross-reference panel. Version 0.4.2 added a read-only CCM Inspector/Browser for 0x80-byte combo records. Version 0.4.1 added a read-only SEQ Inspector/Browser for big-endian header words, referenced resource strings and raw byte-marker counts (without guessing VM opcodes). Version 0.4.0 added in-Editor PEMT Animation Preview with Play/Pause/Stop, scrubbing, orbit and zoom. Version 0.3.7 adds optional partial recovery of supported polygon records and detailed vertex buffer diagnostics for stage EMDs. Version 0.3.6 supported verifiable nonstandard triangle counts or trailing padding in EMD stage index buffers. Version 0.3.5 added 28-byte UV/color-record support for stage EMD models and flags imported EMD assets containing no renderable geometry. Version 0.3.4 adds batch export of all PEMT motions with transform keys to separate .anim files. The decoder is the same as the working single-motion export. Version 0.3.3 made PEMT Inspector controls interactive when selecting a .pemt source file. The previous UI was attached to the generated ScriptableObject, which Unity displays read-only. A separate PEMT Motion Browser window is also available. The EMD model importer remains unchanged from v0.1.5. Unity rendering, rig and shader equivalence remain pending. It may require fixes for particular models.

## Install directly from this GitHub repository

Unity → **Window → Package Manager → + → Add package from git URL...**

```text
https://github.com/MusafirEx/EMD2Unity.git
```

A Git package installs from the repository root (`package.json`). It is named `com.musafirex.emd2unity`. To install a local checkout use **Add package from disk...** and choose its root `package.json`.

### Classic Unity package

Download the `.unitypackage` from the package build workflow or create it locally:

```bash
python Tools/build_unitypackage.py --output EMD2Unity-v0.4.9.unitypackage
```

Then in Unity select **Assets → Import Package → Custom Package...** and select the resulting archive. **Do not install both the UPM package and the classic Assets package**, which would register the same importer twice.

### Automatic STG model import defaults (v0.5.9)

EMD2Unity automatically recognizes stage models located under a case-insensitive `stg` folder or in a folder named `STG` followed by digits, such as `Assets/KRBWS/stg/STG31/model/AREA02.emd`. New and existing stage EMD assets use the following **effective** defaults once imported with v0.5.9:

| Model setting | STG default |
| --- | --- |
| Convert PS3 Z-Up to Unity Y-Up | **Off** |
| Align UV Blocks to 16 Bytes | **On** |
| Stage Vertex Colors Are RGBA | **On** |
| Preserve Vertex Colors | **On** |
| Preserve Zero UV Words | **Off** |
| Mesh Source UV Channel Overrides | **Empty (size 0)** |

This preset is based on user-verified STG texturing and is **on by default** for stage folders; no manual configuration is needed. In the EMD **Model** Inspector, leave **Use STG Import Defaults** checked to display the enforced effective values. To customize a particular stage, uncheck it and use the manual settings below (any stored mesh overrides are preserved but ignored while the preset is active).

Stage defaults do **not** apply to non-STG character/motion models; no original game EMD, EMAT or DDS data is edited. Existing EMD subassets will be reimported when Unity detects the updated ScriptedImporter revision 16. You can confirm the actual effective parser flags in **Diagnose EMD UV Mapping (Read-only)**.


### Phase 5B: Direct FBX → Weapon EMD (one-window export, experimental)

**Tools → EMD2Unity → Direct FBX to Weapon EMD (Experimental)** replaces the older two-window FBX Preparation + Topology Rebuild workflow for users who want to export immediately. **An original `_Wxx.emd` template is still required:** the existing game-specific format's bones, material assignments, polygon count, opaque 12-byte per-vertex attributes, and skin data cannot reliably be synthesized from a standalone FBX yet.

**One-step workflow:**
1. Import a Blender FBX into Unity, enable **Read/Write Enabled** under its Model import settings, and provide UV0 if you want to export new UVs.
2. Open **Direct FBX to Weapon EMD**. Choose the **original** game `DRV00_W00.emd` as **Original Weapon .emd (template)** and choose the FBX asset or a scene instance as **FBX or Blender Model**.
3. Each EMD polygon/material slot has a dropdown to select an FBX mesh **or FBX submesh/material slot**. `Auto-map meshes` matches identical names and maps a single FBX entry to the first EMD slot. `Map in order` assigns available entries sequentially. Leave unmapped EMD slots as original. The supported total number of polygon/material slots is determined by the original EMD; creating new slots is not supported.
4. Enable **Bake FBX root transform** to apply the FBX root's position, scale and rotation (default). Turn it off if a scene object's root placement should be ignored. **Export FBX UV0** defaults on; vertex colors can be opted into if the FBX has them.
5. Click **EXPORT DIRECT FBX → NEW .EMD**. The tool remaps only referenced FBX submesh vertices and triangle indices, copies geometry into transient memory, calls the existing guarded EMD binary rebuild/parse and prompts for a **new** `.emd` output filename. It will not overwrite an existing or original source file. Temporary meshes/GameObjects are destroyed after export; no intermediate `.asset` or Scene copy remains.

**Direct means no preparation workflow or donor-distance threshold.** The converter **still transfers source EMD 12-byte auxiliary vertex blocks and raw 8-byte bone weights from nearest original positions**. It does not encode an FBX armature, new material assignments, faithful FBX normals/tangents or completely new EMD skeletons. For very different FBX shapes, the result may render or deform incorrectly. The prior Phase 4 native-topology test passed in game, but the arbitrary FBX direct-conversion path has **not been verified in Unity or in game yet**. Keep original game backups.

### Phase 5A troubleshooting: FBX donor-distance limit exceeded

If FBX preparation reports `handleblade_0: transformed vertex 392 is 1.1426 Unity units from its closest original vertex`, this is **not** a parsing error or changed-topology failure. The default safety limit in **Weapon FBX Preparation** is 1.0 Unity unit. The source and edited model positions may be misaligned, their relative scale/axis may differ, or the FBX design may genuinely extend away from the original mesh.

A **read-only Analyze Mapping Distances** button is available in **Weapon FBX Preparation**. It reports (for every assigned mesh) source/FBX bounds centres/sizes, nearest-original donor max/mean/95th-percentile distances, counts exceeding the current Preparation limit, counts exceeding the separate **Topology Rebuild 0.5 EMD-unit default**, and the most distant FBX vertex/donor indices. The report is logged to the Unity Console for MCP analysis; it writes no files.

1. Run the diagnostic and inspect alignment/bounds first. For obvious FBX scale/axis/position mistakes, fix its object/mesh transform in Blender or Unity and rerun.
2. If the larger silhouette is intentional, for a controlled experiment set **Max preview donor distance (Unity units)** above the reported maximum, for example **1.2** for a 1.1426-unit outlier. This permits nearest-original skin-weight preview assignment but does **not** prove its validity.
3. Then in **Weapon EMD Topology Rebuild**, set **Maximum donor distance (EMD model units)** above the diagnostic's reported required raw EMD radius; if the EMD importer Model Scale is 1, approximately the same value, e.g. 1.2. Turn **Prefer original vertex index for attributes OFF** for Blender-reordered meshes; turn **Write edited UV0 ON** where appropriate.
4. For genuinely dissimilar geometry or bones, nearest original-vertex mapping may animate wrongly; this is an acknowledged limitation, not something an arbitrarily high threshold fixes. Keep backups and inspect in-game results.

### Phase 5A: Prepare Blender/FBX meshes for an existing weapon EMD (EXPERIMENTAL)

**Tools → EMD2Unity → Weapon FBX Preparation (Experimental)** is a separate editor tool added after the successful user-reported in-game +1-vertex/+1-triangle topology rebuild of `DRV00_W00_topology.emd`. Unlike the earlier test, this prepares geometry from an imported Blender FBX without assuming its vertex count/order matches the original source weapon.

1. Import a Blender FBX into Unity. It must have readable meshes (**Read/Write Enabled**), triangle topology and **one material/submesh per imported mesh**. For multiple material slots, split them into separate objects in Blender. Export UV0 for every vertex.
2. In **Weapon FBX Preparation**, assign the original `DRV00_W00.emd` to **Original Weapon**, and your FBX asset or scene instance to **FBX / Blender Root**. One dropdown per original polygon group maps a **specific FBX mesh** to `handleblade_0`, `emblem_00_1`, `car_wild_2`, or `car_vegas_3`. Unmapped groups retain their original geometry. **Auto-map exact mesh names** is optional; manual mapping accepts arbitrary imported mesh names and nested FBX hierarchy.
3. Click **Create Prepared Weapon Scene Copy**. This bakes every chosen FBX child mesh's transform relative to its FBX root, corrects mirrored triangle winding, and creates a NEW native-EMD-based weapon Scene instance with independent `.asset` meshes under `Assets/EMD2Unity_Editable/`. The imported FBX and original EMD are untouched. The existing EMD skeleton and mesh group names are preserved; for Unity preview, native EMD bone weights are assigned using the nearest original vertex. Vertices beyond the configured donor-distance threshold are **rejected** to avoid blindly assigning unrelated skinning.
4. Open **Weapon EMD Topology Rebuild (Experimental)**, assign **Original _Wxx.emd** to the original EMD, and **Edited Scene/FBX Root** to the selected prepared Scene copy. **Turn OFF** `Prefer original vertex index for attributes` for Blender-reordered vertices, and **turn ON** `Write edited mesh UV0`. You may also turn on **Write edited mesh vertex colors** if you specifically want the prepared colors encoded into the original EMD color order. Export to a new `*_fbx_test.emd` and verify parser output plus visual/game tests.
5. Start with replacing **only `handleblade_0`** using a simple Blender mesh in roughly the same size/location as the original. Keep the source `.emdi` intact; do not change material or bone counts. Back up the game before testing on RPCS3.

**Limits:** EMD polygon count/material slots are fixed; the input must be divided into existing EMD groups. The EMD 12-byte auxiliary vertex data is **not yet fully reverse-engineered**, so the exporter copies it and the original bone-weight bytes from nearest native EMD vertices. FBX skinning, armature changes, normals and tangents are **not faithfully encoded into the game file**; FBX normals are recalculated only for Unity preview. It is possible for an FBX preparation to look correct in Unity but shade/deform incorrectly in game. The proven position-patch and Phase 4 controlled +1 triangle remain untouched. Phase 5A's actual arbitrary FBX game compatibility is **not yet verified**.

### Experimental Weapon EMD Topology Rebuild (Phase 4, unverified in-game)

Use **Tools → EMD2Unity → Weapon EMD Topology Rebuild (Experimental)** in the draft branch. This is a separate, opt-in tool from the proven position-only patcher. It accepts a changed triangle and vertex count per **existing** weapon mesh from an editable EMD copy or a readable FBX, while preserving the original number of polygon groups, group names, materials, bone hierarchy and other top-level EMD sections.

- Select the native `_Wxx.emd` as **Original**, and a readable edited Scene/FBX root with matching direct-child mesh names as **Edited**. Mesh child local transforms must be identity and each mesh must have exactly one triangle submesh.
- For a controlled test, use **Create Editable Weapon Copy from Original EMD** in the older Position Patch window, then assign that new Scene root to Topology Rebuild and click **Add 1 Test Vertex + 1 Triangle to Editable handleblade**. The test duplicates vertex 0 at a 0.05-unit X offset and adds one triangle. For original DRV weapon the expected edited `handleblade_0` is **1,096 vertices and 1,049 triangles**, while the other three groups stay unchanged.
- Click **Rebuild NEW Weapon EMD**, save to a *new* filename (no overwrite), and let the bundled EMD parser validate the reconstructed result. The exporter logs the old/new byte sizes, SHA-256 and per-mesh counts. A locally mirrored **binary-layout proof** recreated the unmodified `DRV00_W00.emd` byte-for-byte (86,432 bytes, same SHA-256), and generated a structurally well-bounded 86,448-byte synthetic EMD for an added vertex+triangle. Actual Unity execution of this alpha tool and the modified-topology game's acceptance remain **unverified**.
- Rebuilds indexed 16-byte-aligned triangle, XYZ, 12-byte auxiliary and skin-weight streams, source 8-byte UV-color records, group-relative pointers and polygon/global offsets and bounding boxes. This is restricted to the verified single-group polygon type `0x0004` with 8-byte per-vertex UV records. Source `*.emdi` is left unchanged (material order/count cannot change).
- **Important limits:** The extra 12-byte per-vertex data stream has **not** been decoded. This tool transfers its bytes and original 8-byte bone-weight data from the closest original vertex, rather than generating correct new normals or bone assignments. Attribute transfer is constrained by the configurable maximum donor distance. Optional **Write edited UV0** re-encodes UV0 as big-endian half-floats; source vertex colors are otherwise transferred. Bones/materials must remain identical, and all unfamiliar source layouts are rejected. For substantially different weapon designs, this nearest-neighbor transfer is not reliable. Backup the game and inspect the output in Unity before PS3 testing.

The existing **Weapon EMD Round-Trip Audit** is a **same-layout** audit: it intentionally rejects rebuilt EMDs whose byte size differs. Use the new topology tool's parser and size report for Phase 4, and manually compare gameplay/rendering. Do not merge this experimental topology tool into a proven workflow without a successful in-game test.

### Fix FBX vertex-count mismatches without rebuilding EMD topology

The strict weapon patch exporter cannot accept FBX meshes that differ from the original EMD vertex count or triangle order. In this case it reports the **original EMD expected count**, **source imported Mesh count** and **edited Mesh count** for the specific named group. This is intentional: writing an FBX mesh with new topology into the original fixed-size EMD buffers would corrupt offsets, index topology and skin weights.

For a safe **first actual modified-vertex test**, open **Tools → EMD2Unity → Weapon EMD Position Patch (Experimental)** and choose the **original imported `DRV00_W00.emd`** as source. Click **Create Editable Weapon Copy from Original EMD**. The tool creates a scene instance with four separately cloned, persisted mesh `.asset` files under `Assets/EMD2Unity_Editable/...`; it does not modify the original EMD. The new scene instance is automatically placed in **Edited Model Root**. Click **Apply Test Offset to handleblade_0 (Vertex #0)** with the default X = `+0.05` to change one position without disturbing topology, then export to a new `DRV00_W00_modified.emd`. Use the **Weapon EMD Round-Trip Audit** to verify **1 changed vertex / 0 unexpected non-position bytes**, then test in the PS3 game.

This workaround supports controlled position-only edits of the **native EMD topology**. It does NOT turn arbitrary FBX/Blender mesh replacement into a general EMD exporter. That requires a separate binary rebuild supporting changed vertex/index counts, UV/weight buffers, and offset updates.

### Verify a weapon EMD export byte-for-byte

After importing `DRV00_W00_patched.emd` in Unity, use **Tools → EMD2Unity → Weapon EMD Round-Trip Audit (Experimental)**. Assign **Original Weapon .emd** to `DRV00_W00.emd` and **Patched Weapon .emd** to `DRV00_W00_patched.emd`, then click **Compare Binary EMD Files**. The tool reads the two source files (not Unity mesh previews) and checks identical file lengths, polygon layout, bone/material/UV/skin/index/unknown data bytes and exactly which float32 position bytes changed. It reports source/target SHA-256, changed byte counts, each mesh's changed vertex count/max position delta and **PASS / FAIL**.

- **PASS — byte-identical no-change round-trip** is the required first acceptance test.
- **PASS — XYZ position-only changes** allows the next modified-blade experiment; PS3 compatibility still needs an in-game test.
- **FAIL — non-position data differs** means the file should not be deployed as a position-only replacement.

The audit is strictly read-only and logs the complete report to Unity Console, where it can be read again via the Unity MCP bridge. This lets us verify the user's exported EMD even when the MCP cannot directly download raw binary project assets. Click **Copy Audit Report** to share the detailed results.

### Experimental weapon EMD position-only patch (draft branch)

The `feat/weapon-emd-position-patch-prototype` branch adds **Tools → EMD2Unity → Weapon EMD Position Patch (Experimental)**. This is deliberately **not** a general EMD exporter: it edits only the 12-byte big-endian XYZ positions in a copy of an original `_Wxx.emd`. The imported file's bone matrices/IDs, weight records, vertex order, triangle indices, materials, UV coordinates, vertex colors, unknown header fields and original bytes otherwise remain untouched. It never writes the original source asset.

Verified source sample: `DRV00_W00.emd` from the user (86,432 bytes), with 4 single-group type-0x0004 polygons, 1,754 vertices, 1,596 triangles, 3 bones and 3 materials. Each UV record is 8 bytes and every vertex is weighted to exactly one bone. A Python byte-for-byte unmodified XYZ coordinate round-trip across all vertices reproduced the original SHA-256 exactly. This does **not** establish that arbitrary edited models are accepted by the PS3 game.

1. In the Unity **Project**, select the imported original `DRV00_W00.emd` GameObject asset and duplicate a copy as an editable model (or use a copy of its mesh assets). Keep all existing mesh children named `handleblade_0`, `emblem_00_1`, `car_wild_2`, `car_vegas_3`, and their identity local transforms.
2. Modify **only vertex positions** on readable, independently duplicated meshes. Never edit the original imported mesh asset (which would be overwritten on reimport). Do not change triangle/vertex order, weight assignments, material count or bone hierarchy.
3. Open **Tools → EMD2Unity → Weapon EMD Position Patch (Experimental)**. Drag the original `.emd` imported GameObject into **Original .emd Model Asset**, and the edited scene/prefab root into **Edited Model Root**.
4. Export to a **new path** (e.g., `DRV00_W00_patched.emd`). For the first no-op test, use the unmodified original as both source and edited model; the output should be byte-identical.
5. Reimport the patched EMD in a separate Unity test folder and compare mesh vertex counts, UVs, attachment bones and shading before any game replacement. Preserve a backup of the game archive/source.

Strict limitations: only single-group polygon type `0x0004`, source filename matching `_Wxx.emd`, identical triangle array and skin weights, identical mesh child names and identity transforms, and unchanged materials/UVs. New topology, arbitrary FBX vertex ordering, new bones, changed weights, animation and EMDI generation are **not** supported. Since this patch does not change material entries or their ordering, retain the **original matching EMDI** for the initial weapon replacement test. Unity compilation and actual PS3 compatibility are pending independent validation.

## How to use

1. Extract `.EMD` model files from your own game files. This package does not extract NCAT archives or package game assets.
2. Place `*.EMD` in `Assets/Models/`. Unity creates an imported prefab-like GameObject.
3. Select the EMD and use the **Model / Rig / Materials / Animation** Inspector tabs. The animation tab is currently **informational only**; this importer does not parse animation sequences.
4. For texture matching, put extracted `*.dds`, `*.png`, `*.tga` or `*.jpg` in a sibling `textures/` directory, and companion `.emat` files in the sibling `material/` directory. EMD's diffuse reference takes priority; EMAT supplies explicitly named _CO/_NO/_SP maps when unambiguous.
5. In the **Materials** inspector tab click **Prepare Nearby _NO Textures as Normal Maps** to convert supported `_NO` texture import settings and enable normal linking. This modifies only nearby `_NO` texture import settings, then reimports the EMD; it does not convert the underlying PS3 normal-map encoding. The tool also accepts already configured Normal maps. Specular linking is experimental and shader-dependent.
6. Change **Reverse Triangle Winding** if faces are inverted, and test the rig by moving bones.
7. For stage/AREA `.emd` files, v0.3.5+ recognizes additional 28-byte UV/color records and v0.3.6 checks unusual index counts against actual buffer capacity or verifiable trailing padding.
8. In the **Model** tab, optionally enable **Recover Supported Meshes (skip unsupported polygons)** and click Apply if a stage EMD has multiple polygon records but fails because one record uses unknown vertex/index data. Supported polygons will still import; skipped records and full buffer/offset diagnostics appear in Console. The default remains strict. This does not reconstruct unsupported geometry. If an EMD still imports as an empty root, inspect the Console warning for declared polygon records and zero-vertex groups; stage assets can be empty/reference objects or use a still-unsupported format. Empty imports are **not** proof that a mesh was extracted.

### PEMT motion inspector and AnimationClip exports (v0.4.0)

1. Drop an extracted `*.pemt` in any `Assets/` directory, for example `Assets/KRBWS/CSD/CSD00/motion/0000.pemt`.
2. Unity imports the file as a `PemtMotionLibrary` ScriptableObject. Click the **.pemt file** (not its ScriptableObject subasset) in the Project window. The **PemtImporter** custom Inspector now exposes active controls. Alternatively, use **Tools → EMD2Unity → PEMT Motion Browser** for an independent interactive window.
3. Use the interactive Inspector or Browser window to filter Motion IDs in hexadecimal (for example `0x140`) or decimal (for example `320`), then select a motion using the **Select Motion ID** dropdown.
4. Assign a corresponding EMD model via the **Nearby EMD Models** dropdown, a Project asset, or a linked EMD prefab instance in the Hierarchy (e.g. `CSR00.emd` for CSD) to **EMD Model**.
5. For one motion, click **Export Selected Motion to .anim** and choose the output path. For the entire PEMT, click **Export ALL Animations in This PEMT**. It creates `<PEMT name>_Animations/` beside the selected PEMT with files such as `0000_Motion_00C.anim`, `0000_Motion_100.anim`, etc.
6. Batch export includes any Motion ID with actual position/rotation keyframes (including Motion 0 if it has keys), skips pure timeline/placeholder entries, skips existing .anim assets without overwriting them, continues past individually failed motions, and supports cancellation. A final report gives the exported/existing/skipped/failed counts. The original PEMT and EMD are not modified.
7. Test exported clips in Unity Animation Window or an Animator Controller on a duplicate matching EMD prefab.
8. If a pose looks twisted, try the alternate **Delta Rotation First** option. Position data is added to the EMD rest-pose position; quaternion motion is multiplied by rest-pose rotation. Rest-pose composition and engine interpolation still require visual validation.

### PEMT Animation Preview (v0.4.0)

1. Open **Tools → EMD2Unity → PEMT Animation Preview**. Alternatively, in the PEMT Inspector or Motion Browser, choose a Motion ID and matching EMD then click **Preview Selected Motion (Play / Pause / Timeline)**.
2. Set the **PEMT Container**, a matching imported character **EMD Model**, **Motion ID**, and optional **Delta Rotation First**. Choose a sibling model from **Nearby EMD Models** if applicable.
3. Click **Build / Refresh Preview**. Use **Play, Pause, Stop, +/-1 Frame**, timeline **Time**, **Loop**, and **Speed** (0.1x–2x). Drag over the model to orbit and scroll to zoom.
4. The preview builds a temporary in-memory AnimationClip using the *same keyframe decoder* as single and batch export, and samples it onto a duplicate EMD placed in an isolated Unity preview scene. No .anim files or Animator Controller are created, and the active Unity Scene is not animated or modified.
5. The source EMD must contain an imported Armature and renderable geometry. Stage-model EMD parsing remains deferred.

**Newly decoded:** marker `0x02040021` stores position (XYZ + zero padding), `0x02040028` stores quaternion (XYZW); both use four big-endian IEEE-754 half-floats per key. Track-info upper 16 bits is bone suffix ID (`_jN`), lower 16 bits is key count. The track's two offsets point to float32 key times and half-float key values, respectively. Marker `0x40` is a master timeline.
**Limitations:** no automatic clip stitching, root-motion extraction, events, scale curves, original interpolation reconstruction, or humanoid mapping. Additional bones absent from the chosen EMD are skipped and reported.

The PEMT parser uses big-endian header and 32-byte track descriptors. In the game samples, header word `0x400`, a 0x10 pointer-table offset, ascending aligned motion-block pointers, and an apparent ~60 Hz duration grid were observed. ID 0 is treated as an indexed entry, not automatically named "idle". The nominal frame count is inferred from duration × 60, not guaranteed to equal Unity animation sample counts.

### SEQ Inspector / Browser (v0.4.1)

1. Place your own extracted `*.seq` files under `Assets/`. Select `setup.seq`, `anm00.seq`, `cpu.seq`, or `assist.seq` to use the read-only `SeqImporter` Inspector, or open **Tools → EMD2Unity → SEQ Browser**.
2. Inspect the first four big-endian header words, file size, candidate ASCII strings, and **raw** counts for patterns `12 00 00 00`, `16 00 00 00` and `20 13 00 00`.
3. Filter resource strings by category (`motion-container`, `combo-container`, `sequence-script`, `model`, `effect`, `camera`, `cutin-drama`, etc.) or path. Each record shows its byte offset, big-endian pointer matches before the string, and matches preceded by `16 00 00 00`. Use **Copy path** to copy original resource paths.
4. Unlike `.pemt`, SEQ is **not a keyframe file**. Strings and pointer matches establish resource references, not Motion IDs, animation events, frame timing, hitboxes or gameplay instruction meanings. Raw marker counts are unaligned byte searches, not decoded opcodes.
5. The Inspector does not change SEQ content or attempt an Animator Controller. The existing EMD stage importer and PEMT Animation Preview/export code remain unchanged.

Tested structurally against 12 SEQ samples from DLC 1.02. Live Unity Editor compilation/GUI validation pending.

### CCM Combo Inspector / Browser (v0.4.2)

1. Place extracted `combo/0000.ccm` within `Assets/`. Unity imports a read-only `CcmComboLibrary`, displayed in the source Inspector or **Tools → EMD2Unity → CCM Browser**.
2. Inspect the little-endian `CCM\\0` header, version marker, 8-byte ID/offset pointer entries, exact 128-byte records and terminal 16-byte empty footer.
3. Select individual records to compare **+0x14** / **+0x18** timing-like integers, **+0x1C** code and **+0x68** link candidate (all raw). The observed difference `A-B=20` and link target validity are reported, but **frames/gameplay timing, animation IDs, looping and input-combo semantics are not established**.
4. Click **Inspect Record #** to follow a low-16-bit link to a present record, open the **All 32 raw little-endian words** foldout, or copy raw offsets/values.
5. The parser intentionally rejects malformed or unfamiliar layouts instead of guessing. It is based on structural cross-checking of three Sousei v1.02 DLC samples (CSD: 6 records, GSD: 7, SPC: 6). It does not generate Animator Controllers or play combo sequences. SEQ and PEMT functions are unchanged.

### CCM ↔ PEMT numerical cross-reference (v0.4.3)

1. Select an imported `combo/0000.ccm` and expand **CCM / PEMT Numeric Cross-Reference (experimental)**.
2. Click **Scan Nearby PEMT Motion IDs (read-only)**. The tool checks imported `motion/*.pemt` files within the SAME character folder and lists a match-count matrix for each CCM record.
3. For a selected code, compare four numeric hypotheses: exact 32-bit CCM code, its low16, low16+0x100, and low16+0x200. Each present PEMT motion shows its container name, Motion ID, duration and track count. **Open This PEMT in Motion Browser** for manual inspection.
4. These are numerical coincidences/candidates, NOT proven gameplay mappings, per-hit timing, animation-event offsets, or a runnable combo state machine. A CCM link at +0x68 still refers to a recorded low16 index; it is not assumed to select a PEMT clip.
5. The CSD/GSD/SPC samples share the terminal CCM `0x00070057` code, while their `0000.pemt` containers include candidate `0x157` motions. This is useful evidence for further testing, not an action-to-animation decoder.

### PEMT header error diagnostics (v0.4.4)

Version 0.4.4 identifies the exact failed validation field for previously unsupported PEMT sources (e.g. ACL). The error includes pointer count, offset, declared size and actual size. No decoding rules are relaxed; character animations already working are unchanged.

### Diagnose ACL and other unsupported PEMT files (v0.4.5)

Select a failing `.pemt` file in Unity's Project window. Choose **Tools → EMD2Unity → Diagnose Selected PEMT Binary**. The read-only diagnostic copies a report to your clipboard and Unity Console: big-endian header words, actual and declared bytes, table boundaries, pointer-count statistics, out-of-bounds pointers, first/last motion header and raw duration. The file itself is never changed. Paste the report into the issue or analysis conversation.

**Do not assume compressed or truncated data solely from a declared-size mismatch.** Unsupported files remain rejected until their actual binary structure is verified. Existing PEMT Animation Preview and Export All behavior remains unchanged.

### Aligned declared-size PEMT variants (v0.4.6)

Some ACL files have a header-declared size larger than the physical file size, even though every pointer reported by the diagnostics is within the physical file. In **v0.4.6**, these files are no longer rejected solely because of this difference, provided declared > actual, both sizes are aligned to 16 bytes, the declaration fits within the file-size limit, and **all motion and track metadata bounds validate**. Accepted files show a **yellow Inspector warning**. Other malformed files remain rejected.

Update the package, reimport `ACL/ACL00/motion/0000.pemt`, and check whether it now contains the expected 66 motion entries. Then test several animations using PEMT Animation Preview and Export All. Successful metadata import alone does not prove keyframe decode or correct skeletal playback.

### Stage EMD vertex/index diagnostics (v0.4.7)

Stage meshes may declare more vertices than their 16 KiB position chunk can hold (e.g. 2,155 vertices = 25,860 xyz-float bytes, but the buffer claims 16,384). The model importer **does not guess** the location of missing vertices. Select a Stage `.emd` asset, open its **Model** tab, and click **Diagnose Stage EMD Buffers (Read-only)**, or use **Tools → EMD2Unity → Diagnose Selected Stage EMD**. Paste the copied Console report into the analysis conversation.

The report checks the **first mesh group of each polygon only**: header offsets, point count, index references, UV/vertex byte lengths and candidate vertex bytes after the declared boundary. Multi-group decoding, index topology, and accurate position recovery remain pending until a real Stage EMD sample or report can verify the structure. Existing Character EMD importer behavior is unchanged.

### Stage EMD 0x4000 position-stream fix (v0.4.8)

For observed Stage EMD polygons that report **0x4000 bytes** for position data but have more vertices, the importer can now read a longer contiguous XYZ stream **only** when index/polygon boundaries and out-of-chunk vertex references provide structural confirmation. Invalid or unfamiliar layouts are still rejected. Each accepted oversized polygon emits a Unity import warning for visual verification.

In particular, `STG31/model/AREA01.emd` reported two such meshes: `obj_10` (2220 vertices) and `obj_11` (1916 vertices). Reimport it using EMD2Unity **v0.4.8** and verify the resulting geometry, UV and triangle topology in Scene view. The separate **Diagnose Stage EMD Buffers** tool remains available.

### Stage EMD headerType 0x200 zero-mesh diagnostics (v0.4.9)

Some stage assets (e.g. `STG30/AREA01.emd`) have a polygon record with `headerType=0x200` for which the conventional group reader reports `pointCount=0` and `vertexBytes=0`. **This does not prove that a 400 KB file contains no geometry.** v0.4.9 expands **Diagnose Stage EMD Buffers (Read-only)** to show the group's raw pointer-like words, target data, global UV section, and any alternative *conventional* mesh headers that pass a bounded structural scan. It also records whether a same-name `.emdi` companion exists. The output is evidence only and does not reconstruct an unverified mesh. The established `0x4000` position-stream support remains as-is.

### Bone axis recovery (v0.5.1)

The Rig tab now has **Repair Missing Bone Axes (Experimental)**, enabled by default. When an EMD bone has a zero-length X, Y or Z matrix axis, the importer reconstructs one missing axis from the other two using a cross product. If there is insufficient independent axis information it generates a fallback orthogonal frame. Console warnings identify each affected bone and the recovery method. Turn the option off to enforce strict import. Synthetic transforms can alter pose/skinning; visually verify DRV02/DRV03 and test their animations.

### PEMT motion-track diagnostics and partial preview (v0.5.2)

Some Motion IDs contain unknown PEMT markers (for example `0x02040022` in a DRV02 motion file). Existing confirmed position `0x02040021` and rotation `0x02040028` decoding is unchanged. The unknown track's payload meaning is **not** guessed.

Select a `.pemt` asset and its numerical Motion ID in the Inspector, then click **Copy Selected Motion Track Report (hex / offsets)**. Or open **Tools > EMD2Unity > PEMT Animation Preview** and click **Copy Motion Track Report (hex / offsets)**. The report copies source path, numerical Motion ID, all track marker/info fields, relative and absolute pointer locations and 32-byte raw samples into the clipboard and Unity Console. Use it to investigate unsupported motion formats.

The Preview window also has **Allow Partial Preview (skip unsupported track markers)**. It is OFF by default. When enabled, unknown tracks are omitted *only in the temporary, isolated preview*, and a visible warning plus Console details identify every skipped track. Missing channels remain in the imported pose; animations may appear incomplete. If no supported transform track matches the EMD skeleton, preview still fails with a clear message. **Export Selected** and **Export All** are always strict and never write incomplete clips by skipping unsupported track markers.

### Investigating PEMT marker 0x02040022 (v0.5.3)

The read-only DRV02/0000.pemt report for Motion ID 0x100 (256) found unsupported track #56 for candidate joint j320. Its 8-byte value payload decodes as float16 `(0, 1, 1, 0)`. This suggests, but **does not establish**, XYZ scale plus zero padding. Do not rename numeric Motion IDs.

To investigate safely, open **Tools > EMD2Unity > PEMT Animation Preview**, select the PEMT, matching EMD and Motion ID **0x100**, then enable **Experimental Scale 0x02040022 (Preview ONLY)** before clicking **Build / Refresh Preview**. This option is OFF by default. The experimental decoder requires finite XYZ, zero fourth half and bounded magnitude; it applies XYZ as multipliers of the bone's imported local scale. The first sample has X=0, so the associated bone may collapse along X in preview. Check whether that matches the game pose. A persistent warning and Console track log indicate every unverified channel. The separate **Allow Partial Preview** option can skip other unsupported markers, when explicitly enabled.

The **Copy Motion Track Report** action also displays candidate values and timestamps for up to four samples of each 0x22 track. Export Selected and Export All remain strict and will not create speculative .anim clips. Established position/rotation tracks are untouched.

### Fixing many STG DDS files (v0.5.4)

For the PS3 STG assets, some DDS textures contain valid **partial mipmap chains** that Unity's DDS importer rejects, e.g. `Invalid mipCount: 4 provided`. Instead of converting individual files manually:

1. Install/update EMD2Unity, then open **Tools > EMD2Unity > STG DDS Batch Repair**.
2. Click **Scan STG DDS (Read-only)** to analyze all `.dds` files under `Assets/KRBWS/stg` without changing anything. Optionally use **Export Audit CSV** for a full per-texture report.
3. Inspect the **FIXABLE** versus **Unsupported/ambiguous** counts. Only structurally verified classic single-surface DXT1/DXT3/DXT5 textures with incomplete-but-valid mip chains can be automatically repaired.
4. Click **Apply Safe Fixes (Backups)** and confirm. Originals are backed up first under **`<Unity project>/EMD2Unity_DDS_Backups/Assets/KRBWS/stg/...`**, outside Unity's `Assets` directory.
5. The original path and `.meta` GUID stay unchanged. The fixed file retains the **exact same full-resolution BCn/DXT compressed blocks**, drops partial lower mip levels, and updates the DDS mip header. Unity can generate mipmaps on import if desired. Re-scan to verify and inspect textures in the Inspector/Scene.

The fixer **does not** reinterpret unknown pixel formats, cubemaps, volume/array textures, malformed headers, unexpected byte lengths, or textures that already have a complete mip chain. Those are left unchanged and listed for manual binary investigation. For example, an `Unsupported ...dds file` error does *not* prove it has the same cause as an incomplete DXT1 mip chain. Backups are never overwritten.

This is an **explicit manual action**, not a background asset postprocessor. There is no automatic destructive conversion on project load. Always test on a separate project copy first. The sample `stg01_jimen01_BL.dds` was checked offline for bit-identical top-level image data; Unity acceptance of its fixed DDS needs Editor testing.

### Existing STG DDS backup collisions (v0.5.5)

If Scan reports **21 repairable DDS** but Apply reports `Repaired 0 / Skipped 21` with `Existing backup will not be overwritten`, the issue is backup-name collision, not a new DDS codec. Upgrade to v0.5.5 and run **Tools > EMD2Unity > STG DDS Batch Repair > Scan STG DDS > Apply Safe Fixes (Backups)**. Each source is backed up outside Assets before replacement. Existing backups are never overwritten; a numbered name such as `.backup-0001.dds` is chosen automatically when necessary. The Console logs the new backup location. Scan again to confirm the number of repairable partial mipmap files reaches zero.

**Important:** `No mip fix needed` does not mean Unity has accepted every DDS. Files already stored as a single mip level, such as `stg01_enkei01_CO.dds`, may still need a separate investigation if Unity reports `Unsupported ...dds file`.

### EMD texture mapping audit and safe _CO preference (v0.5.6)

Some STG models appear to have incorrect surface textures. For example, selected `Assets/KRBWS/stg/STG31/model/AREA00.emd` has eight imported meshes and references three `_LI` textures plus one `_CO`. These suffixes indicate a possible misuse of auxiliary light-related data as the diffuse/Base Map, but do not prove which renderer is wrong.

Open the source `.emd` in Project and click the **Materials** tab, then **Diagnose EMD Texture Bindings (Read-only)**, or use **Tools > EMD2Unity > Diagnose Selected EMD Texture Bindings**. This copies a source-material/submaterial/EMAT and mesh-to-Unity-texture mapping report to the clipboard and Unity Console. It makes no changes. Compare material name, original texture reference, candidate `_CO` paths, and actual renderer texture for each mesh.

**Prefer Unique _CO for Diffuse** is enabled by default. The importer changes an auxiliary first submaterial (`_LI`, `_BL`, `_SP`, `_NO`) only when exactly one distinct `_CO` is explicitly referenced by that material's EMD submaterials or its companion EMAT. It does *not* assume a globally similar filename is correct; ambiguous choices keep the original texture and issue a warning. Disable this checkbox and click Apply to preserve the old selection. This is still an experimental interpretation of the game's render materials.

Texture loading remains within the EMD's local `textures/` folder or model folder and now uses a deterministic order. If a local DDS is not importable as a Unity `Texture2D`, a same-basename loadable PNG/TGA can be tried without renaming materials. An auxiliary map with no explicit `_CO` counterpart is not automatically changed. This feature does not alter the original EMD/DDS bytes.

### STG30/AREA00: 16-byte UV-block alignment candidate (experimental)

### STG31/AREA02: UV alignment confirmed in audit; check RGBA vertex colors

A subsequent read-only audit of `STG31/model/AREA02.emd`, with **Align UV Blocks to 16 Bytes** enabled, decoded **26/26 meshes, 20,465 vertices and zero invalid/extreme UV0 coordinates**. Original legacy UV starts were shifted by up to 64 bytes by the final mesh. The user's audit does **not** independently prove correct visual mapping, but supports the 16-byte UV-stream padding hypothesis.

That audit also exposed a **separate packed vertex color ordering issue**: all 26 first-vertex samples ended in `FF`. For example `4C 4C 4C FF` is likely `RGBA(76,76,76,255)`, while the current MaxScript-compatible ARGB parser produces `RGBA(76,76,255,76)` (blue tint and reduced alpha). In **Model**, enable **Stage Vertex Colors Are RGBA (Experimental)** **for this EMD only** and click **Apply**. Keep **Align UV Blocks to 16 Bytes** on and **Mesh Source UV Channel Overrides** empty. The refreshed read-only UV audit shows the first vertex RGBA/ARGB interpretations for verification. This option changes generated vertex colors only, not textures, materials, geometry, or UVs. Keep disabled for other EMDs until source format is verified.



A reported STG30 `AREA00.emd` UV audit showed five meshes with **24-byte UV vertex records**, but the third through fifth meshes had distorted UVs. Comparing the reported raw hex and vertex colors revealed that UV blocks appear to start on **absolute 16-byte file boundaries**, with nonzero padding possible between blocks:

| Mesh | Old sequential UV start | 16-byte aligned candidate |
| --- | --- | --- |
| `obj000_0` | `0x126B0` | `0x126B0` |
| `obj001_0_1` | `0x166A0` | `0x166A0` |
| `obj001_1_2` | `0x24908` | `0x24910` |
| `obj002_3` | `0x250D0` | `0x250E0` |
| `obj003_4` | `0x28580` | `0x28590` |

For example, on `obj001_1_2` the apparent vertex color shifts from corrupted bytes to `E8 E8 E8 FF` and the first primary UV becomes `(-4.875, 10.023)` when the record begins at the 16-byte boundary. Other affected groups show the same pattern. This supports an **alignment error**, not an alternative texture/UV channel.

To test: select the affected `.emd` in Unity's Project panel, open **Model**, set **Mesh Source UV Channel Overrides** size to zero, enable **Align UV Blocks to 16 Bytes (Experimental)**, leave other UV experimental settings disabled, and **Apply**. Run **Diagnose EMD UV Mapping (Read-only)** again. It now prints decoded results for the 16-byte alignment alongside legacy and preserve-zero interpretations. Verify visuals on all five meshes and unrelated characters/stages before using as a default.

**This is an experimental opt-in** until actual Unity rendering and game-original comparison confirm the format rule. No source EMD, EMAT or DDS bytes are edited.

### Experimental EMD UV mapping audit (v0.5.8 branch)

When the correct DDS texture is assigned but the image looks scrambled or randomly mapped on some meshes, examine **UV coordinates** rather than changing material bindings again. The EMD format can contain several UV pairs per vertex. Older import logic chose source channel 0 for 8–16-byte records or channel 3 for 20–28-byte records, following the available MaxScript reference. That choice may not match every game's shader input.

Select the problem .emd in the **Project** window, open **Model**, then click **Diagnose EMD UV Mapping (Read-only)**. The same action is at **Tools > EMD2Unity > Diagnose Selected EMD UV Mapping**. The report is copied to the clipboard and Console. It lists per-mesh UV stride, source UV channels, source block start offset, number of zero bytes skipped after a UV block, two competing UV cursor-advance interpretations, raw UV sample/ranges, and imported Unity UV0. Compare a visibly good mesh against a scrambled mesh; being outside 0..1 is not by itself a defect because repeated textures often tile.

For an individual scrambled mesh, use **Mesh Source UV Channel Overrides** in Model: add an element with exact **Mesh Name** from the report (such as `obj_00_0`) and **Source Channel** (0, 1, 2, 3, etc., only when listed). Click **Apply**. No rule means unchanged legacy UV0. Each original source UV set is additionally copied to Unity UV1 onward to help inspect different channels.

If the report shows nonzero **zero bytes skipped** after a UV block, test **Preserve Zero UV Words (Experimental)** and click **Apply**, then compare the same surfaces. Some EMDs genuinely contain padding between UV blocks; enabling the option can shift their UVs incorrectly. Leave it **off** unless the original game reference and visual inspection support the change. Restoring the default and removing overrides returns to legacy mapping. Both settings affect imported Unity meshes only; the source EMD, EMAT and textures are never modified.

**Validation needed:** A parser's UV statistics do not prove which UV set a PS3 shader used. This diagnostic can isolate corrupted stream offsets versus an alternate UV channel, but correct layered STG rendering may additionally require an EMAT shader/sampler analysis. Live Unity compilation and in-game comparison are pending.

### Ambiguous STG31 material texture references (v0.5.7)

The source-binding report for `Assets/KRBWS/stg/STG31/model/AREA00.emd` confirms eight materials. Several have *two* explicit `_CO` references, so v0.5.6 left their original `_LI` diffuse assignments untouched rather than guessing. In v0.5.7, **Prefer Referenced _CO for Diffuse** uses a narrow exception: when the material name ends in a recognized auxiliary suffix, and **one of its own explicitly referenced** colours is the matching stem (e.g. `stg31_ground02_BL` references `stg31_ground02_CO`), select that `_CO` for Unity Standard Base Map if the local DDS/PNG is importable as a Texture2D. Otherwise leave it untouched.

For the reported AREA00 model, `obj_00_0`, `obj_01_1`, `obj_02_2` and `obj_05_5` have material-stem-matching candidate `_CO` maps. `obj_07_7` remains unresolved; the report lists two ground textures and no matching soukojimen01_CO, which may indicate a layered material. Do **not** assign an arbitrary ground texture as a final fix. Multi-texture blending, masks and PS3 lighting are **not** reconstructed by simply choosing a different main texture.

After upgrading, select `STG31/model/AREA00.emd` in Unity Project, open **Materials**, keep **Prefer Referenced _CO for Diffuse** enabled, and reimport if necessary. Run **Diagnose EMD Texture Bindings (Read-only)** again. Compare exact Unity renderer diffuse sources with the old audit and visually inspect geometry. Untick the option and Apply to return to the first-submaterial mapping.

## Status

| Component | Status |
| --- | --- |
| Meshes, triangles, UVs, vertex colours | Experimental |
| Diffuse / companion EMAT name matching | Experimental; 97/99 DLC material records resolved in offline tests |
| Normal / specular (_NO / _SP) mapping | Opt-in, shader-dependent; no full EMAT shader decoding |
| Bone hierarchy / four weight influences | Experimental |
| Inspector: Model, Rig, Materials, Animation | Present (animation tab is informational) |
| Generic/Humanoid avatar setup | Not automatic |
| PEMT motion-container metadata (ID, duration, track table) | Import and inspect |
| PEMT AnimationClip export | Experimental single-motion and batch .anim export from position and quaternion keyframes |
| PEMT Animation Preview | Unity Editor window, in-memory model playback, scrub, orbit, zoom (v0.4.0) |
| Automatic animation assignment, EMD export, DLC packaging | Not supported |
| Offline sample inspection | 19 EMD and 117 EMAT files examined; Unity visual validation pending |

Report the Unity Console error and model-specific findings via GitHub Issues. Do not commit copyrighted game content to this repository.

See [CHANGELOG.md](CHANGELOG.md) and [LICENSE.md](LICENSE.md). Thanks to Random Talking Bush for the original 3ds Max format research, particularly the bone and weight mapping.
