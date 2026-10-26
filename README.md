# EMD2Unity

Native **Unity 2022.3+** Editor package for experimenting with PS3 Kamen Rider *Battride War / Battride War Genesis (Sousei)* `.EMD` models. The parser was rewritten in C# based on the format research of [Random Talking Bush](https://github.com/RandomTBush/RTB-3DSMax-Scripts/blob/main/Scripts/KamenRiderPS3_EMD.ms).

> **Experimental:** The Editor assembly was previously compiled in Unity 2022.3.62f3. Version 0.1.6 handles DDSImporter correctly: it generates _NO_EMD2Unity.png normal-map derivatives from DDS inputs without altering the original game textures. Conversion and Unity rendering require validation. Unity rendering, rig and shader equivalence remain pending. It may require fixes for particular models.

## Install directly from this GitHub repository

Unity → **Window → Package Manager → + → Add package from git URL...**

```text
https://github.com/MusafirEx/EMD2Unity.git
```

A Git package installs from the repository root (`package.json`). It is named `com.musafirex.emd2unity`. To install a local checkout use **Add package from disk...** and choose its root `package.json`.

### Classic Unity package

Download the `.unitypackage` from the package build workflow or create it locally:

```bash
python Tools/build_unitypackage.py --output EMD2Unity-v0.1.6.unitypackage
```

Then in Unity select **Assets → Import Package → Custom Package...** and select the resulting archive. **Do not install both the UPM package and the classic Assets package**, which would register the same importer twice.

## How to use

1. Extract `.EMD` model files from your own game files. This package does not extract NCAT archives or package game assets.
2. Place `*.EMD` in `Assets/Models/`. Unity creates an imported prefab-like GameObject.
3. Select the EMD and use the **Model / Rig / Materials / Animation** Inspector tabs. The animation tab is currently **informational only**; this importer does not parse animation sequences.
4. For texture matching, put extracted `*.dds`, `*.png`, `*.tga` or `*.jpg` in a sibling `textures/` directory, and companion `.emat` files in the sibling `material/` directory. EMD's diffuse reference takes priority; EMAT supplies explicitly named _CO/_NO/_SP maps when unambiguous.
5. In the **Materials** inspector tab click **Prepare Nearby Normal Maps (DDS to PNG)**. For each neighbouring `_NO.dds`, the tool creates `_NO_EMD2Unity.png` via GPU decompression (unless the derivative already exists); it changes the PNG importer to **Normal map**, enables linking and reimports the EMD. **The DDS original is never changed or deleted.** For other supported `_NO` images it directly configures their import type. Delete a previously generated `_NO_EMD2Unity.png` and click again to regenerate after the DDS changes. Conversion preserves observed channel values; **PS3-specific channel packing (including possible DXT5nm or green-channel inversion) is not automatically decoded**. Specular linking remains experimental.
6. Change **Reverse Triangle Winding** if faces are inverted, and test the rig by moving bones.

## Status

| Component | Status |
| --- | --- |
| Meshes, triangles, UVs, vertex colours | Experimental |
| Diffuse / companion EMAT name matching | Experimental; 97/99 DLC material records resolved in offline tests |
| Normal / specular (_NO / _SP) mapping | Normal DDS->PNG conversion button, opt-in linking; shader-dependent, no full EMAT shader decoding |
| Bone hierarchy / four weight influences | Experimental |
| Inspector: Model, Rig, Materials, Animation | Present (animation tab is informational) |
| Generic/Humanoid avatar setup | Not automatic |
| Animation import, EMD export, DLC packaging | Not supported |
| Offline sample inspection | 19 EMD and 117 EMAT files examined; Unity visual validation pending |

Report the Unity Console error and model-specific findings via GitHub Issues. Do not commit copyrighted game content to this repository.

See [CHANGELOG.md](CHANGELOG.md) and [LICENSE.md](LICENSE.md). Thanks to Random Talking Bush for the original 3ds Max format research, particularly the bone and weight mapping.
