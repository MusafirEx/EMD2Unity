// Experimental topology-changing weapon EMD writer, restricted to the verified
// four-stream type-0x0004 / 8-byte UV layout. Retains source skeleton/materials.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public sealed class EmdWeaponTopologyRebuildWindow : EditorWindow
    {
        [SerializeField] private GameObject sourceWeapon;
        [SerializeField] private GameObject editedWeapon;
        [SerializeField] private bool preserveIndexAttributes = true;
        [SerializeField] private bool useEditedUv0 = false;
        [SerializeField] private bool useEditedColors = false;
        [SerializeField] private float attributeTransferLimit = 0.5f;
        private string lastReport;
        private Vector2 scroll;
        // Set only by the one-click FBX exporter. Unmapped original EMD parts
        // keep their original UV/color bytes rather than re-encoding imported UV0.
        private HashSet<string> directReplacedParts;
        // FBX direct-export path: use actual FBX normals and FBX bone weights,
        // never borrow original per-vertex attributes for replaced groups.
        private bool directNativeFbxAttributes;

        [MenuItem("Tools/EMD2Unity/Weapon EMD Topology Rebuild (Experimental)")]
        public static void Open() => GetWindow<EmdWeaponTopologyRebuildWindow>("Weapon EMD Rebuild");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Weapon EMD topology rebuild — ALPHA", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Accepts changed vertex and triangle counts for verified _Wxx.emd layout ONLY. " +
                "Retains source bones/materials and reconstructs all buffer offsets. " +
                "IMPORTANT: the 12-byte auxiliary vertex stream has not been fully decoded; " +
                "this version copies it and skin weights from the nearest ORIGINAL vertex. " +
                "Use a similar-shaped weapon, visually inspect, and test a backup in-game. " +
                "This is not a general-purpose EMD exporter.",
                MessageType.Warning);
            sourceWeapon = (GameObject)EditorGUILayout.ObjectField(
                "Original _Wxx.emd", sourceWeapon, typeof(GameObject), false);
            editedWeapon = (GameObject)EditorGUILayout.ObjectField(
                "Edited Scene/FBX Root", editedWeapon, typeof(GameObject), true);
            preserveIndexAttributes = EditorGUILayout.ToggleLeft(
                "Prefer original vertex index for attributes when vertex counts match",
                preserveIndexAttributes);
            useEditedUv0 = EditorGUILayout.ToggleLeft(
                "Write edited mesh UV0 (experimental, 8-byte source layout only)", useEditedUv0);
            useEditedColors = EditorGUILayout.ToggleLeft(
                "Write edited mesh vertex colors (ARGB/RGBA source setting)", useEditedColors);
            attributeTransferLimit = Mathf.Max(0.0001f, EditorGUILayout.FloatField(
                "Maximum donor distance (EMD model units)", attributeTransferLimit));
            using (new EditorGUI.DisabledScope(editedWeapon == null))
                if (GUILayout.Button("Add 1 Test Vertex + 1 Triangle to Editable handleblade"))
                    AddControlledTopologyTest();
            EditorGUILayout.HelpBox(
                "Each edited mesh must be a direct child named exactly as the imported EMD mesh " +
                "(for DRV: handleblade_0, emblem_00_1, car_wild_2, car_vegas_3), " +
                "with identity local transform and one triangle submesh. " +
                "Materials, bone hierarchy and polygon count cannot change yet.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(sourceWeapon == null || editedWeapon == null))
                if (GUILayout.Button("Rebuild NEW Weapon EMD (experimental)"))
                    Run();
            if (!string.IsNullOrEmpty(lastReport))
            {
                if (GUILayout.Button("Copy Last Rebuild Report"))
                    EditorGUIUtility.systemCopyBuffer = lastReport;
                scroll = EditorGUILayout.BeginScrollView(scroll);
                EditorGUILayout.TextArea(lastReport, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private void AddControlledTopologyTest()
        {
            try
            {
                if (editedWeapon == null || !editedWeapon.scene.IsValid() ||
                    !editedWeapon.scene.isLoaded || editedWeapon == sourceWeapon)
                    throw new InvalidDataException(
                        "Use an editable scene instance created by the Position Patch tool.");
                Mesh mesh = FindEditedMesh(editedWeapon, "handleblade_0");
                string path = AssetDatabase.GetAssetPath(mesh).Replace('\\', '/');
                if (!path.StartsWith("Assets/EMD2Unity_Editable/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Only independent mesh .assets from Create Editable Weapon Copy are safe to modify.");
                Vector3[] positions = mesh.vertices;
                int[] indices = mesh.triangles;
                if (positions.Length < 3 || indices.Length < 3)
                    throw new InvalidDataException("Editable handleblade must contain triangles.");
                int oldCount = positions.Length;
                Array.Resize(ref positions, oldCount + 1);
                positions[oldCount] = positions[0] + new Vector3(0.05f, 0f, 0f);
                Vector2[] uv = mesh.uv;
                Color32[] color = mesh.colors32;
                BoneWeight[] weights = mesh.boneWeights;
                Vector3[] normals = mesh.normals;
                if (uv.Length != 0) { Array.Resize(ref uv, oldCount + 1); uv[oldCount] = uv[0]; }
                if (color.Length != 0) { Array.Resize(ref color, oldCount + 1); color[oldCount] = color[0]; }
                if (weights.Length != 0) { Array.Resize(ref weights, oldCount + 1); weights[oldCount] = weights[0]; }
                if (normals.Length != 0) { Array.Resize(ref normals, oldCount + 1); normals[oldCount] = normals[0]; }
                Array.Resize(ref indices, indices.Length + 3);
                indices[indices.Length - 3] = oldCount;
                indices[indices.Length - 2] = indices[1];
                indices[indices.Length - 1] = indices[2];
                Undo.RecordObject(mesh, "EMD topology test: one vertex and triangle");
                mesh.vertices = positions;
                if (uv.Length != 0) mesh.uv = uv;
                if (color.Length != 0) mesh.colors32 = color;
                if (weights.Length != 0) mesh.boneWeights = weights;
                if (normals.Length != 0) mesh.normals = normals;
                mesh.triangles = indices;
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssets();
                lastReport = "Controlled topology test: handleblade_0 now has " +
                    positions.Length + " vertices and " + indices.Length / 3 +
                    " triangles; exactly +1 vertex and +1 face. Rebuild to a NEW EMD file.";
                Debug.Log("EMD2Unity topology test: " + lastReport);
            }
            catch (Exception ex)
            {
                lastReport = "Topology test refused: " + ex.Message;
                Debug.LogError("EMD2Unity topology test: " + ex);
            }
        }

        // Internal one-step entry point. No intermediate mesh assets or scene
        // copy are persisted. The caller owns and destroys the temporary root.
        // Unknown original per-vertex bytes still use nearest original vertices,
        // but the user's distance-threshold test is not part of this workflow.
        internal static string ExportDirectFromFbx(
            GameObject originalEmd, GameObject temporaryEditedRoot,
            HashSet<string> replacedParts, bool writeFbxUv0, bool writeFbxColors,
            bool useNativeFbxAttributes)
        {
            if (originalEmd == null || temporaryEditedRoot == null ||
                replacedParts == null || replacedParts.Count == 0)
                throw new InvalidDataException("Direct export requires an original EMD, FBX meshes and mapping.");
            var worker = CreateInstance<EmdWeaponTopologyRebuildWindow>();
            try
            {
                worker.sourceWeapon = originalEmd;
                worker.editedWeapon = temporaryEditedRoot;
                worker.preserveIndexAttributes = false;
                worker.useEditedUv0 = writeFbxUv0;
                worker.useEditedColors = writeFbxColors;
                worker.attributeTransferLimit = float.MaxValue;
                worker.directReplacedParts = new HashSet<string>(replacedParts);
                worker.directNativeFbxAttributes = useNativeFbxAttributes;
                worker.Run();
                return worker.lastReport;
            }
            finally
            {
                DestroyImmediate(worker);
            }
        }

        private const int MaxFileBytes = 64 * 1024 * 1024;
        private sealed class SourcePolygon
        {
            public int start, end, group, indexStart, vertexStart, auxStart, weightStart, footerStart;
            public int vertices, indices, material;
            public string name;
        }

        private static uint U32(byte[] b, int p)
        {
            if (p < 0 || p > b.Length - 4) throw new InvalidDataException("U32 outside EMD at 0x" + p.ToString("X"));
            return ((uint)b[p] << 24) | ((uint)b[p + 1] << 16) |
                   ((uint)b[p + 2] << 8) | b[p + 3];
        }
        private static ushort U16(byte[] b, int p)
        {
            if (p < 0 || p > b.Length - 2) throw new InvalidDataException("U16 outside EMD at 0x" + p.ToString("X"));
            return (ushort)((b[p] << 8) | b[p + 1]);
        }
        private static int Int32(byte[] b, int p) => checked((int)U32(b, p));
        private static void W32(byte[] b, int p, int v)
        {
            if (p < 0 || p > b.Length - 4) throw new InvalidDataException("W32 outside EMD");
            unchecked
            {
                b[p] = (byte)(v >> 24); b[p + 1] = (byte)(v >> 16);
                b[p + 2] = (byte)(v >> 8); b[p + 3] = (byte)v;
            }
        }
        private static void W16(byte[] b, int p, int v)
        {
            if (v < 0 || v > ushort.MaxValue || p < 0 || p > b.Length - 2)
                throw new InvalidDataException("16-bit EMD field overflow.");
            b[p] = (byte)(v >> 8); b[p + 1] = (byte)v;
        }
        private static int Align(int n, int alignment) => checked((n + alignment - 1) & ~(alignment - 1));
        private static byte[] Slice(byte[] b, int off, int n)
        {
            if (n < 0 || off < 0 || off > b.Length - n)
                throw new InvalidDataException("EMD source stream outside file.");
            var result = new byte[n];
            Buffer.BlockCopy(b, off, result, 0, n);
            return result;
        }
        private static float ReadF32(byte[] b, int off) =>
            BitConverter.ToSingle(BitConverter.GetBytes(U32(b, off)), 0);
        private static void WriteF32(byte[] b, int p, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidDataException("Non-finite mesh position.");
            W32(b, p, unchecked((int)BitConverter.ToUInt32(BitConverter.GetBytes(value), 0)));
        }
        private static string ReadName(byte[] b, int off)
        {
            byte[] name = Slice(b, off, 32);
            int end = Array.IndexOf(name, (byte)0);
            return Encoding.ASCII.GetString(name, 0, end < 0 ? name.Length : end);
        }
        private static SourcePolygon ParsePolygon(byte[] source, int start, int end, int number)
        {
            if (end - start < 0x1E0 || U32(source, start + 4) != end - start)
                throw new InvalidDataException("Unsupported polygon #" + number + " size.");
            var s = new SourcePolygon
            {
                start = start, end = end, name = ReadName(source, start + 0x10),
                material = U16(source, start + 0x130), group = start + 0x140
            };
            if (U16(source, start + 0x132) != 0x0004)
                throw new InvalidDataException(s.name + ": requires single-group polygon type 0x0004.");
            while (s.group + 0x90 <= end && U32(source, s.group) == 0) s.group += 4;
            if (s.group + 0x90 > end) throw new InvalidDataException(s.name + ": missing group descriptor.");
            s.vertices = U16(source, s.group + 0x88);
            s.indices = U16(source, s.group + 0x8A);
            s.indexStart = checked(s.group + Int32(source, s.group));
            s.vertexStart = checked(s.group + Int32(source, s.group + 8));
            s.auxStart = checked(s.group + Int32(source, s.group + 12));
            s.weightStart = checked(s.group + Int32(source, s.group + 0x30));
            s.footerStart = end - 0x40;
            if (s.indices < 3 || s.indices % 3 != 0 || s.vertices < 1 ||
                s.indexStart < s.group + 0x90 ||
                s.vertexStart - s.indexStart != Align(s.indices * 2, 16) ||
                s.auxStart - s.vertexStart != Align(s.vertices * 12, 16) ||
                s.weightStart - s.auxStart != Align(s.vertices * 12, 16) ||
                s.footerStart - s.weightStart != Align(s.vertices * 8, 16) ||
                U16(source, s.group + 4) != Align(s.indices * 2, 16) ||
                U16(source, s.group + 0x10) != Align(s.vertices * 12, 16) ||
                Int32(source, s.group + 0x14) != Align(s.vertices * 12, 16) ||
                Int32(source, s.group + 0x20) != s.vertices * 8 ||
                Int32(source, s.group + 0x58) != s.footerStart - s.group ||
                Int32(source, s.group + 0x64) != s.footerStart + 0x20 - s.group ||
                Int32(source, s.group + 0x74) != s.indexStart - s.group ||
                Int32(source, s.group + 0x34) != Align(end - start, 128) ||
                Int32(source, s.group + 0x38) != Align(s.vertices * 4, 32))
                throw new InvalidDataException(s.name +
                    ": source stream spacing/pointer variant is not verified for topology rebuilding.");
            return s;
        }

        private static Mesh FindEditedMesh(GameObject root, string name)
        {
            Mesh found = null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                if (t.parent != root.transform ||
                    t.localPosition.sqrMagnitude > 1e-7f ||
                    Quaternion.Angle(t.localRotation, Quaternion.identity) > 0.001f ||
                    (t.localScale - Vector3.one).sqrMagnitude > 1e-7f)
                    throw new InvalidDataException(name + ": child mesh transform must be identity.");
                var smr = t.GetComponent<SkinnedMeshRenderer>();
                var mf = t.GetComponent<MeshFilter>();
                Mesh mesh = smr != null ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
                if (mesh == null || !mesh.isReadable)
                    throw new InvalidDataException(name + ": missing/read-protected Mesh. Enable FBX Read/Write.");
                if (found != null) throw new InvalidDataException("Duplicate child name: " + name);
                found = mesh;
            }
            if (found == null) throw new InvalidDataException("Missing edited child: " + name);
            if (found.subMeshCount != 1 ||
                found.GetTopology(0) != MeshTopology.Triangles)
                throw new InvalidDataException(name + ": needs one triangle submesh.");
            return found;
        }

        private static byte[] Pad(byte[] b, int align)
        {
            Array.Resize(ref b, Align(b.Length, align));
            return b;
        }
        private static byte[] Concat(params byte[][] chunks)
        {
            int len = 0;
            foreach (byte[] c in chunks) len = checked(len + c.Length);
            var result = new byte[len];
            int p = 0;
            foreach (byte[] c in chunks) { Buffer.BlockCopy(c, 0, result, p, c.Length); p += c.Length; }
            return result;
        }
        private static int ClosestVertex(Vector3 p, Vector3[] originals, out float distance)
        {
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < originals.Length; i++)
            {
                float d = (p - originals[i]).sqrMagnitude;
                if (d < best) { best = d; nearest = i; }
            }
            distance = Mathf.Sqrt(best);
            return nearest;
        }
        private static Vector3 SourceSpace(Vector3 v, bool swap, float scale)
        {
            v /= scale;
            return swap ? new Vector3(v.x, v.z, v.y) : v;
        }
        // 8-byte EMD weight format = [weight8, rawBoneId8] repeated 4 times.
        // The importer's piecewise node remapping is inverted exhaustively.
        private static byte EncodeBoneId(EmdDocument template, SourcePolygon part, int boneIndex,
            int node1, int node2, int node3)
        {
            if (boneIndex < 0 || boneIndex >= template.Bones.Count)
                throw new InvalidDataException(part.name +
                    ": FBX references a bone outside original EMD hierarchy: " + boneIndex);
            int desiredNode = template.Bones[boneIndex].NodeID;
            for (int raw = 1; raw <= 256; raw++)
            {
                int node = raw >= node3 + 1 ? raw + node2 - node3 : raw + node1;
                if (node == desiredNode) return (byte)(raw - 1);
            }
            throw new InvalidDataException(part.name +
                ": original EMD group cannot encode FBX bone index " + boneIndex +
                " (node ID " + desiredNode + "). Use a compatible FBX rig/bone.");
        }

        private static void WriteNativeFbxSkinWeights(byte[] destination, Mesh mesh,
            EmdDocument document, SourcePolygon part, byte[] original, int vertexCount)
        {
            if (document.Bones.Count == 0)
                throw new InvalidDataException(part.name +
                    ": original EMD has no bone table. Native skin weight export requires bones.");
            BoneWeight[] source = mesh.boneWeights;
            if (source.Length != vertexCount)
                throw new InvalidDataException(part.name +
                    ": FBX skin weights missing for vertex count " + vertexCount +
                    ". For unskinned FBX, enable rigid-bind to an original EMD bone in Direct FBX Export.");
            int node1 = U16(original, part.group + 0x24) / 0x30;
            int node2 = U16(original, part.group + 0x26) / 0x30;
            int node3 = U16(original, part.group + 0x28) / 0x30;
            // Resolve the zero-weight fallback ID once per source polygon.
            int fallback = -1;
            for (int i = 0; i < document.Bones.Count; ++i)
            {
                try
                {
                    EncodeBoneId(document, part, i, node1, node2, node3);
                    fallback = i;
                    break;
                }
                catch (InvalidDataException) { }
            }
            if (fallback < 0)
                throw new InvalidDataException(part.name + ": no EMD bone can be encoded by this group.");
            byte fallbackId = EncodeBoneId(document, part, fallback, node1, node2, node3);
            for (int v = 0; v < vertexCount; ++v)
            {
                BoneWeight b = source[v];
                int[] ids = { b.boneIndex0, b.boneIndex1, b.boneIndex2, b.boneIndex3 };
                float[] sourceWeights = { b.weight0, b.weight1, b.weight2, b.weight3 };
                float total = 0f;
                for (int k = 0; k < 4; k++)
                {
                    float w = sourceWeights[k];
                    if (float.IsNaN(w) || float.IsInfinity(w) || w < 0f)
                        throw new InvalidDataException(part.name + ": invalid FBX skin weight at vertex " + v);
                    if (w > 0f && (ids[k] < 0 || ids[k] >= document.Bones.Count))
                        throw new InvalidDataException(part.name +
                            ": FBX bone mapping invalid at vertex " + v + ", channel " + k);
                    total += w;
                }
                if (total < 1e-6f)
                    throw new InvalidDataException(part.name +
                        ": FBX has zero total skin weight at vertex " + v);
                int[] outBytes = new int[4];
                float[] remainder = new float[4];
                int assigned = 0;
                for (int k = 0; k < 4; k++)
                {
                    float scaled = (sourceWeights[k] / total) * 255f;
                    outBytes[k] = Mathf.FloorToInt(scaled);
                    remainder[k] = scaled - outBytes[k];
                    assigned += outBytes[k];
                }
                while (assigned < 255)
                {
                    int largest = 0;
                    for (int k = 1; k < 4; k++)
                        if (remainder[k] > remainder[largest]) largest = k;
                    outBytes[largest]++;
                    remainder[largest] = -1f;
                    assigned++;
                }
                for (int k = 0; k < 4; ++k)
                {
                    destination[v * 8 + k * 2] = (byte)outBytes[k];
                    destination[v * 8 + k * 2 + 1] =
                        outBytes[k] > 0
                            ? EncodeBoneId(document, part, ids[k], node1, node2, node3)
                            : fallbackId;
                }
            }
        }

        private static void WriteNativeFbxNormals(byte[] destination, Mesh mesh,
            bool swap, int vertices, string name)
        {
            Vector3[] normals = mesh.normals;
            if (normals.Length != vertices)
                throw new InvalidDataException(name +
                    ": FBX normal count mismatch. Enable Import Normals in FBX import settings.");
            for (int i = 0; i < vertices; i++)
            {
                Vector3 n = swap
                    ? new Vector3(normals[i].x, normals[i].z, normals[i].y)
                    : normals[i];
                if (float.IsNaN(n.x) || float.IsInfinity(n.x) ||
                    float.IsNaN(n.y) || float.IsInfinity(n.y) ||
                    float.IsNaN(n.z) || float.IsInfinity(n.z) ||
                    n.sqrMagnitude < 1e-10f)
                    throw new InvalidDataException(name + ": invalid FBX normal #" + i);
                n.Normalize();
                WriteF32(destination, i * 12, n.x);
                WriteF32(destination, i * 12 + 4, n.y);
                WriteF32(destination, i * 12 + 8, n.z);
            }
        }

        private static byte[] IndicesAsBytes(int[] indices, int vertexCount, bool reverse)
        {
            var result = new byte[checked(indices.Length * 2)];
            for (int i = 0; i < indices.Length; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int v = indices[i + (reverse ? 2 - k : k)];
                    if (v < 0 || v >= vertexCount)
                        throw new InvalidDataException("Triangle references invalid vertex " + v);
                    W16(result, (i + k) * 2, v);
                }
            }
            return result;
        }
        private static string Sha(byte[] bytes)
        {
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private void Run()
        {
            try
            {
                string src = AssetDatabase.GetAssetPath(sourceWeapon);
                if (string.IsNullOrEmpty(src) ||
                    !Regex.IsMatch(Path.GetFileName(src), @"_W[0-9]{2,}\.emd$", RegexOptions.IgnoreCase) ||
                    !(AssetImporter.GetAtPath(src) is EmdImporter))
                    throw new InvalidDataException("Select an original imported _Wxx.emd asset.");
                if (editedWeapon == null || editedWeapon == sourceWeapon)
                    throw new InvalidDataException("Choose a separate edited model root.");
                var serialized = new SerializedObject(AssetImporter.GetAtPath(src));
                float scale = serialized.FindProperty("modelScale").floatValue;
                bool swap = serialized.FindProperty("swapYAndZ").boolValue;
                bool reverseWinding = serialized.FindProperty("reverseWinding").boolValue;
                bool sourceColorsRgbaLast =
                    serialized.FindProperty("vertexColorsRgbaLast").boolValue;
                if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale) ||
                    float.IsNaN(attributeTransferLimit) || float.IsInfinity(attributeTransferLimit) ||
                    attributeTransferLimit <= 0f)
                    throw new InvalidDataException("Invalid scale or transfer distance.");
                byte[] original = File.ReadAllBytes(src);
                if (original.Length < 0xB0 || original.Length > MaxFileBytes)
                    throw new InvalidDataException("Invalid weapon source size.");
                int polyCount = Int32(original, 0x24);
                int originalPolyStart = Int32(original, 0x4C);
                int oldUV = Int32(original, 0x5C);
                if (polyCount < 1 || polyCount > 32 || originalPolyStart < 0xB0 ||
                    oldUV <= originalPolyStart || oldUV >= original.Length)
                    throw new InvalidDataException("Unsupported source polygon/UV offsets.");
                EmdDocument originalDocument = directNativeFbxAttributes
                    ? EmdParser.Parse(original) : null;
                if (directNativeFbxAttributes && originalDocument.Bones.Count == 0)
                    throw new InvalidDataException(
                        "Native FBX bone export requires an EMD with a real bone table.");
                var output = new List<byte>(original.Length);
                output.AddRange(Slice(original, 0, originalPolyStart));
                int nextPoly = originalPolyStart;
                int nextUV = oldUV;
                var uvChunks = new List<byte[]>();
                var report = new StringBuilder();
                report.AppendLine("EMD Weapon Topology Rebuild (EXPERIMENTAL)");
                report.AppendLine("Source: " + src);
                report.AppendLine("Original SHA256: " + Sha(original));
                Vector3 globalMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                Vector3 globalMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                int changedTopology = 0, transferWarnings = 0;
                for (int i = 0; i < polyCount; i++)
                {
                    int polyEnd = checked(nextPoly + Int32(original, nextPoly + 4));
                    SourcePolygon sp = ParsePolygon(original, nextPoly, polyEnd, i);
                    Mesh mesh = FindEditedMesh(editedWeapon, sp.name + "_" + i);
                    Vector3[] local = mesh.vertices;
                    int[] triangles = mesh.triangles;
                    int n = local.Length;
                    if (n < 3 || n > 5000 || triangles.Length < 3 ||
                        triangles.Length > 32000 || triangles.Length % 3 != 0 ||
                        (long)n * sp.vertices > 12000000)
                        throw new InvalidDataException(sp.name + ": vertex/index limits exceeded.");
                    var coords = new Vector3[n];
                    var oldCoords = new Vector3[sp.vertices];
                    for (int v = 0; v < sp.vertices; v++)
                    {
                        int p = sp.vertexStart + v * 12;
                        oldCoords[v] = new Vector3(ReadF32(original, p),
                            ReadF32(original, p + 4), ReadF32(original, p + 8));
                    }
                    for (int v = 0; v < n; v++) coords[v] = SourceSpace(local[v], swap, scale);
                    byte[] indices = IndicesAsBytes(triangles, n, !(reverseWinding ^ swap));
                    byte[] positions = new byte[n * 12];
                    byte[] auxiliary = new byte[n * 12];
                    byte[] weights = new byte[n * 8];
                    byte[] uvData = new byte[n * 8];
                    Vector2[] meshUV = mesh.uv;
                    Color32[] meshColors = mesh.colors32;
                    Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                    float maximumTransfer = 0f;
                    bool preserveOrder = preserveIndexAttributes && n == sp.vertices;
                    for (int v = 0; v < n; v++)
                    {
                        Vector3 p = coords[v];
                        WriteF32(positions, v * 12, p.x);
                        WriteF32(positions, v * 12 + 4, p.y);
                        WriteF32(positions, v * 12 + 8, p.z);
                        min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                        float nearestDistance = 0f;
                        int closest = preserveOrder ? v : ClosestVertex(p, oldCoords, out nearestDistance);
                        if (preserveOrder) nearestDistance = (p - oldCoords[v]).magnitude;
                        if (nearestDistance > maximumTransfer) maximumTransfer = nearestDistance;
                        if (nearestDistance > attributeTransferLimit)
                            throw new InvalidDataException(sp.name + ": vertex " + v +
                                " is " + nearestDistance.ToString("G5") +
                                " EMD model units away from original attribute donor " +
                                closest + ". Increase transfer limit ONLY if deliberate.");
                        if (!(directNativeFbxAttributes && directReplacedParts != null &&
                              directReplacedParts.Contains(sp.name + "_" + i)))
                        {
                            Buffer.BlockCopy(original, sp.auxStart + closest * 12, auxiliary, v * 12, 12);
                            Buffer.BlockCopy(original, sp.weightStart + closest * 8, weights, v * 8, 8);
                        }
                        int oldUvByte = nextUV + closest * 8;
                        if (oldUvByte < 0 || oldUvByte > original.Length - 8)
                            throw new InvalidDataException(sp.name + ": UV source block overflow.");
                        Buffer.BlockCopy(original, oldUvByte, uvData, v * 8, 8);
                    }
                    bool directChangedPart = directReplacedParts == null ||
                        directReplacedParts.Contains(sp.name + "_" + i);
                    if (directNativeFbxAttributes && directChangedPart)
                    {
                        WriteNativeFbxNormals(auxiliary, mesh, swap, n, sp.name);
                        WriteNativeFbxSkinWeights(weights, mesh, originalDocument, sp, original, n);
                    }
                    if (useEditedUv0 && directChangedPart)
                    {
                        if (meshUV == null || meshUV.Length != n)
                            throw new InvalidDataException(sp.name + ": edited UV0 missing.");
                        for (int v = 0; v < n; v++)
                        {
                            W16(uvData, v * 8 + 4, EncodeHalf(meshUV[v].x));
                            W16(uvData, v * 8 + 6, EncodeHalf(1f - meshUV[v].y));
                        }
                    }
                    if (useEditedColors && directChangedPart)
                    {
                        if (meshColors == null || meshColors.Length != n)
                            throw new InvalidDataException(sp.name + ": edited vertex colors missing.");
                        for (int v = 0; v < n; v++)
                        {
                            int o = v * 8;
                            Color32 c = meshColors[v];
                            if (sourceColorsRgbaLast)
                            {
                                uvData[o] = c.r; uvData[o + 1] = c.g;
                                uvData[o + 2] = c.b; uvData[o + 3] = c.a;
                            }
                            else
                            {
                                uvData[o] = c.a; uvData[o + 1] = c.r;
                                uvData[o + 2] = c.g; uvData[o + 3] = c.b;
                            }
                        }
                    }
                    byte[] originalIndices = Slice(original, sp.indexStart, sp.indices * 2);
                    if (n == sp.vertices && indices.Length == originalIndices.Length &&
                        Matches(indices, originalIndices) &&
                        Matches(positions, Slice(original, sp.vertexStart, n * 12)))
                    {
                        // Exactly unchanged geometry: inherit per-index source attributes to
                        // guarantee a bit-for-bit no-op rebuild even with coincident vertices.
                        if (!(directNativeFbxAttributes && directChangedPart))
                        {
                            auxiliary = Slice(original, sp.auxStart, n * 12);
                            weights = Slice(original, sp.weightStart, n * 8);
                        }
                        if (!(useEditedUv0 && directChangedPart) &&
                            !(useEditedColors && directChangedPart))
                            uvData = Slice(original, nextUV, n * 8);
                    }
                    byte[] originalIndexPadded = Slice(original, sp.indexStart, sp.vertexStart - sp.indexStart);
                    byte[] originalPositionPadded = Slice(original, sp.vertexStart, sp.auxStart - sp.vertexStart);
                    byte[] originalAuxPadded = Slice(original, sp.auxStart, sp.weightStart - sp.auxStart);
                    byte[] originalWeightPadded = Slice(original, sp.weightStart, sp.footerStart - sp.weightStart);
                    byte[] idxPadded = PadWithOriginalIfSame(indices, originalIndexPadded);
                    byte[] posPadded = PadWithOriginalIfSame(positions, originalPositionPadded);
                    byte[] auxPadded = PadWithOriginalIfSame(auxiliary, originalAuxPadded);
                    byte[] weightPadded = PadWithOriginalIfSame(weights, originalWeightPadded);
                    int groupRelative = sp.group - sp.start;
                    byte[] prefix = Slice(original, sp.start, sp.indexStart - sp.start);
                    int indexRelative = prefix.Length;
                    int posRelative = indexRelative + idxPadded.Length;
                    int auxRelative = posRelative + posPadded.Length;
                    int weightRelative = auxRelative + auxPadded.Length;
                    int footerRelative = weightRelative + weightPadded.Length;
                    byte[] poly = Concat(prefix, idxPadded, posPadded, auxPadded,
                        weightPadded, Slice(original, sp.footerStart, 0x40));
                    if (poly.Length > MaxFileBytes || idxPadded.Length > ushort.MaxValue ||
                        posPadded.Length > ushort.MaxValue || auxPadded.Length > int.MaxValue ||
                        n * 8 > ushort.MaxValue)
                        throw new InvalidDataException(sp.name + ": source-format field overflow.");
                    W32(poly, 4, poly.Length);
                    W32(poly, groupRelative + 0x00, indexRelative - groupRelative);
                    W16(poly, groupRelative + 0x04, idxPadded.Length);
                    W32(poly, groupRelative + 0x08, posRelative - groupRelative);
                    W32(poly, groupRelative + 0x0C, auxRelative - groupRelative);
                    W16(poly, groupRelative + 0x10, posPadded.Length);
                    W32(poly, groupRelative + 0x14, auxPadded.Length);
                    W32(poly, groupRelative + 0x20, n * 8);
                    W32(poly, groupRelative + 0x30, weightRelative - groupRelative);
                    W32(poly, groupRelative + 0x34, Align(poly.Length, 128));
                    W32(poly, groupRelative + 0x38, Align(n * 4, 32));
                    W32(poly, groupRelative + 0x58, footerRelative - groupRelative);
                    W32(poly, groupRelative + 0x64, footerRelative + 0x20 - groupRelative);
                    W32(poly, groupRelative + 0x74, indexRelative - groupRelative);
                    W16(poly, groupRelative + 0x88, n);
                    W16(poly, groupRelative + 0x8A, triangles.Length);
                    for (int axis = 0; axis < 3; axis++)
                    {
                        float lo = axis == 0 ? min.x : axis == 1 ? min.y : min.z;
                        float hi = axis == 0 ? max.x : axis == 1 ? max.y : max.z;
                        WriteF32(poly, 0x110 + axis * 4, lo);
                        WriteF32(poly, 0x120 + axis * 4, hi);
                    }
                    output.AddRange(poly);
                    globalMin = Vector3.Min(globalMin, min);
                    globalMax = Vector3.Max(globalMax, max);
                    if (n != sp.vertices || triangles.Length != sp.indices) changedTopology++;
                    if (maximumTransfer > 0.05f) transferWarnings++;
                    report.AppendLine(sp.name + ": " + sp.vertices + " -> " + n +
                        " vertices; " + (sp.indices / 3) + " -> " + (triangles.Length / 3) +
                        " triangles; donor max distance=" + maximumTransfer.ToString("G5"));
                    int oldUVBytes = sp.vertices * 8;
                    byte[] oldUvBlock = Slice(original, nextUV,
                        Align(nextUV + oldUVBytes, 16) - nextUV);
                    uvChunks.Add(PadWithOriginalIfSame(uvData, oldUvBlock));
                    nextUV = Align(nextUV + oldUVBytes, 16);
                    nextPoly = polyEnd;
                }
                if (nextPoly != oldUV)
                    throw new InvalidDataException("Polygon/UV section boundary mismatch.");
                int newUVOffset = output.Count;
                foreach (byte[] chunk in uvChunks) output.AddRange(chunk);
                if (output.Count > MaxFileBytes)
                    throw new InvalidDataException("Rebuilt EMD exceeds 64 MiB.");
                byte[] result = output.ToArray();
                W32(result, 0x5C, newUVOffset);
                W32(result, 0x60, newUVOffset - 0x10);
                W32(result, 0x64, result.Length - newUVOffset);
                for (int axis = 0; axis < 3; axis++)
                {
                    WriteF32(result, 0x70 + axis * 4,
                        axis == 0 ? globalMin.x : axis == 1 ? globalMin.y : globalMin.z);
                    WriteF32(result, 0x80 + axis * 4,
                        axis == 0 ? globalMax.x : axis == 1 ? globalMax.y : globalMax.z);
                }
                EmdDocument parsed = EmdParser.Parse(result);
                if (parsed.Groups.Count != polyCount)
                    throw new InvalidDataException("Rebuilt EMD parser group-count check failed.");
                int validatedVertices = 0;
                int validatedTriangles = 0;
                for (int i = 0; i < polyCount; i++)
                {
                    var originalGroup = EmdParser.Parse(original).Groups[i];
                    var newGroup = parsed.Groups[i];
                    if (newGroup.Vertices.Length < 3 ||
                        newGroup.Triangles.Length < 3 ||
                        newGroup.Triangles.Length % 3 != 0 ||
                        newGroup.MaterialIndex != originalGroup.MaterialIndex ||
                        newGroup.Name != originalGroup.Name)
                        throw new InvalidDataException("Reimport validation failed for mesh #" + i);
                    validatedVertices += newGroup.Vertices.Length;
                    validatedTriangles += newGroup.Triangles.Length / 3;
                }
                report.AppendLine("Validated parsed vertices: " + validatedVertices +
                    ", triangles: " + validatedTriangles);
                report.AppendLine("File: " + original.Length + " -> " + result.Length + " bytes");
                report.AppendLine("New SHA256: " + Sha(result));
                report.AppendLine("Reimport parser: PASS (" + parsed.Groups.Count + " groups)");
                report.AppendLine("Groups with changed vertex/triangle counts: " + changedTopology);
                report.AppendLine("Mesh groups with max donor displacement > 0.05: " + transferWarnings);
                report.AppendLine(directNativeFbxAttributes
                    ? "FBX native attributes: normals encoded as 3 big-endian float32 values; " +
                      "FBX weights encoded as 4 EMD bone-ID/weight pairs per vertex on replaced groups."
                    : "CAUTION: 12-byte per-vertex auxiliary data and weights were " +
                      "transferred from original vertices. Only PS3 in-game tests can validate rendering.");
                string saved = EditorUtility.SaveFilePanel(
                    "Save new topology-rebuilt _Wxx.emd", Path.GetDirectoryName(Path.GetFullPath(src)),
                    Path.GetFileNameWithoutExtension(src) + "_topology.emd", "emd");
                if (string.IsNullOrEmpty(saved)) { lastReport = report + "\nCancelled; no file written."; return; }
                if (File.Exists(saved) || string.Equals(Path.GetFullPath(saved), Path.GetFullPath(src),
                        StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Refusing to overwrite original or any existing file.");
                using (var stream = new FileStream(saved, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.Write(result, 0, result.Length);
                lastReport = report + "\nSaved: " + saved;
                Debug.Log("EMD2Unity topology rebuild:\n" + lastReport);
            }
            catch (Exception e)
            {
                lastReport = "EMD topology rebuild refused: " + e.Message;
                Debug.LogError("EMD2Unity topology rebuild: " + e);
                EditorUtility.DisplayDialog("EMD topology rebuild refused", e.Message, "OK");
            }
        }

        private static bool Matches(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        private static byte[] PadWithOriginalIfSame(byte[] raw, byte[] originalPadded)
        {
            int aligned = Align(raw.Length, 16);
            byte[] padded = new byte[aligned];
            Buffer.BlockCopy(raw, 0, padded, 0, raw.Length);
            if (aligned == originalPadded.Length &&
                raw.Length <= originalPadded.Length)
            {
                // Preserve source padding bytes, including nonzero unknown values.
                Buffer.BlockCopy(originalPadded, raw.Length, padded, raw.Length,
                    originalPadded.Length - raw.Length);
            }
            return padded;
        }
        private static ushort EncodeHalf(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value) > 65504f)
                throw new InvalidDataException("UV coordinate cannot be represented as float16.");
            uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
            uint sign = (bits >> 16) & 0x8000u;
            int exponent = (int)((bits >> 23) & 0xFFu) - 127 + 15;
            uint mantissa = bits & 0x7FFFFFu;
            if (exponent <= 0)
            {
                if (exponent < -10) return (ushort)sign;
                mantissa |= 0x800000u;
                int shift = 14 - exponent;
                uint halfMantissa = (mantissa + (1u << (shift - 1))) >> shift;
                return (ushort)(sign | halfMantissa);
            }
            if (exponent >= 31) throw new InvalidDataException("UV coordinate exceeds float16 range.");
            uint fraction = (mantissa + 0x1000u) >> 13;
            if (fraction == 0x400u) { fraction = 0; exponent++; }
            if (exponent >= 31) throw new InvalidDataException("UV coordinate exceeds float16 range.");
            return (ushort)(sign | ((uint)exponent << 10) | fraction);
        }
    }
}
#endif
