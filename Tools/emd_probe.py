#!/usr/bin/env python3
"""Quick endian-safe EMD header probe. Does not extract or convert models."""
import argparse
import pathlib
import struct

def probe(path):
    data = pathlib.Path(path).read_bytes()
    if len(data) < 0x68:
        raise ValueError("EMD header too short")
    print("File:", path, "| bytes:", len(data))
    values = struct.unpack_from(">17I", data, 0x24)
    print("Polygon count:", values[0], "| Bone count:", values[2], "| Material count:", values[3])
    print("Polygon offset:", hex(values[10]), "| Bone offset:", hex(values[12]))
    print("Material offset:", hex(values[13]), "| UV offset:", hex(values[14]))
    offset = values[12]
    for i in range(min(values[2], 12)):
        if offset < 0 or offset + 0x1a0 > len(data):
            print("Bone record exceeds file bounds at", hex(offset))
            break
        name = data[offset + 0x10:offset + 0x30].split(b"\\0",1)[0].decode("shift_jis", "replace")
        parent, child, node = struct.unpack_from(">iii", data, offset + 0x110)
        print(f"Bone {i:03d}: {name!r} parent={parent} node={node} child={child}")
        offset += 0x1a0

if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("emd")
    a = p.parse_args()
    probe(a.emd)
