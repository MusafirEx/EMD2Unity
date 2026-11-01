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
        [SerializeField] private bool exportNativeFbxAttributes = true;
        [SerializeField] private int rigidUnskinnedBoneIndex = 0;
        [SerializeField] private bool forceRigidBone = false;
        [SerializeField] private bool allowBoneCollapse = false;
        [SerializeField] private bool useExtendedReplacementRig = false;
        [SerializeField] private bool useGroupedSourceMapping = false;
        [SerializeField] private bool allowMergedDifferentTextures = false;
        [SerializeField] private int[] sourceToPolygon = new int[128];

        [Serializable]
        private sealed class ReplacementMarker
        {
            public string sourceUnityGameObject;
            public string generatedTemplatePath;
            public string[] sourceBoneNames;
            public string[] sourceMeshPaths;
            public int sourceMeshCount;
        }

        private bool HasMatchingReplacementRig(EmdDocument document)
        {
            if (!useExtendedReplacementRig || document == null ||
                originalEmd == null || fbxSource == null)
                return false;
            string templatePath = AssetDatabase.GetAssetPath(originalEmd);
            string markerPath = Path.ChangeExtension(templatePath, ".replacement.json");
            if (!File.Exists(markerPath)) return false;
            try
            {
                ReplacementMarker marker =
                    JsonUtility.FromJson<ReplacementMarker>(File.ReadAllText(markerPath));
                if (marker == null || marker.generatedTemplatePath != templatePath ||
                    marker.sourceUnityGameObject != fbxSource.name ||
                    marker.sourceBoneNames == null || marker.sourceBoneNames.Length == 0)
                    return false;
                var names = new HashSet<string>(document.Bones.ConvertAll(b => b.Name),
                    StringComparer.Ordinal);
                if (!Array.TrueForAll(marker.sourceBoneNames, name => names.Contains(name)))
                    return false;
                List<InputSlot> current = GetInputSlots(fbxSource);
                if (marker.sourceMeshPaths == null ||
                    marker.sourceMeshCount != current.Count ||
                    marker.sourceMeshPaths.Length != current.Count)
                    return false;
                for (int i = 0; i < current.Count; ++i)
                    if (marker.sourceMeshPaths[i] != current[i].path)
                        return false;
                return true;
            }
            catch (Exception) { return false; }
        }
        [SerializeField] private bool keepOriginalPackedAuxiliary = true;
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
            internal string path;
            internal Transform transform;
            internal Mesh mesh;
            internal int subMesh;
            internal SkinnedMeshRenderer skinned;
            internal Renderer renderer;
        }

        [MenuItem("Tools/EMD2Unity/Direct FBX to Weapon EMD (Experimental)")]
        public static void Open() =>
            GetWindow<EmdDirectFbxExportWindow>("Direct FBX to EMD");

        // Option B bridge: generated extended-bone EMD template and any
        // Scene/GameObject source (not necessarily an FBX asset).
        // Pre-map slot order without changing either input asset.
        internal static void ConfigureReplacement(GameObject generatedTemplate, GameObject source)
        {
            var window = GetWindow<EmdDirectFbxExportWindow>("Direct FBX to EMD");
            window.originalEmd = generatedTemplate;
            window.fbxSource = source;
            window.exportNativeFbxAttributes = true;
            window.keepOriginalPackedAuxiliary = true;
            window.forceRigidBone = false;
            window.allowBoneCollapse = false;
            window.useExtendedReplacementRig = true;
            window.exportFbxUv0 = true;
            window.exportFbxColors = false;
            window.bakeRootTransform = false;
            try
            {
                var originals = GetOriginalSlots(generatedTemplate);
                var inputs = GetInputSlots(source);
                if (inputs.Count > originals.Count)
                {
                    window.useGroupedSourceMapping = true;
                    window.MapSourcesInOrder(originals, inputs);
                }
                else
                {
                    window.useGroupedSourceMapping = false;
                    window.MapInOrder(originals, inputs);
                }
                EmdDocument rig = EmdParser.Parse(
                    File.ReadAllBytes(AssetDatabase.GetAssetPath(generatedTemplate)));
                var skinned = source.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skinned != null && skinned.rootBone != null)
                {
                    int target = rig.Bones.FindIndex(b => b.Name == skinned.rootBone.name);
                    if (target >= 0) window.rigidUnskinnedBoneIndex = target;
                }
                window.report = "GameObject replacement mapping prepared: " + inputs.Count +
                    " source meshes, " + originals.Count + " target polygon slots. " +
                    (window.useGroupedSourceMapping
                        ? "Assign ALL source meshes to a destination slot; unmapped " +
                          "parts stop export. Merging different textures is refused " +
                          "without an explicit unsafe test override. "
                        : "Verify the mappings. ") +
                    "Source DDS/EMAT textures are NOT embedded by this exporter.";
            }
            catch (Exception e)
            {
                window.report = "Replacement mapping unavailable: " + e.Message +
                    ". Check original template import and manually assign meshes.";
            }
            window.Show();
            window.Focus();
        }

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
                        path = HierarchyPath(input.transform, child),
                        transform = child,
                        mesh = mesh,
                        subMesh = submesh,
                        skinned = skinned,
                        renderer = skinned != null ? (Renderer)skinned :
                            child.GetComponent<MeshRenderer>()
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
            EditorGUILayout.LabelField("Direct GameObject / FBX → EMD Export", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "ONE-STEP GEOMETRY EXPORT: Unity GameObject/FBX mesh + UV0 → NEW weapon .emd. " +
                "An original or alpha extended-rig _Wxx.emd template supplies bone, material, " +
                "opaque vertex-data and binary-layout template. " +
                "No intermediate mesh assets, scene copies, distance analysis or " +
                "vertex-distance acceptance tests are required. " +
                "Existing EMD polygon/material slot count cannot increase.",
                MessageType.Info);

            var selectedEmd = (GameObject)EditorGUILayout.ObjectField(
                "Original Weapon .emd (template)", originalEmd, typeof(GameObject), false);
            var selectedFbx = (GameObject)EditorGUILayout.ObjectField(
                "Unity GameObject / FBX", fbxSource, typeof(GameObject), true);
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
            exportNativeFbxAttributes = EditorGUILayout.ToggleLeft(
                "Encode FBX normals and skin weights DIRECTLY (no nearest donor)",
                exportNativeFbxAttributes);
            if (exportNativeFbxAttributes)
                EditorGUILayout.HelpBox(
                    "FBX skin weights are encoded to the source EMD bone IDs. " +
                    "IMPORTANT: the original 12-byte secondary vertex stream is NOT plain " +
                    "float32 normals; preserve its native byte layout until the game " +
                    "normal/tangent packing is reverse engineered. " +
                    "FBX bone names must match EMD bone names.",
                    MessageType.Info);
            if (exportNativeFbxAttributes)
            {
                forceRigidBone = EditorGUILayout.ToggleLeft(
                    "Force rigid binding (ignore FBX armature/weights)", forceRigidBone);
                keepOriginalPackedAuxiliary = EditorGUILayout.ToggleLeft(
                    "Preserve game-packed auxiliary stream (recommended; donor fallback)",
                    keepOriginalPackedAuxiliary);
                if (!keepOriginalPackedAuxiliary)
                    EditorGUILayout.HelpBox(
                        "Experimental float32 normals corrupt the native packed 12-byte stream " +
                        "in the tested source. Use ONLY for investigating the packing format.",
                        MessageType.Error);
                allowBoneCollapse = EditorGUILayout.ToggleLeft(
                    "Allow missing original bone influences (unsafe, may stretch mesh)",
                    allowBoneCollapse);
                useGroupedSourceMapping = EditorGUILayout.ToggleLeft(
                    "Map multiple source meshes into original EMD polygon slots",
                    useGroupedSourceMapping);
                if (useGroupedSourceMapping)
                {
                    allowMergedDifferentTextures = EditorGUILayout.ToggleLeft(
                        "UNSAFE: allow different source textures to share ONE EMD slot",
                        allowMergedDifferentTextures);
                    EditorGUILayout.HelpBox(
                        "Each source must be assigned to exactly one original polygon. " +
                        "Multiple sources in one slot have their geometry merged, " +
                        "but EMD supports only ONE material slot/texture mapping there. " +
                        "Use a compatible texture atlas to preserve different textures; " +
                        "the unsafe override is for geometry-only game testing.",
                        MessageType.Warning);
                }
                useExtendedReplacementRig = EditorGUILayout.ToggleLeft(
                    "Use generated extended-rig GameObject replacement manifest",
                    useExtendedReplacementRig);
                if (useExtendedReplacementRig)
                    EditorGUILayout.HelpBox(
                        "Requires the exact generated *_W00.emd template and its " +
                        "*.replacement.json for the selected GameObject. " +
                        "Old mesh bone influences may be replaced intentionally; " +
                        "actual source WP bones must still match the extended EMD " +
                        "skeleton. Game loading and animation remain unverified.",
                        MessageType.Warning);
            }
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
                    if (exportNativeFbxAttributes)
                    {
                        EmdDocument skeleton = EmdParser.Parse(
                            File.ReadAllBytes(AssetDatabase.GetAssetPath(originalEmd)));
                        if (skeleton.Bones.Count < 1)
                            throw new InvalidDataException(
                                "Original EMD contains no bones; native FBX bone export is unavailable.");
                        string[] names = new string[skeleton.Bones.Count];
                        for (int i = 0; i < names.Length; ++i)
                            names[i] = i + ": " + skeleton.Bones[i].Name +
                                " (node " + skeleton.Bones[i].NodeID + ")";
                        rigidUnskinnedBoneIndex = EditorGUILayout.Popup(
                            "Rigid bone for unskinned FBX",
                            Mathf.Clamp(rigidUnskinnedBoneIndex, 0, names.Length - 1), names);
                    }
                    if (inputs.Count == 0)
                        throw new InvalidDataException(
                            "FBX contains no triangle meshes (MeshFilter/SkinnedMeshRenderer).");
                    if (useGroupedSourceMapping)
                    {
                        var slots = new string[originals.Count + 1];
                        slots[0] = "(UNASSIGNED — blocks export)";
                        for (int i = 0; i < originals.Count; ++i)
                            slots[i + 1] = "Slot " + i + " — " + originals[i].name;
                        EditorGUILayout.LabelField("Every source mesh → original EMD polygon",
                            EditorStyles.boldLabel);
                        int unmapped = 0;
                        for (int i = 0; i < inputs.Count; ++i)
                        {
                            if (i >= sourceToPolygon.Length)
                                throw new InvalidDataException(
                                    "More than " + sourceToPolygon.Length + " input submeshes.");
                            sourceToPolygon[i] = EditorGUILayout.Popup(
                                inputs[i].label,
                                Mathf.Clamp(sourceToPolygon[i], 0, originals.Count),
                                slots);
                            if (sourceToPolygon[i] == 0) unmapped++;
                        }
                        if (unmapped > 0)
                            EditorGUILayout.HelpBox(unmapped +
                                " source mesh(es) still UNASSIGNED. Export will refuse " +
                                "until every source has an explicit destination.",
                                MessageType.Warning);
                        if (GUILayout.Button("Initialize first slots in order (others UNASSIGNED)"))
                            MapSourcesInOrder(originals, inputs);
                    }
                    else
                    {
                        var labels = new string[inputs.Count + 1];
                        labels[0] = "(Keep original EMD geometry)";
                        for (int i = 0; i < inputs.Count; i++)
                            labels[i + 1] = inputs[i].label;
                        EditorGUILayout.LabelField("EMD polygon / material slot ← source geometry",
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
            sourceToPolygon = new int[128];
            if (originalEmd == null || fbxSource == null || !IsOriginalWeapon(originalEmd))
                return;
            try
            {
                var slots = GetOriginalSlots(originalEmd);
                var inputs = GetInputSlots(fbxSource);
                if (inputs.Count > slots.Count && useExtendedReplacementRig)
                {
                    useGroupedSourceMapping = true;
                    MapSourcesInOrder(slots, inputs);
                }
                else
                {
                    useGroupedSourceMapping = false;
                    AutoMap(slots, inputs);
                }
            }
            catch (Exception e)
            {
                report = "Automatic mapping unavailable: " + e.Message;
            }
        }

        private void MapSourcesInOrder(List<OriginalSlot> originals, List<InputSlot> inputs)
        {
            sourceToPolygon = new int[128];
            for (int i = 0; i < inputs.Count && i < originals.Count; ++i)
                sourceToPolygon[i] = i + 1;
            report = "Source-to-EMD mapping initialized. " +
                Math.Min(originals.Count, inputs.Count) + " mapped in order, " +
                Math.Max(0, inputs.Count - originals.Count) +
                " sources remain UNASSIGNED and must be mapped manually. " +
                "No geometry will be dropped silently.";
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
            bool requireUv, bool requireColors, bool nativeAttributes,
            EmdDocument emd, int rigidBoneIndex, bool forceRigid)
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
            Vector3[] inputNormals = nativeAttributes ? source.normals : new Vector3[0];
            BoneWeight[] originalFbxWeights = nativeAttributes ? source.boneWeights : new BoneWeight[0];
            bool hasSkin = nativeAttributes && !forceRigid &&
                originalFbxWeights.Length == inputVertices.Length;
            int[] fbxtoEmd = null;
            if (hasSkin)
            {
                if (from.skinned == null || from.skinned.bones == null ||
                    from.skinned.bones.Length == 0)
                    throw new InvalidDataException(from.label +
                        ": FBX has bone weights but SkinnedMeshRenderer.bones is missing. " +
                        "Use a rigged FBX with named bones or remove skin weights for rigid binding.");
                Transform[] fbxBones = from.skinned.bones;
                fbxtoEmd = new int[fbxBones.Length];
                for (int i = 0; i < fbxtoEmd.Length; ++i)
                {
                    fbxtoEmd[i] = -1;
                    if (fbxBones[i] == null) continue;
                    string name = fbxBones[i].name;
                    for (int k = 0; k < emd.Bones.Count; ++k)
                    {
                        if (string.Equals(name, emd.Bones[k].Name,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            if (fbxtoEmd[i] >= 0)
                                throw new InvalidDataException(from.label +
                                    ": duplicated original bone name: " + name);
                            fbxtoEmd[i] = k;
                        }
                    }
                }
            }
            if (nativeAttributes && !hasSkin &&
                (rigidBoneIndex < 0 || rigidBoneIndex >= emd.Bones.Count))
                throw new InvalidDataException("Select a valid EMD rigid bone.");
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
            if (float.IsNaN(frame.determinant) ||
                float.IsInfinity(frame.determinant) ||
                Mathf.Abs(frame.determinant) < 1e-8f)
                throw new InvalidDataException(from.label + ": zero-scale FBX transformation.");
            var oldToNew = new Dictionary<int, int>();
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();
            var normals = new List<Vector3>();
            var weights = new List<BoneWeight>();
            Matrix4x4 normalTransform = frame.inverse.transpose;
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
                    if (nativeAttributes)
                    {
                        Vector3 n = inputNormals.Length == inputVertices.Length ?
                            normalTransform.MultiplyVector(inputNormals[old]) : Vector3.zero;
                        normals.Add(n);
                        BoneWeight w;
                        if (hasSkin)
                        {
                            BoneWeight fbxWeight = originalFbxWeights[old];
                            int[] ids =
                            {
                                fbxWeight.boneIndex0, fbxWeight.boneIndex1,
                                fbxWeight.boneIndex2, fbxWeight.boneIndex3
                            };
                            float[] vals =
                            {
                                fbxWeight.weight0, fbxWeight.weight1,
                                fbxWeight.weight2, fbxWeight.weight3
                            };
                            for (int k = 0; k < 4; ++k)
                            {
                                if (float.IsNaN(vals[k]) || float.IsInfinity(vals[k]) ||
                                    vals[k] < 0f)
                                    throw new InvalidDataException(from.label +
                                        ": invalid FBX skin weights at vertex " + old);
                                if (vals[k] <= 0f) { ids[k] = 0; continue; }
                                if (ids[k] < 0 || ids[k] >= fbxtoEmd.Length ||
                                    fbxtoEmd[ids[k]] < 0)
                                    throw new InvalidDataException(from.label +
                                        ": bone index " + ids[k] +
                                        " at vertex " + old +
                                        " has no matching original EMD bone name.");
                                ids[k] = fbxtoEmd[ids[k]];
                            }
                            w = new BoneWeight
                            {
                                boneIndex0 = ids[0], boneIndex1 = ids[1],
                                boneIndex2 = ids[2], boneIndex3 = ids[3],
                                weight0 = vals[0], weight1 = vals[1],
                                weight2 = vals[2], weight3 = vals[3]
                            };
                        }
                        else
                        {
                            w = new BoneWeight
                            {
                                boneIndex0 = rigidBoneIndex, weight0 = 1f,
                                boneIndex1 = rigidBoneIndex,
                                boneIndex2 = rigidBoneIndex, boneIndex3 = rigidBoneIndex
                            };
                        }
                        weights.Add(w);
                    }
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
            if (nativeAttributes)
            {
                bool missingNormals = inputNormals.Length != inputVertices.Length;
                if (!missingNormals)
                {
                    foreach (Vector3 n in normals)
                        if (!Finite(n) || n.sqrMagnitude < 1e-10f)
                        {
                            missingNormals = true;
                            break;
                        }
                }
                if (missingNormals) mesh.RecalculateNormals();
                else
                {
                    for (int i = 0; i < normals.Count; ++i)
                        normals[i] = normals[i].normalized;
                    mesh.normals = normals.ToArray();
                }
                mesh.boneWeights = weights.ToArray();
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Transform FindDirectChild(Transform root, string name)
        {
            foreach (Transform child in root)
                if (child.name == name) return child;
            throw new InvalidDataException("Original weapon clone is missing polygon child " + name);
        }

        private static string BoneNames(EmdDocument document, HashSet<int> ids)
        {
            var result = new List<string>();
            foreach (int id in ids)
                if (id >= 0 && id < document.Bones.Count)
                    result.Add(document.Bones[id].Name);
            result.Sort(StringComparer.Ordinal);
            return string.Join(", ", result.ToArray());
        }

        private static HashSet<int> ActiveBones(EmdWeight[] weights)
        {
            var result = new HashSet<int>();
            if (weights == null) return result;
            foreach (EmdWeight w in weights)
                for (int k = 0; k < 4; ++k)
                    if (w.Values[k] > 0f && w.Indices[k] >= 0)
                        result.Add(w.Indices[k]);
            return result;
        }

        private static HashSet<int> ActiveBones(BoneWeight[] weights)
        {
            var result = new HashSet<int>();
            if (weights == null) return result;
            foreach (BoneWeight w in weights)
            {
                if (w.weight0 > 0f) result.Add(w.boneIndex0);
                if (w.weight1 > 0f) result.Add(w.boneIndex1);
                if (w.weight2 > 0f) result.Add(w.boneIndex2);
                if (w.weight3 > 0f) result.Add(w.boneIndex3);
            }
            return result;
        }

        // A source GameObject may have many Renderers but the original EMD
        // can have fewer polygon/material slots. Distinct textures cannot be
        // preserved in a single EMD material slot without an atlas or new
        // material-table/EMAT writer; default to an explicit refusal.
        private static string SourceMaterialIdentity(InputSlot slot)
        {
            Renderer renderer = slot.renderer;
            if (renderer == null)
                return "(none)";
            Material[] mats = renderer.sharedMaterials;
            Material material = mats != null && slot.subMesh < mats.Length ?
                mats[slot.subMesh] : null;
            if (material == null) return "(none)";
            Texture texture = null;
            if (material.HasProperty("_MainTex"))
                texture = material.GetTexture("_MainTex");
            if (texture == null && material.HasProperty("_BaseMap"))
                texture = material.GetTexture("_BaseMap");
            if (texture == null && material.HasProperty("_BaseColorMap"))
                texture = material.GetTexture("_BaseColorMap");
            // Instance IDs are used only to compare within the current run.
            // Identical texture names are not evidence of identical content.
            return texture != null
                ? "texture:" + texture.GetInstanceID() + " (" + texture.name + ")"
                : "material:" + material.GetInstanceID() + " (" + material.name + ")";
        }

        private static Mesh MergeMappedMeshes(
            List<Mesh> pieces, string meshName,
            bool includeUV, bool includeColors, bool includeWeights)
        {
            if (pieces == null || pieces.Count == 0)
                throw new InvalidDataException("No source meshes to merge.");
            int count = 0, indices = 0;
            foreach (Mesh p in pieces)
            {
                count = checked(count + p.vertexCount);
                indices = checked(indices + p.triangles.Length);
            }
            if (count < 3 || count > 5000 || indices < 3 || indices > 32000 ||
                count > ushort.MaxValue || indices % 3 != 0)
                throw new InvalidDataException(meshName + ": combined source geometry (" +
                    count + " vertices, " + indices + " indices) exceeds current " +
                    "EMD per-polygon limits. Use fewer groups or implement a real " +
                    "new-polygon writer instead of dropping geometry.");

            var vertices = new Vector3[count];
            var triangles = new int[indices];
            var uvs = includeUV ? new Vector2[count] : null;
            var colors = includeColors ? new Color32[count] : null;
            var normals = includeWeights ? new Vector3[count] : null;
            var weights = includeWeights ? new BoneWeight[count] : null;
            int vertexOffset = 0, indexOffset = 0;
            foreach (Mesh p in pieces)
            {
                Vector3[] vv = p.vertices;
                int[] ii = p.triangles;
                Array.Copy(vv, 0, vertices, vertexOffset, vv.Length);
                if (includeUV)
                {
                    Vector2[] uv = p.uv;
                    if (uv.Length != vv.Length)
                        throw new InvalidDataException(meshName + ": source missing UV0.");
                    Array.Copy(uv, 0, uvs, vertexOffset, uv.Length);
                }
                if (includeColors)
                {
                    Color32[] color = p.colors32;
                    if (color.Length != vv.Length)
                        throw new InvalidDataException(meshName + ": source missing vertex colors.");
                    Array.Copy(color, 0, colors, vertexOffset, color.Length);
                }
                if (includeWeights)
                {
                    Vector3[] normal = p.normals;
                    BoneWeight[] bone = p.boneWeights;
                    if (normal.Length != vv.Length || bone.Length != vv.Length)
                        throw new InvalidDataException(meshName +
                            ": source normals/skin weights incomplete.");
                    Array.Copy(normal, 0, normals, vertexOffset, normal.Length);
                    Array.Copy(bone, 0, weights, vertexOffset, bone.Length);
                }
                for (int i = 0; i < ii.Length; ++i)
                {
                    if (ii[i] < 0 || ii[i] >= vv.Length)
                        throw new InvalidDataException(meshName +
                            ": invalid source triangle index.");
                    triangles[indexOffset + i] = checked(vertexOffset + ii[i]);
                }
                vertexOffset += vv.Length;
                indexOffset += ii.Length;
            }
            var combined = new Mesh { name = meshName, indexFormat = IndexFormat.UInt16 };
            combined.vertices = vertices;
            combined.triangles = triangles;
            if (includeUV) combined.uv = uvs;
            if (includeColors) combined.colors32 = colors;
            if (includeWeights)
            {
                combined.normals = normals;
                combined.boneWeights = weights;
            }
            else combined.RecalculateNormals();
            combined.RecalculateBounds();
            return combined;
        }

        private void ExportNow(List<OriginalSlot> original, List<InputSlot> inputs)
        {
            GameObject temporary = null;
            var newMeshes = new List<Mesh>();
            try
            {
                EmdDocument emd = exportNativeFbxAttributes ?
                    EmdParser.Parse(File.ReadAllBytes(
                        AssetDatabase.GetAssetPath(originalEmd))) : null;
                bool fullReplacement = HasMatchingReplacementRig(emd);
                if (useExtendedReplacementRig && !fullReplacement)
                    throw new InvalidDataException(
                        "Extended-rig export requires the exact EMD template and companion " +
                        ".replacement.json generated from this GameObject.");
                var usedInputs = new HashSet<int>();
                var replacement = new HashSet<string>();
                var summary = new StringBuilder();
                summary.AppendLine("EMD2Unity DIRECT GameObject/FBX -> EMD");
                summary.AppendLine("EMD template: " + AssetDatabase.GetAssetPath(originalEmd));
                summary.AppendLine("Source GameObject: " + fbxSource.name);
                summary.AppendLine("Source meshes: " + inputs.Count +
                    "; target EMD polygons: " + original.Count);
                summary.AppendLine("Mode: direct; no persisted intermediate Scene/mesh assets.");
                var assignedParts = new List<InputSlot>[original.Count];
                for (int i = 0; i < assignedParts.Length; ++i)
                    assignedParts[i] = new List<InputSlot>();

                if (useGroupedSourceMapping)
                {
                    if (!exportNativeFbxAttributes || emd == null)
                        throw new InvalidDataException(
                            "Grouped weapon replacement requires native bone/skin export; " +
                            "turn on Encode FBX normals and skin weights DIRECTLY.");
                    if (inputs.Count > sourceToPolygon.Length)
                        throw new InvalidDataException("Too many input slots for the " +
                            sourceToPolygon.Length + "-slot mapping buffer.");
                    for (int i = 0; i < inputs.Count; ++i)
                    {
                        int dest = sourceToPolygon[i];
                        if (dest < 1 || dest > original.Count)
                            throw new InvalidDataException(
                                "Source mesh #" + i + " '" + inputs[i].label +
                                "' is UNASSIGNED. Explicitly choose one of the original " +
                                "EMD polygon slots for EVERY mesh. Nothing is dropped.");
                        assignedParts[dest - 1].Add(inputs[i]);
                        usedInputs.Add(i);
                    }
                    if (fullReplacement)
                    {
                        for (int i = 0; i < assignedParts.Length; ++i)
                            if (assignedParts[i].Count == 0)
                                throw new InvalidDataException(
                                    "Original EMD polygon " + original[i].name +
                                    " has no replacement source mesh. Full replacement " +
                                    "requires all EMD slots to be assigned.");
                    }

                    // Original EMD materials may be shared by MULTIPLE polygon
                    // slots. Check by actual destination MaterialIndex, not
                    // polygon index, to catch unrepresentable source textures.
                    var chosenTextures = new Dictionary<int, string>();
                    for (int i = 0; i < original.Count; ++i)
                    {
                        int gameMaterialIndex = emd.Groups[i].MaterialIndex;
                        foreach (InputSlot part in assignedParts[i])
                        {
                            string id = SourceMaterialIdentity(part);
                            string previous;
                            if (chosenTextures.TryGetValue(gameMaterialIndex, out previous) &&
                                !string.Equals(previous, id, StringComparison.Ordinal))
                            {
                                string warning =
                                    "EMD material #" + gameMaterialIndex +
                                    " is shared by sources with DIFFERENT texture/material identities: '" +
                                    previous + "' versus '" + id + "' (" + part.label + "). " +
                                    "Only one game texture can be referenced by this original " +
                                    "material slot; make a shared atlas with corrected UVs " +
                                    "or implement new EMD material/EMAT slots.";
                                if (!allowMergedDifferentTextures)
                                    throw new InvalidDataException(warning +
                                        " Set UNSAFE texture merge only for geometry-only testing.");
                                summary.AppendLine("UNSAFE TEXTURE LOSS: " + warning);
                            }
                            else chosenTextures[gameMaterialIndex] = id;
                        }
                    }
                    summary.AppendLine("Grouped mapping: every input mesh assigned exactly once.");
                }
                else
                {
                    for (int i = 0; i < original.Count; ++i)
                    {
                        int assigned = mapping[i];
                        if (assigned == 0) continue;
                        if (assigned < 1 || assigned > inputs.Count)
                            throw new InvalidDataException(original[i].name +
                                ": invalid mapping index.");
                        if (!usedInputs.Add(assigned - 1))
                            throw new InvalidDataException(
                                "Same source submesh mapped twice. Use grouped mapping " +
                                "if multiple source meshes must share one EMD polygon.");
                        assignedParts[i].Add(inputs[assigned - 1]);
                    }
                    if (fullReplacement && usedInputs.Count != inputs.Count)
                        throw new InvalidDataException(
                            "Extended-rig source has " + inputs.Count + " meshes but only " +
                            usedInputs.Count + " mapped. Enable grouped mapping and " +
                            "explicitly assign EVERY source mesh; do not silently omit any.");
                }
                if (usedInputs.Count == 0)
                    throw new InvalidDataException("Map at least one source mesh to an EMD polygon.");

                // Each source is transformed, compacted and mapped to the
                // extended EMD skeleton independently before concatenation.
                // The resulting combined BoneWeights use EMD-global bone IDs,
                // so local source skeleton index tables never collide.
                var sourceMeshes = new Mesh[original.Count];
                for (int i = 0; i < original.Count; ++i)
                {
                    if (assignedParts[i].Count == 0) continue;
                    var pieces = new List<Mesh>();
                    foreach (InputSlot input in assignedParts[i])
                    {
                        Mesh piece = BuildMappedMesh(
                            input, fbxSource, bakeRootTransform, exportFbxUv0,
                            exportFbxColors, exportNativeFbxAttributes,
                            emd, rigidUnskinnedBoneIndex, forceRigidBone);
                        newMeshes.Add(piece);
                        pieces.Add(piece);
                        summary.AppendLine("  " + original[i].name + " <= " + input.label +
                            ": " + piece.vertexCount + " vertices, " +
                            piece.triangles.Length / 3 + " triangles; material=" +
                            SourceMaterialIdentity(input));
                    }
                    Mesh created = pieces.Count == 1 ? pieces[0] :
                        MergeMappedMeshes(pieces, original[i].name + "_GameObjectMerged",
                            exportFbxUv0, exportFbxColors, exportNativeFbxAttributes);
                    if (pieces.Count > 1) newMeshes.Add(created);
                    if (exportNativeFbxAttributes)
                    {
                        if (emd == null || i >= emd.Groups.Count)
                            throw new InvalidDataException("Cannot inspect source polygon bone influences.");
                        HashSet<int> originalBones = ActiveBones(emd.Groups[i].Weights);
                        HashSet<int> exportedBones = ActiveBones(created.boneWeights);
                        var missing = new HashSet<int>(originalBones);
                        missing.ExceptWith(exportedBones);
                        if (missing.Count > 0)
                        {
                            string warning = original[i].name +
                                ": original game polygon uses bones [" +
                                BoneNames(emd, originalBones) + "] but replacement uses [" +
                                BoneNames(emd, exportedBones) + "]. Missing old influences [" +
                                BoneNames(emd, missing) + "].";
                            if (!fullReplacement && !allowBoneCollapse)
                                throw new InvalidDataException(warning +
                                    " This can stretch or detach geometry in game. " +
                                    "Skin source meshes to matching EMD bones, or explicitly " +
                                    "enable the unsafe old-influence collapse override.");
                            summary.AppendLine((fullReplacement
                                ? "EXTENDED RIG (intentional original influence replacement): "
                                : "UNSAFE BONE COLLAPSE: ") + warning);
                        }
                    }
                    sourceMeshes[i] = created;
                    replacement.Add(original[i].name);
                    summary.AppendLine("COMBINED " + original[i].name +
                        ": " + assignedParts[i].Count + " source part(s), " +
                        created.vertexCount + " vertices, " +
                        created.triangles.Length / 3 + " triangles.");
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
                summary.AppendLine(exportNativeFbxAttributes
                    ? "Native FBX geometry and skin weights encoded into EMD. " +
                      (keepOriginalPackedAuxiliary
                          ? "The 12-byte game-packed auxiliary stream uses a conservative " +
                            "nearest-source-vertex compatibility fallback; FBX normal float32 " +
                            "is NOT emitted."
                          : "WARNING: experimental float32 auxiliary data may corrupt game rendering.") +
                      " FBX rig bone names map to existing EMD skeleton."
                    : "Legacy mode: original EMD per-vertex normal/skin attributes " +
                      "transferred from nearest original vertex.");
                string result = EmdWeaponTopologyRebuildWindow.ExportDirectFromFbx(
                    originalEmd, temporary, replacement, exportFbxUv0,
                    exportFbxColors, exportNativeFbxAttributes,
                    keepOriginalPackedAuxiliary);
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
