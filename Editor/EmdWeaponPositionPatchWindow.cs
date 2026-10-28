// Experimental same-topology weapon EMD position-only patch writer.
// Deliberately does NOT rebuild EMD headers, bones, weights, UVs or materials.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public sealed class EmdWeaponPositionPatchWindow : EditorWindow
    {
        private GameObject sourceModel;
        private GameObject editedModel;
        private string lastResult;

        [MenuItem("Tools/EMD2Unity/Weapon EMD Position Patch (Experimental)")]
        public static void Open()
        {
            GetWindow<EmdWeaponPositionPatchWindow>("Weapon EMD Patch");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Weapon EMD: same-topology position patch", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Experimental: overwrites ONLY the original weapon's XYZ vertex floats " +
                "inside a NEW file. Mesh count, vertex count, triangle indices, mesh names, " +
                "UVs, vertex colors, materials, bones, weights and all unknown source bytes " +
                "remain unchanged. Do not use for new topology or new materials. " +
                "Source EMD is never overwritten.",
                MessageType.Warning);
            sourceModel = (GameObject)EditorGUILayout.ObjectField(
                "Original .emd Model Asset", sourceModel, typeof(GameObject), false);
            editedModel = (GameObject)EditorGUILayout.ObjectField(
                "Edited Model Root", editedModel, typeof(GameObject), true);
            EditorGUILayout.HelpBox(
                "Copy the imported weapon root and edit the existing mesh vertex positions " +
                "without changing vertex order, faces, skinning or child mesh transforms. " +
                "The modified root may be a scene object or prefab asset. " +
                "A no-change test can use the source model as both fields.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(sourceModel == null || editedModel == null))
                if (GUILayout.Button("Validate and Export Patched Weapon .emd"))
                    Export();
            if (!string.IsNullOrEmpty(lastResult))
                EditorGUILayout.HelpBox(lastResult, MessageType.Info);
        }

        private sealed class MeshPatch
        {
            public int vertexOffset;
            public int vertexCount;
            public Vector3[] positions;
        }

        private static uint ReadU32(byte[] data, int offset)
        {
            if (offset < 0 || offset > data.Length - 4)
                throw new InvalidDataException("EMD out-of-range U32 at 0x" + offset.ToString("X"));
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
                   ((uint)data[offset + 2] << 8) | data[offset + 3];
        }

        private static ushort ReadU16(byte[] data, int offset)
        {
            if (offset < 0 || offset > data.Length - 2)
                throw new InvalidDataException("EMD out-of-range U16 at 0x" + offset.ToString("X"));
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static string ReadName(byte[] data, int offset, int length)
        {
            if (offset < 0 || offset > data.Length - length)
                throw new InvalidDataException("EMD out-of-range name.");
            int end = offset;
            while (end < offset + length && data[end] != 0) ++end;
            return Encoding.ASCII.GetString(data, offset, end - offset);
        }

        private static Mesh FindMesh(GameObject root, string childName)
        {
            Mesh found = null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(child.name, childName, StringComparison.Ordinal))
                    continue;
                // For this prototype, geometry must remain in the original local frame.
                if (child.parent != root.transform ||
                    child.localPosition.sqrMagnitude > 0.0000001f ||
                    Quaternion.Angle(child.localRotation, Quaternion.identity) > 0.001f ||
                    (child.localScale - Vector3.one).sqrMagnitude > 0.0000001f)
                    throw new InvalidDataException(
                        childName + ": must be a direct child with identity local transform.");
                var skinned = child.GetComponent<SkinnedMeshRenderer>();
                var filter = child.GetComponent<MeshFilter>();
                Mesh mesh = skinned != null ? skinned.sharedMesh :
                    filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    throw new InvalidDataException("Missing Mesh on child " + childName);
                if (found != null)
                    throw new InvalidDataException("Duplicate named mesh child " + childName);
                found = mesh;
            }
            if (found == null)
                throw new InvalidDataException("Cannot find mesh child " + childName);
            return found;
        }

        private static void VerifyTopology(string name, Mesh reference, Mesh edited, int vertexCount,
            int indexCount)
        {
            if (!edited.isReadable || !reference.isReadable)
                throw new InvalidDataException(name + ": mesh Read/Write must be enabled.");
            if (reference.vertexCount != vertexCount || edited.vertexCount != vertexCount)
                throw new InvalidDataException(name + ": vertex count differs from the original EMD.");
            if (reference.subMeshCount != 1 || edited.subMeshCount != 1)
                throw new InvalidDataException(name + ": only one triangle submesh is supported.");
            int[] a = reference.triangles, b = edited.triangles;
            if (a.Length != indexCount || b.Length != a.Length)
                throw new InvalidDataException(name + ": triangle index count differs.");
            for (int i = 0; i < a.Length; ++i)
                if (a[i] != b[i])
                    throw new InvalidDataException(name + ": topology/index order differs at #" + i);
            // Editing skin weights is unsupported. Preserve source EMD weights unchanged.
            BoneWeight[] wa = reference.boneWeights, wb = edited.boneWeights;
            if (wa.Length != wb.Length)
                throw new InvalidDataException(name + ": source/edited skin weight arrays differ.");
            for (int i = 0; i < wa.Length; ++i)
                if (wa[i].boneIndex0 != wb[i].boneIndex0 ||
                    wa[i].boneIndex1 != wb[i].boneIndex1 ||
                    wa[i].boneIndex2 != wb[i].boneIndex2 ||
                    wa[i].boneIndex3 != wb[i].boneIndex3 ||
                    wa[i].weight0 != wb[i].weight0 ||
                    wa[i].weight1 != wb[i].weight1 ||
                    wa[i].weight2 != wb[i].weight2 ||
                    wa[i].weight3 != wb[i].weight3)
                    throw new InvalidDataException(name + ": edited skin weights differ at #" + i);
        }

        private static void WriteFloatBigEndian(byte[] data, int offset, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidDataException("Cannot export nonfinite EMD vertex coordinate.");
            byte[] bits = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bits);
            Buffer.BlockCopy(bits, 0, data, offset, 4);
        }

        private void Export()
        {
            try
            {
                string sourcePath = AssetDatabase.GetAssetPath(sourceModel);
                if (string.IsNullOrEmpty(sourcePath) ||
                    !sourcePath.EndsWith(".emd", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Choose the imported source .emd asset (not its scene instance).");
                if (!Regex.IsMatch(Path.GetFileNameWithoutExtension(sourcePath),
                        @"_W[0-9]{2,}$", RegexOptions.IgnoreCase))
                    throw new InvalidDataException(
                        "This prototype only exports existing _Wxx weapon EMD assets.");
                if (EmdImporter.IsStageEmdPath(sourcePath))
                    throw new InvalidDataException("STG EMDs are not supported by this prototype.");

                var importer = AssetImporter.GetAtPath(sourcePath) as EmdImporter;
                if (importer == null) throw new InvalidDataException("Source uses a different EMD importer.");
                var serialized = new SerializedObject(importer);
                float scale = serialized.FindProperty("modelScale").floatValue;
                bool swap = serialized.FindProperty("swapYAndZ").boolValue;
                if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
                    throw new InvalidDataException("Invalid source model scale.");

                byte[] original = File.ReadAllBytes(sourcePath);
                if (original.Length < 0xB0)
                    throw new InvalidDataException("Source EMD header is too small.");
                int polygons = checked((int)ReadU32(original, 0x24));
                int polygonOffset = checked((int)ReadU32(original, 0x4C));
                int uvOffset = checked((int)ReadU32(original, 0x5C));
                if (polygons < 1 || polygons > 256 ||
                    polygonOffset < 0xB0 || uvOffset <= polygonOffset ||
                    uvOffset > original.Length)
                    throw new InvalidDataException("Unsupported original EMD section layout.");

                var patches = new List<MeshPatch>();
                int cursor = polygonOffset;
                for (int i = 0; i < polygons; ++i)
                {
                    int size = checked((int)ReadU32(original, cursor + 4));
                    long end = (long)cursor + size;
                    if (size < 0x1D0 || end > uvOffset)
                        throw new InvalidDataException("Unsupported polygon block #" + i);
                    string polyName = ReadName(original, cursor + 16, 32);
                    if (ReadU16(original, cursor + 0x132) != 0x04)
                        throw new InvalidDataException(polyName +
                            ": only single-group type 0x0004 weapon polygons are supported.");
                    int group = cursor + 0x140;
                    while (group + 0x90 <= end && ReadU32(original, group) == 0)
                        group += 4;
                    if (group + 0x90 > end)
                        throw new InvalidDataException(polyName + ": group descriptor not found.");
                    int positionRel = checked((int)ReadU32(original, group + 8));
                    int positionOffset = checked(group + positionRel);
                    int vertexCount = ReadU16(original, group + 0x88);
                    int indexCount = ReadU16(original, group + 0x8A);
                    int positionCapacity = ReadU16(original, group + 0x10);
                    if (vertexCount < 1 || indexCount < 3 || indexCount % 3 != 0 ||
                        (long)vertexCount * 12L > positionCapacity ||
                        positionOffset < group + 0x90 ||
                        (long)positionOffset + vertexCount * 12L > end)
                        throw new InvalidDataException(polyName + ": unsupported vertex/index layout.");
                    string meshName = polyName + "_" + i;
                    Mesh sourceMesh = FindMesh(sourceModel, meshName);
                    Mesh modifiedMesh = FindMesh(editedModel, meshName);
                    VerifyTopology(meshName, sourceMesh, modifiedMesh, vertexCount, indexCount);
                    patches.Add(new MeshPatch
                    {
                        vertexOffset = positionOffset,
                        vertexCount = vertexCount,
                        positions = modifiedMesh.vertices
                    });
                    cursor = checked((int)end);
                }
                if (cursor != uvOffset)
                    throw new InvalidDataException(
                        "Polygon blocks do not end at UV data start; variant unsupported.");

                // Construct output in memory; only commit after every validation succeeds.
                byte[] output = (byte[])original.Clone();
                int changedCoordinates = 0;
                int totalVertices = 0;
                foreach (MeshPatch patch in patches)
                {
                    for (int v = 0; v < patch.vertexCount; ++v)
                    {
                        Vector3 p = patch.positions[v] / scale;
                        if (swap) p = new Vector3(p.x, p.z, p.y);
                        int off = patch.vertexOffset + v * 12;
                        byte[] before = new byte[12];
                        Buffer.BlockCopy(output, off, before, 0, 12);
                        WriteFloatBigEndian(output, off, p.x);
                        WriteFloatBigEndian(output, off + 4, p.y);
                        WriteFloatBigEndian(output, off + 8, p.z);
                        for (int k = 0; k < 12; ++k)
                            if (output[off + k] != before[k]) { changedCoordinates++; break; }
                        totalVertices++;
                    }
                }

                string defaultName = Path.GetFileNameWithoutExtension(sourcePath) + "_patched.emd";
                string dest = EditorUtility.SaveFilePanel("Save NEW patched weapon EMD",
                    Path.GetDirectoryName(Path.GetFullPath(sourcePath)), defaultName, "emd");
                if (string.IsNullOrEmpty(dest)) return;
                string absoluteSource = Path.GetFullPath(sourcePath);
                if (string.Equals(Path.GetFullPath(dest), absoluteSource,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Refusing to overwrite the original source EMD.");
                File.WriteAllBytes(dest, output);
                lastResult = "Exported " + patches.Count + " mesh groups / " + totalVertices +
                    " vertices to " + dest + ". Changed positions in " + changedCoordinates +
                    " vertices. Original EMD, bone data, UV, material and topology retained.";
                Debug.Log("EMD2Unity experimental weapon patch: " + lastResult);
            }
            catch (Exception ex)
            {
                lastResult = "Weapon patch validation failed: " + ex.Message;
                Debug.LogError("EMD2Unity weapon patch: " + ex);
                EditorUtility.DisplayDialog("Weapon EMD position patch refused", ex.Message, "OK");
            }
        }
    }
}
#endif
