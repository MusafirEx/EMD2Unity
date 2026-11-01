// Experimental two-stage Unity GameObject -> weapon EMD replacement.
// This first stage appends source bones to a verified original-weapon EMD
// template, preserving original attachment bones/geometry/material slots.
// The Direct FBX to Weapon EMD tool then rebuilds mesh streams from ANY
// selected GameObject (including BRRES prefabs). No game acceptance implied.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public sealed class EmdWeaponGameObjectReplacementWindow : EditorWindow
    {
        private const int BoneRecordSize = 0x1A0;
        private const int MaxFileBytes = 64 * 1024 * 1024;
        [SerializeField] private GameObject originalEmd;
        [SerializeField] private GameObject sourceGameObject;
        [SerializeField] private int attachmentBoneIndex;
        [SerializeField] private bool patchTextureReferenceNames;
        [SerializeField] private bool allowUnverifiedBindMatrix;
        [SerializeField] private string report = "Select original DRV00_W00.emd and the axe002 Scene GameObject.";
        private Vector2 scroll;

        [Serializable] private sealed class MaterialReference
        {
            public string polygon;
            public int originalMaterialIndex;
            public string originalMaterialName;
            public string originalTextureName;
            public string sourceMaterialName;
            public string sourceTextureName;
            public string sourceTextureAssetPath;
        }

        [Serializable] private sealed class ReplacementManifest
        {
            public string sourceUnityGameObject;
            public string sourcePrefabAssetPath;
            public string originalEmdAssetPath;
            public string generatedTemplatePath;
            public string note = "Not game-ready: second step must write replacement meshes; source textures must be encoded/registered as game-supported DDS/EMAT resources.";
            public string[] sourceBoneNames;
            public string[] sourceMeshPaths;
            public int sourceMeshCount;
            public MaterialReference[] materials;
        }

        private sealed class InputMesh
        {
            internal Renderer renderer;
            internal Mesh mesh;
            internal string path;
        }

        private sealed class Analysis
        {
            internal byte[] original;
            internal EmdDocument document;
            internal string originalPath;
            internal Transform rigRoot;
            internal Transform[] bones;
            internal List<InputMesh> meshes;
            internal MaterialReference[] materials;
            internal int boneStart;
            internal int oldBoneEnd;
            internal int materialStart;
            internal int polygonStart;
            internal int uvStart;
            internal float modelScale;
            internal bool swapYAndZ;
            internal float maxInverseDeviation;
        }

        [MenuItem("Tools/EMD2Unity/Full GameObject Weapon Replacement (Alpha)")]
        public static void Open()
        {
            var w = GetWindow<EmdWeaponGameObjectReplacementWindow>("GameObject to EMD");
            if (Selection.activeGameObject != null)
                w.sourceGameObject = Selection.activeGameObject;
        }

        private static int Be32(byte[] data, int offset)
        {
            if (offset < 0 || offset > data.Length - 4)
                throw new InvalidDataException("32-bit EMD field outside file.");
            uint value = ((uint)data[offset] << 24) |
                ((uint)data[offset + 1] << 16) |
                ((uint)data[offset + 2] << 8) | data[offset + 3];
            if (value > int.MaxValue)
                throw new InvalidDataException("Unexpected negative/large EMD offset at 0x" + offset.ToString("X"));
            return (int)value;
        }

        private static void Put32(byte[] data, int offset, int value)
        {
            if (offset < 0 || offset > data.Length - 4)
                throw new InvalidDataException("EMD write outside file.");
            unchecked
            {
                data[offset] = (byte)(value >> 24);
                data[offset + 1] = (byte)(value >> 16);
                data[offset + 2] = (byte)(value >> 8);
                data[offset + 3] = (byte)value;
            }
        }

        private static void PutFloat(byte[] data, int offset, float value)
        {
            if (float.IsInfinity(value) || float.IsNaN(value))
                throw new InvalidDataException("Non-finite source bone matrix.");
            Put32(data, offset, BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
        }

        private static void PutName(byte[] data, int offset, int capacity, string value)
        {
            if (string.IsNullOrEmpty(value) || value.Any(c => c < ' ' || c > '~'))
                throw new InvalidDataException("EMD names must be printable ASCII: " + value);
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            if (bytes.Length >= capacity || offset < 0 || offset > data.Length - capacity)
                throw new InvalidDataException("EMD name exceeds " + (capacity - 1) + " bytes: " + value);
            Array.Clear(data, offset, capacity);
            Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length);
        }

        private static Matrix4x4 ReadMatrix(byte[] bytes, int offset)
        {
            Matrix4x4 matrix = Matrix4x4.zero;
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                {
                    int at = offset + (col * 4 + row) * 4;
                    if (at < 0 || at > bytes.Length - 4)
                        throw new InvalidDataException("Bone matrix outside EMD.");
                    uint bits = ((uint)bytes[at] << 24) |
                        ((uint)bytes[at + 1] << 16) |
                        ((uint)bytes[at + 2] << 8) | bytes[at + 3];
                    matrix[row, col] = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
                }
            return matrix;
        }

        private static float MatrixMaxError(Matrix4x4 left, Matrix4x4 right)
        {
            float error = 0f;
            for (int i = 0; i < 16; i++)
            {
                if (float.IsNaN(left[i]) || float.IsNaN(right[i]) ||
                    float.IsInfinity(left[i]) || float.IsInfinity(right[i]))
                    return float.PositiveInfinity;
                error = Mathf.Max(error, Mathf.Abs(left[i] - right[i]));
            }
            return error;
        }

        private static void PutMatrix(byte[] bytes, int offset, Matrix4x4 matrix)
        {
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                    PutFloat(bytes, offset + (col * 4 + row) * 4, matrix[row, col]);
        }

        private static bool IsDescendantOf(Transform child, Transform root)
        {
            while (child != null)
            {
                if (child == root) return true;
                child = child.parent;
            }
            return false;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null) return skinned.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        private static Texture GetMainTexture(Material mat)
        {
            if (mat == null) return null;
            if (mat.HasProperty("_MainTex")) return mat.GetTexture("_MainTex");
            if (mat.HasProperty("_BaseMap")) return mat.GetTexture("_BaseMap");
            if (mat.HasProperty("_BaseColorMap")) return mat.GetTexture("_BaseColorMap");
            return null;
        }

        private static string RelativePath(Transform root, Transform child)
        {
            var names = new List<string>();
            for (Transform t = child; t != null && t != root; t = t.parent)
                names.Add(t.name);
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private Analysis Inspect()
        {
            if (originalEmd == null || sourceGameObject == null)
                throw new InvalidDataException("Select both the original weapon EMD and the replacement GameObject.");
            string path = AssetDatabase.GetAssetPath(originalEmd);
            if (string.IsNullOrEmpty(path) ||
                !Regex.IsMatch(Path.GetFileName(path), @"_W[0-9]{2,}\.emd$", RegexOptions.IgnoreCase) ||
                !(AssetImporter.GetAtPath(path) is EmdImporter))
                throw new InvalidDataException("Original weapon must be an imported _Wxx.emd asset.");
            byte[] original = File.ReadAllBytes(path);
            if (original.Length > MaxFileBytes || original.Length < 0xB0)
                throw new InvalidDataException("Invalid original EMD file length.");
            var doc = EmdParser.Parse(original);
            int oldBones = Be32(original, 0x2C);
            int oldMaterials = Be32(original, 0x30);
            int boneStart = Be32(original, 0x54);
            int oldBoneEnd = checked(boneStart + oldBones * BoneRecordSize);
            int materialStart = Be32(original, 0x58);
            int polygonStart = Be32(original, 0x4C);
            int uvStart = Be32(original, 0x5C);
            if (oldBones != doc.Bones.Count || oldBones < 1 || oldBones > 100 ||
                oldMaterials != doc.Materials.Count || materialStart != oldBoneEnd ||
                oldBoneEnd <= boneStart || polygonStart <= materialStart ||
                uvStart <= polygonStart || uvStart > original.Length ||
                Be32(original, 0x60) != uvStart - 16)
                throw new InvalidDataException("This EMD does not match the verified contiguous bone/material/polygon/UV template layout. Refusing speculative bone writes.");
            // The source parser advances by exactly 0x1A0 per bone; the
            // embedded recordSize field is not a reliable stride indicator.
            float maxInverseError = 0f;
            for (int i = 0; i < oldBones; i++)
            {
                int start = boneStart + i * BoneRecordSize;
                Matrix4x4 forward = ReadMatrix(original, start + 0x120);
                Matrix4x4 reverse = ReadMatrix(original, start + 0x160);
                if (Mathf.Abs(forward.determinant) < 1e-8f)
                {
                    maxInverseError = float.PositiveInfinity;
                    break;
                }
                maxInverseError = Mathf.Max(maxInverseError,
                    MatrixMaxError(reverse, forward.inverse));
            }
            if (doc.Groups.Count < 1 || doc.Groups.Count > 32)
                throw new InvalidDataException("Original EMD polygon count unsupported.");
            var inputs = new List<InputMesh>();
            foreach (Renderer renderer in sourceGameObject.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = GetMesh(renderer);
                if (mesh == null) continue;
                if (!mesh.isReadable || mesh.subMeshCount != 1 ||
                    mesh.GetTopology(0) != UnityEngine.MeshTopology.Triangles ||
                    mesh.vertexCount < 3 || mesh.vertexCount > 5000 ||
                    mesh.GetIndexCount(0) < 3 || mesh.GetIndexCount(0) > 32000)
                    throw new InvalidDataException("Source mesh " + renderer.name +
                        " must be readable, have one triangle submesh, and fit the experimental writer limits.");
                inputs.Add(new InputMesh {
                    renderer = renderer, mesh = mesh,
                    path = RelativePath(sourceGameObject.transform, renderer.transform)
                });
            }
            if (inputs.Count == 0 || inputs.Count > 128)
                throw new InvalidDataException("Source must contain 1–128 compatible meshes, got " +
                    inputs.Count + ". This is a bounded alpha writer.");
            // A source GameObject may have more meshes than the binary EMD has
            // polygon slots. Direct Export provides explicit source-to-slot
            // assignment and merges geometry only when the user maps all parts.
            var skinnedRenderers = inputs.Select(m => m.renderer as SkinnedMeshRenderer)
                .Where(m => m != null).ToArray();
            if (skinnedRenderers.Length == 0)
                throw new InvalidDataException("The full-rig alpha needs at least one SkinnedMeshRenderer.");
            Transform rigRoot = skinnedRenderers[0].rootBone;
            if (rigRoot == null || !IsDescendantOf(rigRoot, sourceGameObject.transform))
                throw new InvalidDataException("Source SkinnedMeshRenderer rootBone must be inside the selected GameObject.");
            foreach (var skinned in skinnedRenderers)
                if (skinned.rootBone != rigRoot)
                    throw new InvalidDataException("All source meshes must share one rootBone for this alpha.");
            Transform[] bones = rigRoot.GetComponentsInChildren<Transform>(true);
            if (bones.Length == 0 || bones.Length + oldBones > 256 ||
                doc.Bones.Max(b => b.NodeID) + bones.Length > 256)
                throw new InvalidDataException("Bone count or node ID cannot fit supported byte identifiers.");
            var boneNames = new HashSet<string>(doc.Bones.Select(b => b.Name), StringComparer.Ordinal);
            foreach (Transform t in bones)
            {
                if (!boneNames.Add(t.name))
                    throw new InvalidDataException("Duplicate/conflicting bone name: " + t.name);
                if (!IsDescendantOf(t, rigRoot))
                    throw new InvalidDataException("Invalid source bone hierarchy.");
            }
            foreach (var skinned in skinnedRenderers)
            {
                if (skinned.sharedMesh.boneWeights.Length != skinned.sharedMesh.vertexCount)
                    throw new InvalidDataException(skinned.name + ": source skin weights missing.");
                foreach (Transform b in skinned.bones)
                    if (b == null || !IsDescendantOf(b, rigRoot))
                        throw new InvalidDataException(skinned.name +
                            ": skin bone not included under source rootBone.");
            }
            var importer = new SerializedObject(AssetImporter.GetAtPath(path));
            float scale = importer.FindProperty("modelScale").floatValue;
            bool swap = importer.FindProperty("swapYAndZ").boolValue;
            if (scale <= 0f || float.IsInfinity(scale) || float.IsNaN(scale))
                throw new InvalidDataException("Invalid EMD model scale.");
            var materials = new MaterialReference[inputs.Count];
            for (int i = 0; i < inputs.Count; ++i)
            {
                int matIndex = i < doc.Groups.Count ?
                    doc.Groups[i].MaterialIndex : -1;
                if (matIndex >= doc.Materials.Count)
                    throw new InvalidDataException("Invalid original material ID for polygon " + i);
                Material[] assigned = inputs[i].renderer.sharedMaterials;
                Material mat = assigned.Length > 0 ? assigned[0] : null;
                Texture texture = GetMainTexture(mat);
                materials[i] = new MaterialReference {
                    polygon = (i < doc.Groups.Count ? doc.Groups[i].Name : "(mapping pending)") +
                        " <- " + inputs[i].path,
                    originalMaterialIndex = matIndex,
                    originalMaterialName = matIndex >= 0 ?
                        doc.Materials[matIndex].Name : "(mapping pending)",
                    originalTextureName = matIndex >= 0 ?
                        doc.Materials[matIndex].TextureName : "(mapping pending)",
                    sourceMaterialName = mat != null ? mat.name : "(none)",
                    sourceTextureName = texture != null ? texture.name : "(none)",
                    sourceTextureAssetPath = texture != null ? AssetDatabase.GetAssetPath(texture) : ""
                };
            }
            return new Analysis {
                original = original, document = doc, originalPath = path,
                rigRoot = rigRoot, bones = bones, meshes = inputs, materials = materials,
                boneStart = boneStart, oldBoneEnd = oldBoneEnd, materialStart = materialStart,
                polygonStart = polygonStart, uvStart = uvStart,
                modelScale = scale, swapYAndZ = swap,
                maxInverseDeviation = maxInverseError
            };
        }

        private static Matrix4x4 SourceBoneMatrix(Transform transform, Transform rigRoot,
            Transform selectedRoot, float modelScale, bool swap)
        {
            Matrix4x4 local = transform == rigRoot
                ? selectedRoot.worldToLocalMatrix * transform.localToWorldMatrix
                : Matrix4x4.TRS(transform.localPosition, transform.localRotation, transform.localScale);
            local.m03 /= modelScale;
            local.m13 /= modelScale;
            local.m23 /= modelScale;
            if (swap)
            {
                Matrix4x4 basis = Matrix4x4.identity;
                basis.m11 = 0f; basis.m22 = 0f;
                basis.m12 = 1f; basis.m21 = 1f;
                local = basis * local * basis;
            }
            return local;
        }

        private static void PatchMaterialNames(byte[] result, Analysis a, int delta)
        {
            var desired = new Dictionary<int, string>();
            foreach (MaterialReference m in a.materials)
            {
                if (m.sourceTextureName == "(none)")
                    throw new InvalidDataException(m.polygon +
                        ": missing source texture; cannot replace EMD texture name.");
                string oldName;
                if (desired.TryGetValue(m.originalMaterialIndex, out oldName) &&
                    !string.Equals(oldName, m.sourceTextureName, StringComparison.Ordinal))
                    throw new InvalidDataException("Several source textures share original EMD material #" +
                        m.originalMaterialIndex + ". A new material-table writer is needed.");
                desired[m.originalMaterialIndex] = m.sourceTextureName;
            }
            int cursor = a.materialStart + delta;
            for (int i = 0; i < a.document.Materials.Count; i++)
            {
                int block = Be32(result, cursor + 4);
                if (block < 0x230 || cursor + block > a.polygonStart + delta)
                    throw new InvalidDataException("Unknown EMD material block layout.");
                int count = Be32(result, cursor + 556);
                if (count < 1 || count > 32)
                    throw new InvalidDataException("Unknown source submaterial count.");
                cursor += block;
                for (int n = 0; n < count; n++)
                {
                    int subSize = Be32(result, cursor + 4);
                    if (subSize < 0x110 || cursor + subSize > a.polygonStart + delta)
                        throw new InvalidDataException("Unknown EMD submaterial layout.");
                    if (n == 0 && desired.ContainsKey(i))
                        PutName(result, cursor + 0x10, 255, desired[i]);
                    cursor += subSize;
                }
            }
            if (cursor != a.polygonStart + delta)
                throw new InvalidDataException("Material section has unfamiliar trailing data.");
        }

        private byte[] BuildTemplate(Analysis a)
        {
            if (a.maxInverseDeviation > 0.05f && !allowUnverifiedBindMatrix)
                throw new InvalidDataException("Original EMD second bone matrix differs from inverse local " +
                    "(maximum difference " + a.maxInverseDeviation.ToString("G6") +
                    "). Its meaning is unverified. Use read-only diagnostics or explicitly opt in to unsafe testing.");
            int added = a.bones.Length;
            int delta = checked(added * BoneRecordSize);
            if (a.original.Length > MaxFileBytes - delta)
                throw new InvalidDataException("Output file would exceed 64 MiB.");
            var result = new byte[a.original.Length + delta];
            Buffer.BlockCopy(a.original, 0, result, 0, a.oldBoneEnd);
            Buffer.BlockCopy(a.original, a.oldBoneEnd, result,
                a.oldBoneEnd + delta, a.original.Length - a.oldBoneEnd);
            int oldCount = a.document.Bones.Count;
            int nextNode = a.document.Bones.Max(b => b.NodeID) + 1;
            var boneIndices = new Dictionary<Transform, int>();
            for (int i = 0; i < a.bones.Length; i++)
                boneIndices[a.bones[i]] = oldCount + i;
            for (int i = 0; i < a.bones.Length; i++)
            {
                Transform t = a.bones[i];
                int dest = a.oldBoneEnd + i * BoneRecordSize;
                // Preserve all unknown record fields from an original bone.
                int donor = a.boneStart + (oldCount - 1) * BoneRecordSize;
                Buffer.BlockCopy(a.original, donor, result, dest, BoneRecordSize);
                PutName(result, dest + 0x10, 32, t.name);
                int parent = t == a.rigRoot ? attachmentBoneIndex : boneIndices[t.parent];
                Put32(result, dest + 0x110, parent);
                Put32(result, dest + 0x118, nextNode + i - 1);
                Matrix4x4 local = SourceBoneMatrix(t, a.rigRoot,
                    sourceGameObject.transform, a.modelScale, a.swapYAndZ);
                if (Mathf.Abs(local.determinant) < 1e-8f)
                    throw new InvalidDataException("Degenerate source bone: " + t.name);
                PutMatrix(result, dest + 0x120, local);
                // This second matrix is unverified in the game; the working
                // hypothesis is inverse local/bind. Must be game-tested.
                PutMatrix(result, dest + 0x160, local.inverse);
            }
            Put32(result, 0x2C, oldCount + added);
            Put32(result, 0x58, a.materialStart + delta);
            Put32(result, 0x4C, a.polygonStart + delta);
            Put32(result, 0x5C, a.uvStart + delta);
            Put32(result, 0x60, a.uvStart + delta - 16);
            if (patchTextureReferenceNames)
            {
                if (a.meshes.Count != a.document.Groups.Count)
                    throw new InvalidDataException("Cannot rename per-slot textures while " +
                        a.meshes.Count + " source meshes have not yet been mapped to " +
                        a.document.Groups.Count + " target slots. Disable rename and use " +
                        "the final Direct Export material manifest.");
                PatchMaterialNames(result, a, delta);
            }
            EmdDocument parsed = EmdParser.Parse(result);
            if (parsed.Bones.Count != oldCount + added ||
                parsed.Groups.Count != a.document.Groups.Count ||
                parsed.Materials.Count != a.document.Materials.Count)
                throw new InvalidDataException("EMD parser round-trip failed: structural record count mismatch.");
            for (int i = 0; i < added; i++)
            {
                if (parsed.Bones[oldCount + i].Name != a.bones[i].name)
                    throw new InvalidDataException("Imported EMD appended-bone name mismatch.");
            }
            return result;
        }

        private void Generate()
        {
            try
            {
                Analysis a = Inspect();
                if (attachmentBoneIndex < 0 || attachmentBoneIndex >= a.document.Bones.Count)
                    throw new InvalidDataException("Invalid original weapon attachment bone.");
                byte[] output = BuildTemplate(a);
                string filename = Path.GetFileNameWithoutExtension(a.originalPath) + "_" +
                    sourceGameObject.name + "_W00";
                string path = EditorUtility.SaveFilePanelInProject(
                    "Save experimental extended-rig weapon EMD", filename, "emd",
                    "This is ONLY the intermediate template for the existing Direct FBX exporter.");
                if (string.IsNullOrEmpty(path)) return;
                if (!Regex.IsMatch(Path.GetFileName(path), @"_W[0-9]{2,}\.emd$", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("New template must end in _W00.emd for Direct FBX Export.");
                string fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath) ||
                    string.Equals(Path.GetFullPath(a.originalPath), fullPath,
                        StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Existing files are never overwritten.");
                var manifest = new ReplacementManifest {
                    sourceUnityGameObject = sourceGameObject.name,
                    sourcePrefabAssetPath = PrefabUtility.GetCorrespondingObjectFromSource(sourceGameObject) != null
                        ? AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(sourceGameObject))
                        : AssetDatabase.GetAssetPath(sourceGameObject),
                    originalEmdAssetPath = a.originalPath,
                    generatedTemplatePath = path,
                    sourceBoneNames = a.bones.Select(b => b.name).ToArray(),
                    sourceMeshPaths = a.meshes.Select(m => m.path).ToArray(),
                    sourceMeshCount = a.meshes.Count,
                    materials = a.materials
                };
                string manifestPath = Path.ChangeExtension(fullPath, ".replacement.json");
                if (File.Exists(manifestPath))
                    throw new IOException("Existing replacement manifest will not be overwritten.");
                using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.Write(output, 0, output.Length);
                File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), Encoding.UTF8);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(Path.ChangeExtension(path, ".replacement.json"),
                    ImportAssetOptions.ForceUpdate);
                var outputAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (outputAsset != null)
                    EmdDirectFbxExportWindow.ConfigureReplacement(outputAsset, sourceGameObject);
                report = "Created extended skeleton EMD template: " + path +
                    "\nOriginal bones: " + a.document.Bones.Count +
                    " + source bones: " + a.bones.Length +
                    "\nOriginal mesh slots/material count preserved: " + a.document.Groups.Count +
                    "/" + a.document.Materials.Count +
                    "\nSource renderers to map: " + a.meshes.Count +
                    (a.meshes.Count > a.document.Groups.Count ?
                        " (some source meshes must share a target slot)" : "") +
                    "\nManifest: " + manifestPath +
                    "\nNEXT: Direct FBX Export window opens with this EMD template and axe002." +
                    "\nWARNING: Bone inverse matrix and EMD external texture packaging are not game-validated. " +
                    "This template does NOT contain axe002 mesh geometry until Direct Export is run.";
                Debug.Log("EMD2Unity full replacement alpha:\n" + report);
            }
            catch (Exception e)
            {
                report = "Full GameObject replacement refused: " + e.Message;
                Debug.LogError("EMD2Unity full replacement alpha: " + e);
                EditorUtility.DisplayDialog("GameObject EMD replacement refused", e.Message, "OK");
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("ALPHA: Create an extended-bone weapon EMD template from a Unity GameObject (e.g. axe002.brres). " +
                "The next Direct FBX Export step writes the source mesh geometry and weights. " +
                "Original DRV attachment bones and EMD polygon/material slots remain for game compatibility. " +
                "New source textures require external game-format conversion/registration. " +
                "NEVER replace your original EMD before separate in-game tests.", MessageType.Warning);
            originalEmd = (GameObject)EditorGUILayout.ObjectField(
                "Original _Wxx EMD", originalEmd, typeof(GameObject), false);
            sourceGameObject = (GameObject)EditorGUILayout.ObjectField(
                "Selected GameObject", sourceGameObject, typeof(GameObject), true);
            string[] labels = null;
            if (originalEmd != null)
            {
                try
                {
                    string p = AssetDatabase.GetAssetPath(originalEmd);
                    if (p.EndsWith(".emd", StringComparison.OrdinalIgnoreCase))
                    {
                        EmdDocument doc = EmdParser.Parse(File.ReadAllBytes(p));
                        labels = doc.Bones.Select((b, i) => i + ": " + b.Name).ToArray();
                    }
                }
                catch (Exception) { }
            }
            if (labels != null && labels.Length > 0)
                attachmentBoneIndex = EditorGUILayout.Popup("Attach new ROOT under", Mathf.Clamp(
                    attachmentBoneIndex, 0, labels.Length - 1), labels);
            patchTextureReferenceNames = EditorGUILayout.ToggleLeft(
                "Experimental: rename existing EMD texture references (needs game DDS/EMAT)", patchTextureReferenceNames);
            allowUnverifiedBindMatrix = EditorGUILayout.ToggleLeft(
                "Unsafe: permit unknown second-bone-matrix layout (only for isolated test)", allowUnverifiedBindMatrix);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Analyze selected GameObject (read-only)"))
                {
                    try
                    {
                        Analysis a = Inspect();
                        var builder = new StringBuilder();
                        builder.AppendLine("GAMEOBJECT -> EMD FULL REPLACEMENT PREFLIGHT");
                        builder.AppendLine("Original: " + a.originalPath);
                        builder.AppendLine("Source: " + sourceGameObject.name);
                        builder.AppendLine("Original bones: " + a.document.Bones.Count +
                            "; appended source bones: " + a.bones.Length);
                        builder.AppendLine("Original bone reverse-matrix vs inverse-local max error: " +
                            a.maxInverseDeviation.ToString("G6") +
                            " (<= 0.05 considered candidate; not proof of game compatibility)");
                        foreach (Transform t in a.bones)
                            builder.AppendLine("  +" + t.name + " <- " +
                                (t == a.rigRoot ? "original attachment bone #" + attachmentBoneIndex : t.parent.name));
                        builder.AppendLine("Source meshes: " + a.meshes.Count +
                            "; existing EMD polygon slots: " + a.document.Groups.Count);
                        for (int i = 0; i < a.meshes.Count; i++)
                        {
                            var m = a.meshes[i];
                            builder.AppendLine("  SOURCE #" + i + ": " + m.path + " (" +
                                m.mesh.vertexCount + " vertices; " +
                                m.mesh.GetIndexCount(0) / 3 + " triangles; material " +
                                a.materials[i].sourceMaterialName + "; texture " +
                                a.materials[i].sourceTextureName + ")");
                        }
                        if (a.meshes.Count != a.document.Groups.Count)
                            builder.AppendLine("MAPPING REQUIRED: assign EVERY source mesh " +
                                "to one of the existing polygon slots in the Direct Export " +
                                "window. Nothing is silently omitted. Different textures " +
                                "merged into one slot require an atlas or explicit unsafe override.");
                        builder.AppendLine("Original texture slots are retained by default. " +
                            "No new game texture archive is written.");
                        report = builder.ToString();
                        Debug.Log("EMD2Unity GameObject preflight:\n" + report);
                    }
                    catch (Exception e)
                    {
                        report = "Preflight failed: " + e.Message;
                        Debug.LogError(report);
                    }
                }
                if (GUILayout.Button("Create skeleton template + open Direct Export"))
                    Generate();
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.SelectableLabel(report ?? "", EditorStyles.wordWrappedLabel,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
