// Direct FBX / Blender mesh -> weapon EMD export, no persistent intermediate copy.
// Uses a genuine existing _Wxx.emd as the binary skeleton/material template.
// Geometry/UV0/optional color is from FBX, unparsed attributes from source donor.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.EMD2Unity
{
    public sealed class EmdDirectFbxExportWindow : EditorWindow
    {
        [SerializeField] private GameObject originalEmd;
        [SerializeField] private GameObject fbxSource;
        [SerializeField] private bool bakeRootTransform = true;
        [SerializeField] private bool exportFbxUv0 = true;
        [SerializeField] private bool exportFbxColors = false;
        [SerializeField] private int[] mapping = new int[32];
        private Vector2 scrollPosition;
        private string report;

        private sealed class OriginalSlot
        {
            internal string name;
            internal Transform transform;
            internal Mesh mesh;
        }
        private sealed class InputSlot
        {
            internal string label;
            internal Transform transform;
            internal Mesh mesh;
            internal int subMesh;
        }

        [MenuItem("Tools/EMD2Unity/Direct FBX to Weapon EMD (Experimental)")]
        public static void Open() =>
            GetWindow<EmdDirectFbxExportWindow>("Direct FBX to EMD");

        private static int GetTrailingIndex(string name)
        {
            int at = name.LastIndexOf('_');
            if (at < 0) return -1;
            int index;
            return int.TryParse(name.Substring(at + 1), out index) ? index : -1;
        }

        private static List<OriginalSlot> GetOriginalSlots(GameObject original)
        {
            var list = new List<OriginalSlot>();
            foreach (Transform child in original.GetComponentsInChildren<Transform>(true))
            {
                if (child.parent != original.transform) continue;
                var skinned = child.GetComponent<SkinnedMeshRenderer>();
                var filter = child.GetComponent<MeshFilter>();
                Mesh mesh = skinned != null ? skinned.sharedMesh :
                    filter != null ? filter.sharedMesh : null;
                if (mesh != null)
                    list.Add(new OriginalSlot
                    {
                        name = child.name,
                        transform = child,
                        mesh = mesh
                    });
            }
            list.Sort((a, b) =>
                GetTrailingIndex(a.name).CompareTo(GetTrailingIndex(b.name)));
            if (list.Count == 0 || list.Count > 32)
                throw new InvalidDataException("The source EMD requires 1–32 existing polygon slots.");
            for (int i = 0; i < list.Count; ++i)
                if (GetTrailingIndex(list[i].name) != i)
                    throw new InvalidDataException(
                        "The source EMD mesh suffix must be contiguous (_0, _1, ...).");
            return list;
        }

        private static string HierarchyPath(Transform root, Transform selected)
        {
            var nodes = new List<string>();
            Transform node = selected;
            while (node != null && node != root)
            {
                nodes.Add(node.name);
                node = node.parent;
            }
            if (node != root) return selected.name;
            nodes.Reverse();
            return nodes.Count == 0 ? root.name : string.Join("/", nodes.ToArray());
        }

        private static List<InputSlot> GetInputSlots(GameObject input)
        {
            var list = new List<InputSlot>();
            foreach (Transform child in input.GetComponentsInChildren<Transform>(true))
            {
                var skinned = child.GetComponent<SkinnedMeshRenderer>();
                var filter = child.GetComponent<MeshFilter>();
                Mesh mesh = skinned != null ? skinned.sharedMesh :
                    filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles ||
                        mesh.GetIndexCount(submesh) < 3)
                        continue;
                    list.Add(new InputSlot
                    {
                        label = HierarchyPath(input.transform, child) +
                            " [material slot " + submesh + "; mesh verts " + mesh.vertexCount + "]",
                        transform = child,
                        mesh = mesh,
                        subMesh = submesh
                    });
                }
            }
            return list;
        }

        private static bool IsOriginalWeapon(GameObject go)
        {
            string path = AssetDatabase.GetAssetPath(go);
            return !string.IsNullOrEmpty(path) &&
                Regex.IsMatch(Path.GetFileName(path),
                    @"_W[0-9]{2,}\.emd$", RegexOptions.IgnoreCase) &&
                AssetImporter.GetAtPath(path) is EmdImporter;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Direct Blender / FBX → EMD Export", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "ONE-STEP EXPORT: FBX geometry + UV0 → NEW weapon .emd. " +
                "The ORIGINAL _Wxx.emd is required as the PS3 skeleton, material, " +
                "opaque vertex-data and binary-layout template. " +
                "No intermediate mesh assets, scene copies, distance analysis or " +
                "vertex-distance acceptance tests are required. " +
                "Existing EMD polygon/material slot count cannot increase.",
                MessageType.Info);

            var selectedEmd = (GameObject)EditorGUILayout.ObjectField(
                "Original Weapon .emd (template)", originalEmd, typeof(GameObject), false);
            var selectedFbx = (GameObject)EditorGUILayout.ObjectField(
                "FBX or Blender Model", fbxSource, typeof(GameObject), true);
            if (selectedEmd != originalEmd || selectedFbx != fbxSource)
            {
                originalEmd = selectedEmd;
                fbxSource = selectedFbx;
                ResetMapping();
            }
            bakeRootTransform = EditorGUILayout.ToggleLeft(
                "Bake FBX root transform (including scale, position and rotation)",
                bakeRootTransform);
            exportFbxUv0 = EditorGUILayout.ToggleLeft(
                "Export FBX UV0 to EMD (recommended)", exportFbxUv0);
            exportFbxColors = EditorGUILayout.ToggleLeft(
                "Export FBX vertex colors (only if provided)", exportFbxColors);
            EditorGUILayout.HelpBox(
                "A single FBX mesh with multiple material submeshes is treated as " +
                "multiple selectable geometry entries. Assign FBX entries to existing " +
                "EMD material slots. Unassigned EMD slots retain their original geometry.",
                MessageType.None);
            if (originalEmd != null && fbxSource != null)
            {
                try
                {
                    if (!IsOriginalWeapon(originalEmd))
                        throw new InvalidDataException(
                            "Select an imported original _Wxx.emd, not the FBX or a scene copy.");
                    List<OriginalSlot> originals = GetOriginalSlots(originalEmd);
                    List<InputSlot> inputs = GetInputSlots(fbxSource);
                    if (inputs.Count == 0)
                        throw new InvalidDataException(
                            "FBX contains no triangle meshes (MeshFilter/SkinnedMeshRenderer).");
                    var labels = new string[inputs.Count + 1];
                    labels[0] = "(Keep original EMD geometry)";
                    for (int i = 0; i < inputs.Count; i++)
                        labels[i + 1] = inputs[i].label;
                    EditorGUILayout.LabelField("EMD polygon / material slot ← FBX geometry",
                        EditorStyles.boldLabel);
                    for (int i = 0; i < originals.Count; i++)
                        mapping[i] = EditorGUILayout.Popup(
                            originals[i].name, Mathf.Clamp(mapping[i], 0, inputs.Count), labels);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Auto-map meshes"))
                            AutoMap(originals, inputs);
                        if (GUILayout.Button("Map in order"))
                            MapInOrder(originals, inputs);
                    }
                    EditorGUILayout.Space();
                    if (GUILayout.Button("EXPORT DIRECT FBX → NEW .EMD", GUILayout.Height(34)))
                        ExportNow(originals, inputs);
                }
                catch (Exception ex)
                {
                    EditorGUILayout.HelpBox(ex.Message, MessageType.Error);
                }
            }
            if (!string.IsNullOrEmpty(report))
            {
                if (GUILayout.Button("Copy Export Report"))
                    EditorGUIUtility.systemCopyBuffer = report;
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private void ResetMapping()
        {
            mapping = new int[32];
            if (originalEmd == null || fbxSource == null || !IsOriginalWeapon(originalEmd))
                return;
            try
            {
                var slots = GetOriginalSlots(originalEmd);
                var inputs = GetInputSlots(fbxSource);
                AutoMap(slots, inputs);
            }
            catch (Exception e)
            {
                report = "Automatic mapping unavailable: " + e.Message;
            }
        }

        private void MapInOrder(List<OriginalSlot> original, List<InputSlot> inputs)
        {
            Array.Clear(mapping, 0, mapping.Length);
            for (int i = 0; i < original.Count && i < inputs.Count; ++i)
                mapping[i] = i + 1;
            report = "Mapped " + Math.Min(original.Count, inputs.Count) +
                " FBX mesh/submesh entries to original EMD slots in order.";
        }

        private void AutoMap(List<OriginalSlot> original, List<InputSlot> inputs)
        {
            Array.Clear(mapping, 0, mapping.Length);
            var reserved = new HashSet<int>();
            for (int i = 0; i < original.Count; i++)
                for (int j = 0; j < inputs.Count; j++)
                {
                    if (reserved.Contains(j)) continue;
                    bool exact = string.Equals(original[i].name, inputs[j].transform.name,
                        StringComparison.OrdinalIgnoreCase);
                    if (exact && inputs[j].subMesh == 0)
                    {
                        mapping[i] = j + 1;
                        reserved.Add(j);
                        break;
                    }
                }
            if (inputs.Count == 1 && mapping[0] == 0)
                mapping[0] = 1;
            else if (inputs.Count == original.Count)
            {
                for (int i = 0; i < original.Count; i++)
                {
                    if (mapping[i] != 0) continue;
                    for (int j = 0; j < inputs.Count; j++)
                    {
                        if (reserved.Contains(j)) continue;
                        mapping[i] = j + 1;
                        reserved.Add(j);
                        break;
                    }
                }
            }
            report = "Mesh slots mapped automatically. Edit any dropdown before exporting.";
        }

        private static bool Finite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
            !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
            !float.IsNaN(v.z) && !float.IsInfinity(v.z);

        private static Mesh BuildMappedMesh(
            InputSlot from, GameObject root, bool bakeRoot,
            bool requireUv, bool requireColors)
        {
            Mesh source = from.mesh;
            if (!source.isReadable)
                throw new InvalidDataException(from.label +
                    ": FBX mesh Read/Write Enabled must be ON in Model import settings.");
            int[] inputIndices = source.GetIndices(from.subMesh);
            if (inputIndices.Length < 3 || inputIndices.Length % 3 != 0)
                throw new InvalidDataException(from.label +
                    ": FBX triangle count must be a nonzero multiple of 3.");

            Vector3[] inputVertices = source.vertices;
            Vector2[] inputUV = source.uv;
            Color32[] inputColors = source.colors32;
            if (requireUv && inputUV.Length != inputVertices.Length)
                throw new InvalidDataException(from.label +
                    ": missing FBX UV0 on some/all vertices. " +
                    "Uncheck Export FBX UV0 to use original EMD donor UV instead.");
            if (requireColors && inputColors.Length != inputVertices.Length)
                throw new InvalidDataException(from.label +
                    ": vertex colors missing. Uncheck Export FBX vertex colors.");
            Matrix4x4 frame = bakeRoot ?
                from.transform.localToWorldMatrix :
                root.transform.worldToLocalMatrix * from.transform.localToWorldMatrix;
            if (!float.IsNaN(frame.determinant) &&
                !float.IsInfinity(frame.determinant) &&
                Mathf.Abs(frame.determinant) < 1e-8f)
                throw new InvalidDataException(from.label + ": zero-scale FBX transformation.");
            var oldToNew = new Dictionary<int, int>();
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();
            int[] indices = new int[inputIndices.Length];
            for (int i = 0; i < inputIndices.Length; i++)
            {
                int old = inputIndices[i];
                if (old < 0 || old >= inputVertices.Length)
                    throw new InvalidDataException(from.label + ": invalid FBX vertex index.");
                int mapped;
                if (!oldToNew.TryGetValue(old, out mapped))
                {
                    mapped = positions.Count;
                    oldToNew.Add(old, mapped);
                    Vector3 pos = frame.MultiplyPoint3x4(inputVertices[old]);
                    if (!Finite(pos))
                        throw new InvalidDataException(from.label + ": nonfinite FBX vertex position.");
                    positions.Add(pos);
                    if (requireUv) uvs.Add(inputUV[old]);
                    if (requireColors) colors.Add(inputColors[old]);
                }
                indices[i] = mapped;
            }
            if (frame.determinant < 0f)
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int swap = indices[i + 1];
                    indices[i + 1] = indices[i + 2];
                    indices[i + 2] = swap;
                }
            if (positions.Count < 3 || positions.Count > 5000 || indices.Length > 32000)
                throw new InvalidDataException(from.label +
                    ": selected submesh exceeds EMD exporter 3–5000 vertices / 32000 indices.");
            var mesh = new Mesh { name = "EMD2Unity_Direct_" + from.transform.name };
            mesh.indexFormat = IndexFormat.UInt16;
            mesh.vertices = positions.ToArray();
            mesh.triangles = indices;
            if (requireUv) mesh.uv = uvs.ToArray();
            if (requireColors) mesh.colors32 = colors.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Transform FindDirectChild(Transform root, string name)
        {
            foreach (Transform child in root)
                if (child.name == name) return child;
            throw new InvalidDataException("Original weapon clone is missing polygon child " + name);
        }

        private void ExportNow(List<OriginalSlot> original, List<InputSlot> inputs)
        {
            GameObject temporary = null;
            var newMeshes = new List<Mesh>();
            try
            {
                var usedInputs = new HashSet<int>();
                var replacement = new HashSet<string>();
                var summary = new StringBuilder();
                summary.AppendLine("EMD2Unity DIRECT FBX → EMD");
                summary.AppendLine("EMD template: " + AssetDatabase.GetAssetPath(originalEmd));
                summary.AppendLine("FBX root: " + fbxSource.name);
                summary.AppendLine("Mode: direct; no persisted scene or mesh copy.");
                for (int i = 0; i < original.Count; i++)
                {
                    int assigned = mapping[i];
                    if (assigned == 0) continue;
                    if (assigned < 1 || assigned > inputs.Count)
                        throw new InvalidDataException(original[i].name + ": invalid mapping index.");
                    if (!usedInputs.Add(assigned))
                        throw new InvalidDataException(
                            "Same FBX submesh mapped twice. Create unique FBX mesh slots first.");
                }
                if (usedInputs.Count == 0)
                    throw new InvalidDataException("Map at least one FBX mesh to an EMD polygon.");

                // Build all requested meshes before creating temporary GameObjects.
                var sourceMeshes = new Mesh[original.Count];
                for (int i = 0; i < original.Count; i++)
                {
                    int assigned = mapping[i];
                    if (assigned == 0) continue;
                    InputSlot input = inputs[assigned - 1];
                    Mesh created = BuildMappedMesh(
                        input, fbxSource, bakeRootTransform, exportFbxUv0, exportFbxColors);
                    newMeshes.Add(created);
                    sourceMeshes[i] = created;
                    replacement.Add(original[i].name);
                    summary.AppendLine(original[i].name + " <= " + input.label +
                        ": " + created.vertexCount + " vertices; " +
                        created.triangles.Length / 3 + " triangles");
                }

                temporary = Instantiate(originalEmd);
                temporary.name = originalEmd.name + "_DirectFBX_Transient";
                temporary.transform.position = Vector3.zero;
                temporary.transform.rotation = Quaternion.identity;
                temporary.transform.localScale = Vector3.one;
                temporary.hideFlags = HideFlags.HideAndDontSave;
                for (int i = 0; i < original.Count; i++)
                {
                    if (sourceMeshes[i] == null) continue;
                    Transform part = FindDirectChild(temporary.transform, original[i].name);
                    var skinned = part.GetComponent<SkinnedMeshRenderer>();
                    var filter = part.GetComponent<MeshFilter>();
                    if (skinned != null) skinned.sharedMesh = sourceMeshes[i];
                    else if (filter != null) filter.sharedMesh = sourceMeshes[i];
                    else throw new InvalidDataException(
                        original[i].name + ": no MeshRenderer/MeshFilter in original EMD.");
                }
                summary.AppendLine("The game's original bone/material/opaque attributes " +
                    "are retained/transferred from nearest original EMD vertices. " +
                    "FBX armature and imported normals are not yet serialized into EMD.");
                string result = EmdWeaponTopologyRebuildWindow.ExportDirectFromFbx(
                    originalEmd, temporary, replacement, exportFbxUv0, exportFbxColors);
                report = summary + "\n" + result;
                Debug.Log(report);
            }
            catch (Exception ex)
            {
                report = "Direct FBX to EMD failed: " + ex.Message;
                Debug.LogError("EMD2Unity direct FBX exporter: " + ex);
                EditorUtility.DisplayDialog("Direct FBX to EMD", ex.Message, "OK");
            }
            finally
            {
                if (temporary != null) DestroyImmediate(temporary);
                foreach (Mesh mesh in newMeshes)
                    if (mesh != null) DestroyImmediate(mesh);
            }
        }
    }
}
#endif
