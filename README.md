# EMD2Unity

Native **Unity 2022.3+** Editor package for experimenting with PS3 Kamen Rider *Battride War / Battride War Genesis (Sousei)* `.EMD` models. The parser was rewritten in C# based on the format research of [Random Talking Bush](https://github.com/RandomTBush/RTB-3DSMax-Scripts/blob/main/Scripts/KamenRiderPS3_EMD.ms).

> **Experimental:** The Editor assembly was previously compiled in Unity 2022.3.62f3. Version 0.3.0 adds manual export of experimental Unity AnimationClips decoded from PEMT position and quaternion keyframes. The EMD model importer remains unchanged from v0.1.5. PEMT transform/keyframe decoding is not yet implemented. Unity rendering, rig and shader equivalence remain pending. It may require fixes for particular models.

## Install directly from this GitHub repository

Unity → **Window → Package Manager → + → Add package from git URL...**

```text
https://github.com/MusafirEx/EMD2Unity.git
```

A Git package installs from the repository root (`package.json`). It is named `com.musafirex.emd2unity`. To install a local checkout use **Add package from disk...** and choose its root `package.json`.

### Classic Unity package

Download the `.unitypackage` from the package build workflow or create it locally:

```bash
python Tools/build_unitypackage.py --output EMD2Unity-v0.3.0.unitypackage
```

Then in Unity select **Assets → Import Package → Custom Package...** and select the resulting archive. **Do not install both the UPM package and the classic Assets package**, which would register the same importer twice.

## How to use

1. Extract `.EMD` model files from your own game files. This package does not extract NCAT archives or package game assets.
2. Place `*.EMD` in `Assets/Models/`. Unity creates an imported prefab-like GameObject.
3. Select the EMD and use the **Model / Rig / Materials / Animation** Inspector tabs. The animation tab is currently **informational only**; this importer does not parse animation sequences.
4. For texture matching, put extracted `*.dds`, `*.png`, `*.tga` or `*.jpg` in a sibling `textures/` directory, and companion `.emat` files in the sibling `material/` directory. EMD's diffuse reference takes priority; EMAT supplies explicitly named _CO/_NO/_SP maps when unambiguous.
5. In the **Materials** inspector tab click **Prepare Nearby _NO Textures as Normal Maps** to convert supported `_NO` texture import settings and enable normal linking. This modifies only nearby `_NO` texture import settings, then reimports the EMD; it does not convert the underlying PS3 normal-map encoding. The tool also accepts already configured Normal maps. Specular linking is experimental and shader-dependent.
6. Change **Reverse Triangle Winding** if faces are inverted, and test the rig by moving bones.

### PEMT motion inspector and experimental AnimationClip export (v0.3.0)

1. Drop an extracted `*.pemt` in any `Assets/` directory, for example `Assets/KRBWS/CSD/CSD00/motion/0000.pemt`.
2. Unity imports the file as a `PemtMotionLibrary` ScriptableObject. Select it and use the read-only Inspector.
3. Search any Motion ID in hexadecimal (for example `0x140`) or decimal (for example `320`), click the motion, and inspect durations, track types, metadata, and relative payload offsets.
4. Assign a corresponding imported EMD model (e.g. `CSR00.emd` for CSD) to **EMD Model**.
5. Click **Export Selected Motion to .anim** and choose the output path. Test the clip in Unity Animation Window or an Animator Controller on a duplicate matching EMD prefab.
6. If a pose looks twisted, try the alternate **Delta Rotation First** option. Position data is added to the EMD rest-pose position; quaternion motion is multiplied by rest-pose rotation. Rest-pose composition and engine interpolation still require visual validation.

**Newly decoded:** marker `0x02040021` stores position (XYZ + zero padding), `0x02040028` stores quaternion (XYZW); both use four big-endian IEEE-754 half-floats per key. Track-info upper 16 bits is bone suffix ID (`_jN`), lower 16 bits is key count. The track's two offsets point to float32 key times and half-float key values, respectively. Marker `0x40` is a master timeline.
**Limitations:** no automatic clip stitching, root-motion extraction, events, scale curves, original interpolation reconstruction, or humanoid mapping. Additional bones absent from the chosen EMD are skipped and reported.

The PEMT parser uses big-endian header and 32-byte track descriptors. In the game samples, header word `0x400`, a 0x10 pointer-table offset, ascending aligned motion-block pointers, and an apparent ~60 Hz duration grid were observed. ID 0 is treated as an indexed entry, not automatically named "idle". The nominal frame count is inferred from duration × 60, not guaranteed to equal Unity animation sample counts.

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
| PEMT AnimationClip export | Experimental manual .anim output from position and quaternion keyframes |
| Automatic animation assignment, EMD export, DLC packaging | Not supported |
| Offline sample inspection | 19 EMD and 117 EMAT files examined; Unity visual validation pending |

Report the Unity Console error and model-specific findings via GitHub Issues. Do not commit copyrighted game content to this repository.

See [CHANGELOG.md](CHANGELOG.md) and [LICENSE.md](LICENSE.md). Thanks to Random Talking Bush for the original 3ds Max format research, particularly the bone and weight mapping.
