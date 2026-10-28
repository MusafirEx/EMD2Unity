// Stage 5A: prepare imported Blender/FBX meshes for the guarded EMD topology writer.
// Original EMD, FBX assets and original skinning are never modified.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.EMD2Unity
{
    public sealed class EmdWeaponFbxPreparationWindow : EditorWindow
    {
        [SerializeField] private GameObject sourceWeapon;
        [SerializeField] private GameObject fbxRoot;
        [SerializeField] private float maxDonorDistance = 1f;
        [SerializeField] private int[] selections = new int[32];
        [SerializeField] private string lastSource = "";
        [SerializeField] private int lastFbxId;
        [SerializeField] private bool copyFbxVertexColors;
        private Vector2 scroll;
        private string report = "Select an imported EMD and an FBX root, then map one or more FBX meshes.";

        private sealed class Part
        {
            public string name;
            public Mesh mesh;
            public Transform transform;
            public SkinnedMeshRenderer skinned;
            public MeshFilter filter;
        }

        [MenuItem("Tools/EMD2Unity/Weapon FBX Preparation (Experimental)")]
        public static void Open() =>
            GetWindow<EmdWeaponFbxPreparationWindow>("Weapon FBX Prep");

        private static List<Part> FindParts(GameObject root, bool directOnly)
        {
            var list = new List<Part>();
            if (root == null) return list;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (directOnly && t.parent != root.transform) continue;
                var skinned = t.GetComponent<SkinnedMeshRenderer>();
                var filter = t.GetComponent<MeshFilter>();
                Mesh m = skinned != null ? skinned.sharedMesh :
                    filter != null ? filter.sharedMesh : null;
                if (m == null) continue;
                list.Add(new Part
                {
                    name = t.name, transform = t, mesh = m,
                    skinned = skinned, filter = filter
                });
            }
            return list;
        }

        private static int TrailingIndex(string name)
        {
            int end = name.LastIndexOf('_');
            if (end < 0 || end == name.Length - 1) return -1;
            int n;
            return int.TryParse(name.Substring(end + 1), out n) ? n : -1;
        }

        private static List<Part> OrderedEmdParts(GameObject source)
        {
            var parts = FindParts(source, true);
            parts.Sort((a, b) => TrailingIndex(a.name).CompareTo(TrailingIndex(b.name)));
            for (int i = 0; i < parts.Count; i++)
            {
                if (TrailingIndex(parts[i].name) != i)
                    throw new InvalidDataException("Original EMD mesh names must use _0.._N contiguous suffixes.");
            }
            if (parts.Count < 1 || parts.Count > 32)
                throw new InvalidDataException("Expected 1–32 original EMD mesh parts.");
            return parts;
        }

        private static string RelativePath(Transform root, Transform child)
        {
            if (child == root) return root.name;
            var segments = new List<string>();
            Transform t = child;
            while (t != null && t != root)
            {
                segments.Add(t.name);
                t = t.parent;
            }
            if (t != root) return child.name;
            segments.Reverse();
            return string.Join("/", segments.ToArray());
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Blender / FBX → EMD-compatible mesh mapping", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This tool prepares a separate editable Scene weapon from an original _Wxx.emd " +
                "plus imported FBX meshes. Map one FBX mesh per original EMD polygon; unmapped " +
                "polygons stay unchanged. Nested FBX transforms are baked into the target mesh. " +
                "No source asset is edited. Skin weights are borrowed from the nearest " +
                "original EMD vertex (NOT authored from the FBX rig).",
                MessageType.Warning);
            var nextSource = (GameObject)EditorGUILayout.ObjectField(
                "Original Weapon .emd", sourceWeapon, typeof(GameObject), false);
            var nextFbx = (GameObject)EditorGUILayout.ObjectField(
                "FBX / Blender Root", fbxRoot, typeof(GameObject), true);
            if (nextSource != sourceWeapon || nextFbx != fbxRoot)
            {
                sourceWeapon = nextSource;
                fbxRoot = nextFbx;
                ResetSelections();
            }
            maxDonorDistance = EditorGUILayout.FloatField(
                "Max preview donor distance (Unity units)", maxDonorDistance);
            copyFbxVertexColors = EditorGUILayout.ToggleLeft(
                "Use FBX vertex colors when present (preview; enable colors in exporter separately)",
                copyFbxVertexColors);

            if (sourceWeapon != null && fbxRoot != null)
            {
                try
                {
                    if (!ValidSource(sourceWeapon))
                        throw new InvalidDataException("The source must be an imported _Wxx.emd asset.");
                    var emd = OrderedEmdParts(sourceWeapon);
                    var candidates = FindParts(fbxRoot, false);
                    if (candidates.Count < 1)
                        throw new InvalidDataException("No MeshFilter or SkinnedMeshRenderer meshes found under FBX root.");
                    var options = new string[candidates.Count + 1];
                    options[0] = "(Keep original EMD part)";
                    for (int j = 0; j < candidates.Count; j++)
                        options[j + 1] = RelativePath(fbxRoot.transform, candidates[j].transform) +
                            "  [" + candidates[j].mesh.vertexCount + " vertices]";
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Original EMD polygon → FBX geometry", EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                        for (int i = 0; i < emd.Count; i++)
                        {
                            selections[i] = EditorGUILayout.Popup(
                                emd[i].name + "  [" + emd[i].mesh.vertexCount + " vertices]",
                                Mathf.Clamp(selections[i], 0, candidates.Count), options);
                        }
                    EditorGUILayout.Space();
                    if (GUILayout.Button("Auto-map exact mesh names"))
                        AutoMap(emd, candidates);
                    if (GUILayout.Button("Create Prepared Weapon Scene Copy"))
                        CreatePrepared(emd, candidates);
                }
                catch (Exception e)
                {
                    EditorGUILayout.HelpBox(e.Message, MessageType.Error);
                }
            }
            EditorGUILayout.Space();
            if (GUILayout.Button("Copy Preparation Report"))
                EditorGUIUtility.systemCopyBuffer = report;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void ResetSelections()
        {
            selections = new int[32];
            lastSource = AssetDatabase.GetAssetPath(sourceWeapon);
            lastFbxId = fbxRoot != null ? fbxRoot.GetInstanceID() : 0;
        }

        private static bool ValidSource(GameObject src)
        {
            string path = AssetDatabase.GetAssetPath(src);
            return !string.IsNullOrEmpty(path) &&
                System.Text.RegularExpressions.Regex.IsMatch(
                    Path.GetFileName(path), @"_W[0-9]{2,}\.emd$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                AssetImporter.GetAtPath(path) is EmdImporter;
        }

        private void AutoMap(List<Part> emd, List<Part> fbxs)
        {
            int matches = 0;
            for (int i = 0; i < emd.Count; i++)
            {
                selections[i] = 0;
                for (int j = 0; j < fbxs.Count; j++)
                {
                    if (!string.Equals(emd[i].name, fbxs[j].name,
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                    selections[i] = j + 1;
                    matches++;
                    break;
                }
            }
            report = "Mapped " + matches + " identical mesh names. " +
                "Manually assign unmatched parts with the dropdowns; " +
                "name matching is intentionally conservative.";
        }

        private static int ClosestVertex(Vector3 position, Vector3[] original, out float distance)
        {
            int id = 0;
            float best = float.PositiveInfinity;
            for (int i = 0; i < original.Length; i++)
            {
                float delta = (position - original[i]).sqrMagnitude;
                if (delta < best) { best = delta; id = i; }
            }
            distance = Mathf.Sqrt(best);
            return id;
        }

        private void CreatePrepared(List<Part> emd, List<Part> fbxs)
        {
            GameObject instance = null;
            try
            {
                if (maxDonorDistance <= 0 || float.IsInfinity(maxDonorDistance) ||
                    float.IsNaN(maxDonorDistance))
                    throw new InvalidDataException("Max donor distance must be a finite positive number.");
                var used = new HashSet<int>();
                for (int i = 0; i < emd.Count; i++)
                {
                    int selected = selections[i];
                    if (selected == 0) continue;
                    if (selected < 1 || selected > fbxs.Count)
                        throw new InvalidDataException("Invalid FBX mesh mapping for " + emd[i].name);
                    if (!used.Add(selected))
                        throw new InvalidDataException("FBX mesh assigned to more than one EMD part. " +
                            "Duplicate it explicitly in Blender if intentional.");
                }
                if (used.Count == 0)
                    throw new InvalidDataException("Assign at least one FBX mesh to replace an EMD polygon.");

                // Build all replacements in memory first. Do not leave a partially modified
                // scene/asset when an FBX mesh fails topology, UV, or donor validation.
                var replacements = new Mesh[emd.Count];
                var summary = new StringBuilder();
                summary.AppendLine("EMD2Unity PHASE 5A FBX PREPARATION");
                summary.AppendLine("Source EMD: " + AssetDatabase.GetAssetPath(sourceWeapon));
                summary.AppendLine("FBX root: " + fbxRoot.name);
                int totalMapped = 0;
                for (int i = 0; i < emd.Count; i++)
                {
                    int selection = selections[i];
                    if (selection == 0) continue;
                    Part original = emd[i], candidate = fbxs[selection - 1];
                    Mesh fb = candidate.mesh;
                    if (!fb.isReadable)
                        throw new InvalidDataException(candidate.name +
                            ": FBX Read/Write Enabled must be ON in Unity Model import settings.");
                    if (fb.subMeshCount != 1 || fb.GetTopology(0) != MeshTopology.Triangles)
                        throw new InvalidDataException(candidate.name +
                            ": only one triangle submesh is supported. Split material slots in Blender.");
                    int[] triangles = fb.triangles;
                    Vector3[] vertex = fb.vertices;
                    Vector2[] uv = fb.uv;
                    if (vertex.Length < 3 || vertex.Length > 5000 ||
                        triangles.Length < 3 || triangles.Length > 32000 ||
                        triangles.Length % 3 != 0)
                        throw new InvalidDataException(candidate.name + ": vertex/triangle limits exceeded.");
                    if (uv == null || uv.Length != vertex.Length)
                        throw new InvalidDataException(candidate.name +
                            ": UV0 must be present for every FBX vertex.");
                    if (original.mesh == null || !original.mesh.isReadable ||
                        original.mesh.boneWeights.Length != original.mesh.vertexCount)
                        throw new InvalidDataException(original.name +
                            ": original source EMD mesh or bone weights are not readable.");
                    Matrix4x4 rootRelative =
                        fbxRoot.transform.worldToLocalMatrix * candidate.transform.localToWorldMatrix;
                    float determinant = rootRelative.determinant;
                    if (Mathf.Abs(determinant) < 1e-8f)
                        throw new InvalidDataException(candidate.name + ": zero-scale or degenerate FBX transform.");
                    var mapped = new Vector3[vertex.Length];
                    var bones = new BoneWeight[vertex.Length];
                    var colors = new Color32[vertex.Length];
                    var donorColors = original.mesh.colors32;
                    var fbColors = fb.colors32;
                    bool hasFbColors = copyFbxVertexColors && fbColors.Length == vertex.Length;
                    bool hasDonorColors = donorColors.Length == original.mesh.vertexCount;
                    BoneWeight[] originalWeights = original.mesh.boneWeights;
                    Vector3[] originalVertices = original.mesh.vertices;
                    float maximumDistance = 0f;
                    for (int v = 0; v < vertex.Length; v++)
                    {
                        mapped[v] = rootRelative.MultiplyPoint3x4(vertex[v]);
                        if (!Finite(mapped[v]))
                            throw new InvalidDataException(candidate.name + ": non-finite transformed vertex " + v);
                        float distance;
                        int donor = ClosestVertex(mapped[v], originalVertices, out distance);
                        if (distance > maxDonorDistance)
                            throw new InvalidDataException(original.name + ": transformed vertex " + v +
                                " is " + distance.ToString("G5") +
                                " Unity units from its closest original vertex. " +
                                "Cannot reliably transfer original EMD rig attributes.");
                        maximumDistance = Mathf.Max(maximumDistance, distance);
                        bones[v] = originalWeights[donor];
                        colors[v] = hasFbColors ? fbColors[v] :
                            hasDonorColors ? donorColors[donor] :
                            new Color32(255, 255, 255, 255);
                    }
                    if (determinant < 0f)
                        for (int t = 0; t < triangles.Length; t += 3)
                        {
                            int temp = triangles[t + 1];
                            triangles[t + 1] = triangles[t + 2];
                            triangles[t + 2] = temp;
                        }
                    var prepared = new Mesh { name = original.name + "_FBXPrepared" };
                    prepared.indexFormat = vertex.Length > 65535 ?
                        IndexFormat.UInt32 : IndexFormat.UInt16;
                    prepared.vertices = mapped;
                    prepared.triangles = triangles;
                    prepared.uv = uv;
                    prepared.colors32 = colors;
                    prepared.boneWeights = bones;
                    prepared.bindposes = (Matrix4x4[])original.mesh.bindposes.Clone();
                    prepared.RecalculateNormals();
                    prepared.RecalculateTangents();
                    prepared.RecalculateBounds();
                    replacements[i] = prepared;
                    totalMapped++;
                    summary.AppendLine(original.name + " <= " + candidate.name +
                        ": " + original.mesh.vertexCount + " -> " + mapped.Length + " vertices; " +
                        original.mesh.triangles.Length / 3 + " -> " + triangles.Length / 3 +
                        " triangles; max rig donor distance (Unity)=" +
                        maximumDistance.ToString("G5") +
                        (determinant < 0f ? "; mirrored transform corrected" : ""));
                }

                string folderBase = "Assets/EMD2Unity_Editable";
                if (!AssetDatabase.IsValidFolder(folderBase))
                    AssetDatabase.CreateFolder("Assets", "EMD2Unity_Editable");
                string folderPath = AssetDatabase.GenerateUniqueAssetPath(
                    folderBase + "/" + sourceWeapon.name + "_FBX");
                if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(
                    folderBase, Path.GetFileName(folderPath))))
                    throw new IOException("Cannot create unique editable FBX mesh folder.");
                instance = (GameObject)PrefabUtility.InstantiatePrefab(sourceWeapon);
                if (instance == null) instance = Instantiate(sourceWeapon);
                instance.name = sourceWeapon.name + "_FBXPrepared";
                Undo.RegisterCreatedObjectUndo(instance, "Prepare EMD weapon from FBX");
                var newParts = OrderedEmdParts(instance);
                if (newParts.Count != emd.Count)
                    throw new InvalidDataException("Native EMD copy has different group count.");
                for (int i = 0; i < newParts.Count; i++)
                {
                    Mesh mesh = replacements[i] ?? Instantiate(emd[i].mesh);
                    mesh.name = newParts[i].name + "_Editable";
                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                        folderPath + "/" + newParts[i].name + ".asset");
                    AssetDatabase.CreateAsset(mesh, assetPath);
                    if (newParts[i].skinned != null)
                    {
                        newParts[i].skinned.sharedMesh = mesh;
                        if (PrefabUtility.IsPartOfPrefabInstance(newParts[i].skinned))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(newParts[i].skinned);
                    }
                    else if (newParts[i].filter != null)
                    {
                        newParts[i].filter.sharedMesh = mesh;
                        if (PrefabUtility.IsPartOfPrefabInstance(newParts[i].filter))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(newParts[i].filter);
                    }
                }
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = instance;
                EditorGUIUtility.PingObject(instance);
                summary.AppendLine("Mapped FBX meshes: " + totalMapped + "/" + newParts.Count);
                summary.AppendLine("Saved independent editable meshes: " + folderPath);
                summary.AppendLine("Selected Scene root: " + instance.name);
                summary.AppendLine("NEXT: Open Weapon EMD Topology Rebuild. " +
                    "Set source to original _Wxx.emd; set Edited Root to this selected scene copy.");
                summary.AppendLine("Set Prefer original index attributes OFF for Blender vertex reorder. " +
                    "Set Write edited UV0 ON to export the imported Blender UV.");
                summary.AppendLine("RESTRICTION: FBX skin weights/rig were NOT encoded. " +
                    "Nearest native EMD vertex supplies source rig data. " +
                    "The 12-byte auxiliary EMD vertex data remains experimental.");
                report = summary.ToString();
                Debug.Log(report);
            }
            catch (Exception e)
            {
                report = "Weapon FBX preparation failed: " + e.Message;
                Debug.LogError("EMD2Unity FBX preparation: " + e);
                if (instance != null) Undo.DestroyObjectImmediate(instance);
            }
        }

        private static bool Finite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
            !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
            !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }
}
#endif
