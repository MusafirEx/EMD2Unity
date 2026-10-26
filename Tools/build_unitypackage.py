#!/usr/bin/env python3
"""Create a classic Unity .unitypackage from UPM package sources."""
import argparse
import io
import pathlib
import tarfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
FILES = (
    "Editor",
    "Editor/EmdImporter.cs",
    "Editor/EmdImporterInspector.cs",
    "Editor/PemtImporter.cs",
    "Editor/PemtMotionLibrary.cs",
    "Editor/PemtMotionLibraryInspector.cs",
    "Editor/PemtAnimationPreviewWindow.cs",
    "Editor/PemtImporterInspector.cs",
    "Editor/PemtMotionBrowserWindow.cs",
    "Editor/PemtAnimationClipExporter.cs",
    "Editor/PemtAnimationBatchExporter.cs",
    "Editor/SeqScriptLibrary.cs",
    "Editor/SeqImporter.cs",
    "Editor/SeqImporterInspector.cs",
    "Editor/SeqBrowserWindow.cs",
    "Editor/MusafirEx.EMD2Unity.Editor.asmdef",
)

def entry(tf, name, content, directory=False):
    info = tarfile.TarInfo(name)
    info.mtime = 0
    if directory:
        info.type = tarfile.DIRTYPE
        info.mode = 0o755
        tf.addfile(info)
    else:
        info.size = len(content)
        info.mode = 0o644
        tf.addfile(info, io.BytesIO(content))

def build(destination):
    with tarfile.open(destination, "w:gz") as tf:
        for relative in FILES:
            full = ROOT / relative
            meta = ROOT / (relative + ".meta")
            if not full.exists() or not meta.exists():
                raise FileNotFoundError("Missing source or .meta: " + relative)
            guid = next(line.split(":", 1)[1].strip() for line in meta.read_text().splitlines() if line.startswith("guid:"))
            path = "Assets/EMD2Unity/" + relative
            if full.is_dir():
                entry(tf, guid + "/asset", b"", True)
            else:
                entry(tf, guid + "/asset", full.read_bytes())
            entry(tf, guid + "/asset.meta", meta.read_bytes())
            entry(tf, guid + "/pathname", path.encode("utf-8"))
    return destination

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="EMD2Unity-v0.4.1.unitypackage")
    args = parser.parse_args()
    output = pathlib.Path(args.output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    build(output)
    print(output)
