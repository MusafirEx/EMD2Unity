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
