// EMD2Unity - experimental Kamen Rider Battride War (PS3) EMD importer.
// Format research adapted from Random Talking Bush's KamenRiderPS3_EMD.ms.
// Original reference: https://github.com/RandomTBush/RTB-3DSMax-Scripts/blob/main/Scripts/KamenRiderPS3_EMD.ms
// The original author asks for attribution. No original game data included.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.EMD2Unity
{
    [ScriptedImporter(7, "emd")]
    public sealed class EmdImporter : ScriptedImporter
    {
        [Header("Model")]
        [SerializeField, Min(0.00001f)] private float modelScale = 1f;
        [SerializeField] private bool swapYAndZ = true;
        [SerializeField] private bool reverseWinding = false;
        [SerializeField] private bool useVertexColors = true;
        [SerializeField] private bool recoverSupportedPolygons = false;
        [Header("Rig")]
        [SerializeField] private bool importSkeleton = true;
        [Header("Materials")]
        [SerializeField] private bool findNearbyTextures = true;
        [SerializeField] private bool useEmatTextureReferences = true;
        [SerializeField] private bool linkNormalMaps = false;
        [SerializeField] private bool linkSpecularMaps = false;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            try
            {
                byte[] data = File.ReadAllBytes(ctx.assetPath);
                var document = EmdParser.Parse(data, recoverSupportedPolygons);
                CreateUnityAssets(ctx, document);
                foreach (string warning in document.ImportWarnings)
                    ctx.LogImportWarning(warning);
            }
            catch (Exception e)
            {
                ctx.LogImportError("EMD2Unity could not parse this file: " + e.Message + "\n" + e.StackTrace);
                var errorRoot = new GameObject(Path.GetFileNameWithoutExtension(ctx.assetPath) + " [EMD Import Error]");
                ctx.AddObjectToAsset("root", errorRoot);
                ctx.SetMainObject(errorRoot);
            }
        }

        private Vector3 ConvertVector(Vector3 v) =>
            (swapYAndZ ? new Vector3(v.x, v.z, v.y) : v) * modelScale;

        private Matrix4x4 ConvertBoneMatrix(Matrix4x4 source)
        {
            if (swapYAndZ)
            {
                Matrix4x4 flip = Matrix4x4.identity;
                flip.m11 = 0f; flip.m22 = 0f;
                flip.m12 = 1f; flip.m21 = 1f;
                source = flip * source * flip;
            }
            source.m03 *= modelScale;
            source.m13 *= modelScale;
            source.m23 *= modelScale;
            return source;
        }

        private static void SetLocalMatrix(Transform transform, Matrix4x4 matrix)
        {
            Vector3 right = new Vector3(matrix.m00, matrix.m10, matrix.m20);
            Vector3 up = new Vector3(matrix.m01, matrix.m11, matrix.m21);
            Vector3 forward = new Vector3(matrix.m02, matrix.m12, matrix.m22);
            Vector3 scale = new Vector3(right.magnitude, up.magnitude, forward.magnitude);
            if (scale.x < 1e-6f || scale.y < 1e-6f || scale.z < 1e-6f)
                throw new InvalidDataException("Bone transform has a zero-length axis.");
            if (Vector3.Dot(Vector3.Cross(right, up), forward) < 0)
            {
                scale.x = -scale.x;
                right = -right;
            }
            transform.localPosition = new Vector3(matrix.m03, matrix.m13, matrix.m23);
            transform.localRotation = Quaternion.LookRotation(forward / scale.z, up / scale.y);
            transform.localScale = scale;
        }

        private void CreateUnityAssets(AssetImportContext ctx, EmdDocument doc)
        {
            string name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            GameObject root = new GameObject(name);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var materials = new Material[doc.Materials.Count];
            for (int i = 0; i < materials.Length; ++i)
            {
                EmdMaterial info = doc.Materials[i];
                // Prefer the active render pipeline before trying a built-in shader.
                string pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null
                    ? "" : UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name;
                Shader shader = pipeline.Contains("Universal") ? Shader.Find("Universal Render Pipeline/Lit")
                    : pipeline.Contains("HD") ? Shader.Find("HDRP/Lit") : Shader.Find("Standard");
                if (linkSpecularMaps && string.IsNullOrEmpty(pipeline))
                    shader = Shader.Find("Standard (Specular setup)") ?? shader;
                shader = shader ?? Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Unlit/Texture");
                if (shader == null) throw new InvalidOperationException("No compatible material shader found.");
                Material mat = new Material(shader) { name = string.IsNullOrWhiteSpace(info.Name) ? "EMD_Material_" + i : info.Name };
                if (findNearbyTextures)
                {
                    string texName = info.TextureName;
                    var ematTextures = new List<string>();
                    if (useEmatTextureReferences)
                    {
                        string ematPath = FindCompanionEmat(ctx.assetPath, info.Name);
                        if (ematPath != null)
                        {
                            ctx.DependsOnSourceAsset(ematPath);
                            ematTextures = ReadEmatTextureReferences(ematPath);
                        }
                    }
                    if (string.IsNullOrWhiteSpace(texName) || texName == "null")
                    {
                        // Exceptions from Random Talking Bush's MaxScript.
                        if (name.Equals("CCS00", StringComparison.OrdinalIgnoreCase) && info.Name == "CCS00eye02") texName = "CCS00eye00_CO";
                        else if (name.Equals("KAB00", StringComparison.OrdinalIgnoreCase) && info.Name == "eye2") texName = "KAB00eye00_CO";
                        else if (name.Equals("GAI01_W01", StringComparison.OrdinalIgnoreCase) && info.Name == "GAI01W04_base") texName = "GAI01djgun00_CO";
                        else
                        {
                            // Do not guess a material variant when .emat contains multiple colour references.
                            var colours = ematTextures.Where(t => t.EndsWith("_CO", StringComparison.OrdinalIgnoreCase)).ToList();
                            if (colours.Count == 1) texName = colours[0];
                        }
                    }

                    Texture2D diffuse = LoadNearbyTexture(ctx, ctx.assetPath, texName);
                    if (diffuse != null)
                    {
                        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", diffuse);
                        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", diffuse);
                        if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", diffuse);
                    }

                    // EMAT contains actual texture references; unlike a guessed filename,
                    // the chosen normal/specular map must match the EMD colour-map stem.
                    if (!string.IsNullOrWhiteSpace(texName) &&
                        texName.EndsWith("_CO", StringComparison.OrdinalIgnoreCase))
                    {
                        string stem = texName.Substring(0, texName.Length - 3);
                        if (linkNormalMaps)
                        {
                            string normalName = stem + "_NO";
                            if (ematTextures.Any(t => t.Equals(normalName, StringComparison.OrdinalIgnoreCase)))
                            {
                                string normalPath = FindTexture(ctx.assetPath, normalName);
                                if (normalPath != null)
                                {
                                    ctx.DependsOnSourceAsset(normalPath);
                                    var importer = AssetImporter.GetAtPath(normalPath) as TextureImporter;
                                    // Unity normal-map packing requires an imported Normal map.
                                    // Do not assign raw DDS as a normal map: it renders incorrectly.
                                    if (importer != null && importer.textureType == TextureImporterType.NormalMap)
                                    {
                                        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                                        if (normal != null)
                                        {
                                            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", normal);
                                            if (mat.HasProperty("_NormalMap")) mat.SetTexture("_NormalMap", normal);
                                            mat.EnableKeyword("_NORMALMAP");
                                        }
                                    }
                                    else
                                        ctx.LogImportWarning("EMD normal texture '" + normalPath +
                                            "' is not imported as a Normal map. In the EMD Materials tab, click " +
                                            "'Prepare Nearby _NO Textures as Normal Maps' to fix this.");
                                }
                            }
                        }
                        if (linkSpecularMaps)
                        {
                            string specName = stem + "_SP";
                            if (ematTextures.Any(t => t.Equals(specName, StringComparison.OrdinalIgnoreCase)))
                            {
                                Texture2D spec = LoadNearbyTexture(ctx, ctx.assetPath, specName);
                                if (spec != null)
                                {
                                    if (mat.HasProperty("_SpecGlossMap"))
                                    {
                                        mat.SetTexture("_SpecGlossMap", spec);
                                        if (mat.HasProperty("_WorkflowMode"))
                                        {
                                            mat.SetFloat("_WorkflowMode", 0f);
                                            mat.EnableKeyword("_SPECULAR_SETUP");
                                        }
                                        mat.EnableKeyword("_SPECGLOSSMAP");
                                    }
                                    else if (mat.HasProperty("_SpecularColorMap"))
                                        mat.SetTexture("_SpecularColorMap", spec);
                                }
                            }
                        }
                    }
                }
                materials[i] = mat;
                ctx.AddObjectToAsset("material_" + i, mat);
            }

            Transform[] bones = new Transform[importSkeleton ? doc.Bones.Count : 0];
            if (bones.Length > 0)
            {
                var armature = new GameObject("Armature").transform;
                armature.SetParent(root.transform, false);
                for (int i = 0; i < bones.Length; ++i)
                {
                    string boneName = string.IsNullOrWhiteSpace(doc.Bones[i].Name) ? "Bone_" + i : doc.Bones[i].Name;
                    bones[i] = new GameObject(boneName).transform;
                    bones[i].SetParent(armature, false);
                }
                for (int i = 0; i < bones.Length; ++i)
                {
                    int parent = doc.Bones[i].Parent;
                    if (parent >= 0 && parent < bones.Length && parent != i)
                        bones[i].SetParent(bones[parent], false);
                    SetLocalMatrix(bones[i], ConvertBoneMatrix(doc.Bones[i].LocalMatrix));
                }
            }
            for (int i = 0; i < doc.Groups.Count; ++i)
            {
                EmdGroup info = doc.Groups[i];
                if (info.Vertices.Length == 0 || info.Triangles.Length == 0) continue;
                Mesh mesh = new Mesh { name = info.Name + "_" + i };
                if (info.Vertices.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
                Vector3[] verts = new Vector3[info.Vertices.Length];
                for (int v = 0; v < verts.Length; ++v) verts[v] = ConvertVector(info.Vertices[v]);
                mesh.vertices = verts;
                int[] triangles = (int[])info.Triangles.Clone();
                if (reverseWinding ^ swapYAndZ)
                    for (int t = 0; t < triangles.Length; t += 3)
                        { int temp = triangles[t]; triangles[t] = triangles[t + 2]; triangles[t + 2] = temp; }
                mesh.triangles = triangles;
                if (info.UV.Length == verts.Length) mesh.uv = info.UV;
                if (useVertexColors && info.Colors.Length == verts.Length) mesh.colors32 = info.Colors;
                mesh.RecalculateNormals();
                if (info.UV.Length == verts.Length) mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                ctx.AddObjectToAsset("mesh_" + i, mesh);

                var child = new GameObject(string.IsNullOrWhiteSpace(info.Name) ? "EMD_Mesh_" + i : info.Name + "_" + i);
                child.transform.SetParent(root.transform, false);
                Material mat = info.MaterialIndex >= 0 && info.MaterialIndex < materials.Length ? materials[info.MaterialIndex] : null;
                if (bones.Length > 0 && info.Weights.Length == verts.Length)
                {
                    BoneWeight[] weights = new BoneWeight[verts.Length];
                    for (int v = 0; v < weights.Length; ++v)
                    {
                        var weight = info.Weights[v];
                        BoneWeight converted = new BoneWeight();
                        int firstValid = 0;
                        float[] w = new float[4];
                        int[] ids = new int[4];
                        for (int k = 0; k < 4; ++k)
                        {
                            ids[k] = Math.Max(0, Math.Min(bones.Length - 1, weight.Indices[k]));
                            w[k] = weight.Indices[k] >= 0 && weight.Indices[k] < bones.Length ? Math.Max(0f, weight.Values[k]) : 0f;
                            if (w[k] > 0f) firstValid = ids[k];
                        }
                        float total = w[0] + w[1] + w[2] + w[3];
                        if (total <= 0.000001f) { w[0] = 1f; ids[0] = firstValid; total = 1f; }
                        converted.boneIndex0 = ids[0]; converted.weight0 = w[0] / total;
                        converted.boneIndex1 = ids[1]; converted.weight1 = w[1] / total;
                        converted.boneIndex2 = ids[2]; converted.weight2 = w[2] / total;
                        converted.boneIndex3 = ids[3]; converted.weight3 = w[3] / total;
                        weights[v] = converted;
                    }
                    mesh.boneWeights = weights;
                    Matrix4x4[] bindposes = new Matrix4x4[bones.Length];
                    for (int b = 0; b < bones.Length; ++b)
                        bindposes[b] = bones[b].worldToLocalMatrix * child.transform.localToWorldMatrix;
                    mesh.bindposes = bindposes;
                    SkinnedMeshRenderer render = child.AddComponent<SkinnedMeshRenderer>();
                    render.sharedMesh = mesh;
                    render.bones = bones;
                    render.rootBone = bones[0];
                    if (mat != null) render.sharedMaterial = mat;
                    render.updateWhenOffscreen = true;
                }
                else
                {
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer render = child.AddComponent<MeshRenderer>();
                    if (mat != null) render.sharedMaterial = mat;
                }
            }
            ctx.AddObjectToAsset("root", root);
            ctx.SetMainObject(root);
            if (doc.Groups.Count == 0)
            {
                ctx.LogImportWarning("EMD imported with no renderable polygon groups: " +
                    Path.GetFileName(ctx.assetPath) + ". Header declares " +
                    doc.DeclaredPolygonCount + " polygon records; " +
                    doc.EmptyGroupCount + " groups have zero vertices. " +
                    "Unity will display an empty root object. This file may be " +
                    "an empty/reference EMD or a variant of the format requiring " +
                    "further binary analysis.");
            }
        }

        // Called from the Inspector, never from OnImportAsset: changing an importer
        // during its own import would cause nested imports/reimport loops.
        // Every *_NO image in the model's adjacent textures directory is treated
        // as a normal map; existing NormalMap importers are left unchanged.
        internal static int PrepareNearbyNormalTextures(string emdPath, out int matched)
        {
            matched = 0;
            int updated = 0;
            string parent = Path.GetDirectoryName(emdPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) return 0;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] directories = { parent + "/textures", parent + "/Textures", parent };
            string[] extensions = { ".dds", ".png", ".tga", ".jpg", ".jpeg", ".bmp" };
            foreach (string directory in directories)
            {
                if (!Directory.Exists(directory)) continue;
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    if (!Path.GetFileNameWithoutExtension(file).EndsWith("_NO", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        continue;
                    string assetPath = file.Replace('\\', '/');
                    if (!visited.Add(assetPath)) continue;
                    var textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (textureImporter == null) continue;
                    matched++;
                    if (textureImporter.textureType == TextureImporterType.NormalMap) continue;
                    textureImporter.textureType = TextureImporterType.NormalMap;
                    textureImporter.sRGBTexture = false;
                    textureImporter.SaveAndReimport();
                    updated++;
                }
            }
            return updated;
        }

        // EMAT v1 discovery only: the observed files use the "TAME" byte order and
        // hold null-terminated texture names. We do not infer sampler order, UV sets,
        // render states or shader parameters from these strings.
        private static List<string> ReadEmatTextureReferences(string path)
        {
            var names = new List<string>();
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 4 || bytes.Length > 2 * 1024 * 1024 ||
                bytes[0] != (byte)'T' || bytes[1] != (byte)'A' ||
                bytes[2] != (byte)'M' || bytes[3] != (byte)'E') return names;
            string source = Encoding.ASCII.GetString(bytes);
            var matches = Regex.Matches(source,
                @"(?<![A-Za-z0-9_])[A-Za-z0-9_]{3,72}_(?:CO|NO|SP)(?=\x00)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (Match match in matches)
            {
                if (!names.Any(existing => existing.Equals(match.Value, StringComparison.OrdinalIgnoreCase)))
                    names.Add(match.Value);
            }
            return names;
        }

        private static string FindCompanionEmat(string emdPath, string materialName)
        {
            string parent = Path.GetDirectoryName(emdPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(materialName)) return null;
            string dir = parent + "/material";
            if (!Directory.Exists(dir)) return null;
            foreach (string path in Directory.EnumerateFiles(dir))
                if (Path.GetExtension(path).Equals(".emat", StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileNameWithoutExtension(path).Equals(materialName, StringComparison.OrdinalIgnoreCase))
                    return path.Replace('\\', '/');
            return null;
        }

        private static Texture2D LoadNearbyTexture(AssetImportContext ctx, string emdPath, string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName) || textureName == "null") return null;
            string assetPath = FindTexture(emdPath, textureName);
            if (assetPath == null) return null;
            ctx.DependsOnSourceAsset(assetPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static string FindTexture(string emdPath, string textureName)
        {
            string parent = Path.GetDirectoryName(emdPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) return null;
            string[] dirs = { parent + "/textures", parent + "/Textures", parent };
            string[] exts = { ".dds", ".png", ".tga", ".jpg", ".bmp" };
            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string filename in Directory.EnumerateFiles(dir))
                {
                    string fn = Path.GetFileNameWithoutExtension(filename);
                    string ext = Path.GetExtension(filename);
                    if (fn.Equals(textureName, StringComparison.OrdinalIgnoreCase) &&
                        exts.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        return filename.Replace('\\', '/');
                }
            }
            return null;
        }
    }

    internal sealed class EmdBone
    {
        public string Name;
        public int Parent;
        public int NodeID;
        public Matrix4x4 LocalMatrix;
    }
    internal sealed class EmdMaterial { public string Name; public string TextureName; }
    internal sealed class EmdWeight
    {
        public readonly int[] Indices = { -1, -1, -1, -1 };
        public readonly float[] Values = new float[4];
    }
    internal sealed class EmdGroup
    {
        public string Name;
        public int MaterialIndex;
        public Vector3[] Vertices;
        public int[] Triangles;
        public Vector2[] UV;
        public Color32[] Colors;
        public EmdWeight[] Weights;
    }
    internal sealed class EmdDocument
    {
        public readonly List<EmdBone> Bones = new List<EmdBone>();
        public readonly List<EmdMaterial> Materials = new List<EmdMaterial>();
        public readonly List<EmdGroup> Groups = new List<EmdGroup>();
        public int DeclaredPolygonCount;
        public int EmptyGroupCount;
        public int RecoveredPolygonCount;
        public int SkippedPolygonCount;
        public readonly List<string> ImportWarnings = new List<string>();
    }

    // Big endian byte reader with strict bounds checks (PS3 EMD format).
    internal sealed class BeReader
    {
        private readonly byte[] data;
        public int Position { get; private set; }
        public int Length => data.Length;
        public BeReader(byte[] data) { this.data = data; }
        public void Seek(long pos)
        {
            if (pos < 0 || pos > data.Length) throw new InvalidDataException("Invalid EMD file offset: 0x" + pos.ToString("X"));
            Position = (int)pos;
        }
        private void Require(int count)
        {
            if (count < 0 || (long)Position + count > data.Length)
                throw new EndOfStreamException("Unexpected end of EMD at 0x" + Position.ToString("X"));
        }
        public byte U8() { Require(1); return data[Position++]; }
        public ushort U16() { Require(2); return (ushort)((U8() << 8) | U8()); }
        public int I32() { return unchecked((int)U32()); }
        public uint U32() { return ((uint)U16() << 16) | U16(); }
        public float F32()
        {
            uint bits = U32();
            byte[] bytes = BitConverter.GetBytes(bits);
            return BitConverter.ToSingle(bytes, 0);
        }
        public float Half()
        {
            ushort bits = U16();
            uint sign = (uint)(bits & 0x8000) << 16;
            int exponent = (bits >> 10) & 0x1F;
            uint fraction = (uint)(bits & 0x3FF);
            uint raw;
            if (exponent == 0)
            {
                if (fraction == 0) raw = sign;
                else
                {
                    int power = -14;
                    while ((fraction & 0x400) == 0) { fraction <<= 1; power--; }
                    fraction &= 0x3FF;
                    raw = sign | ((uint)(power + 127) << 23) | (fraction << 13);
                }
            }
            else if (exponent == 31) raw = sign | 0x7F800000u | (fraction << 13);
            else raw = sign | ((uint)(exponent - 15 + 127) << 23) | (fraction << 13);
            return BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);
        }
        public string CString(int maxBytes = 256)
        {
            var bytes = new List<byte>();
            for (int i = 0; i < maxBytes; ++i)
            {
                byte b = U8();
                if (b == 0) break;
                bytes.Add(b);
            }
            try { return Encoding.GetEncoding(932).GetString(bytes.ToArray()); }
            catch { return Encoding.ASCII.GetString(bytes.ToArray()); }
        }
        public void Skip(int bytes) { Seek((long)Position + bytes); }
        public int SkipZeroWords()
        {
            int tries = 0;
            while (Position + 4 <= Length)
            {
                int ptr = Position;
                if (U32() != 0) { Position = ptr; return ptr; }
                if (++tries > 65536) break;
            }
            throw new InvalidDataException("Could not find a nonzero group/UV header.");
        }
    }

    internal static class EmdParser
    {
        private const int MaxEntries = 10000;
        private static void ValidateCount(int count, string label, int max = MaxEntries)
        {
            if (count < 0 || count > max) throw new InvalidDataException("Invalid " + label + " count: " + count);
        }

        public static EmdDocument Parse(byte[] data, bool recoverSupportedPolygons = false)
        {
            if (data.Length < 0x68) throw new InvalidDataException("EMD header is too short.");
            var r = new BeReader(data);
            var doc = new EmdDocument();
            r.Seek(0x24);
            int polygonCount = r.I32(); r.I32();
            int boneCount = r.I32(); int matCount = r.I32();
            r.I32(); r.I32(); r.I32(); r.I32(); r.I32(); r.I32();
            int polygonOffset = r.I32(); r.I32();
            int boneOffset = r.I32(); int materialOffset = r.I32();
            int uvOffset = r.I32(); r.I32(); r.I32();
            doc.DeclaredPolygonCount = polygonCount;
            ValidateCount(polygonCount, "polygon");
            ValidateCount(boneCount, "bone");
            ValidateCount(matCount, "material");
            r.Seek(boneOffset);
            for (int i = 0; i < boneCount; ++i)
            {
                int start = r.Position;
                r.U16(); r.U16(); int recordSize = r.I32();
                r.I32(); r.F32();
                string boneName = r.CString(32);
                r.Seek(start + 0x110);
                int parent = r.I32(); r.I32(); int nodeId = r.I32() + 1; r.I32();
                float[] f = new float[32];
                for (int k = 0; k < f.Length; ++k) f[k] = r.F32();
                // MaxScript matrix3 uses row vectors. Convert to Unity's column-vector matrix.
                Matrix4x4 m = Matrix4x4.identity;
                m.SetColumn(0, new Vector4(f[0], f[1], f[2], 0));
                m.SetColumn(1, new Vector4(f[4], f[5], f[6], 0));
                m.SetColumn(2, new Vector4(f[8], f[9], f[10], 0));
                m.SetColumn(3, new Vector4(f[12], f[13], f[14], 1));
                doc.Bones.Add(new EmdBone { Name = boneName, Parent = parent, NodeID = nodeId, LocalMatrix = m });
                // Original MaxScript consumes exactly 0x1A0 bytes per bone, regardless of header length.
                r.Seek(start + 0x1A0);
            }
            // The MaxScript reads the material blocks immediately after the bone records.
            // materialOffset is retained from the header as a fallback when there are no bones.
            if (boneCount == 0 && materialOffset > 0 && materialOffset < r.Length) r.Seek(materialOffset);
            for (int i = 0; i < matCount; ++i)
            {
                int start = r.Position;
                r.I32(); int blockSize = r.I32();
                if (blockSize < 0x230) throw new InvalidDataException("Material block is unexpectedly small at 0x" + start.ToString("X"));
                r.I32(); r.I32();
                string name = r.CString(255);
                r.Seek(start + 556);
                int subCount = r.I32();
                ValidateCount(subCount, "submaterial", 512);
                string tex = "null";
                r.Seek((long)start + blockSize);
                for (int sub = 0; sub < subCount; ++sub)
                {
                    int subStart = r.Position;
                    r.I32(); int subSize = r.I32();
                    if (subSize < 16) throw new InvalidDataException("Malformed submaterial at 0x" + subStart.ToString("X"));
                    r.I32(); r.I32();
                    string subName = r.CString(255);
                    if (sub == 0) tex = subName;
                    r.Seek((long)subStart + subSize);
                }
                doc.Materials.Add(new EmdMaterial { Name = name, TextureName = tex });
            }
            r.Seek(polygonOffset);
            for (int p = 0; p < polygonCount; ++p)
            {
                int start = r.Position;
                r.Skip(4);
                int recordSize = r.I32();
                if (recordSize < 0x140 || (long)start + recordSize > r.Length)
                    throw new InvalidDataException("Invalid polygon record size at 0x" + start.ToString("X") +
                        ": size=" + recordSize + ", totalFileBytes=" + r.Length);
                int groupsBeforePolygon = doc.Groups.Count;
                try
                {
                r.I32(); r.F32();
                string name = r.CString(32);
                r.Seek(start + 0x110);
                r.Skip(32); // bounding boxes
                int matIndex = r.U16();
                int headerType = r.U16();
                r.Skip(12);
                int groupCount = 1;
                if (headerType == 0x204)
                {
                    groupCount = r.U16(); r.U16(); r.Skip(12 + 16);
                }
                ValidateCount(groupCount, "mesh groups", 512);

                for (int g = 0; g < groupCount; ++g)
                {
                    if (g > 0) r.Skip(0x50);
                    int basePos = r.SkipZeroWords();
                    int indexJump = checked(basePos + r.I32());
                    int indexBytes = r.U16(); r.U16();
                    int vertexJump = checked(basePos + r.I32());
                    r.I32(); // reserved normal pointer
                    int vertexBytes = r.U16(); r.U16();
                    r.I32(); r.U16(); r.U16(); r.I32();
                    int uvLength = r.I32();
                    int node1 = r.U16() / 0x30;
                    int node2 = r.U16() / 0x30;
                    int node3 = r.U16() / 0x30;
                    r.U16(); r.U16(); r.U16();
                    int weightJump = checked(basePos + r.I32());
                    r.I32(); r.I32();
                    for (int k = 0; k < 7; ++k) r.I32();
                    r.I32(); r.I32(); r.I32();
                    r.I32(); r.I32(); r.I32(); r.I32();
                    r.I32(); r.I32(); r.I32();
                    r.U16(); r.U16(); r.U16(); r.U16();
                    int pointCount = r.U16();
                    // EMD stores the number of triangle *indices* here, not the number of faces.
                    // Each triangle contains three 16-bit indices; indexBytes also includes padding.
                    int declaredIndexCount = r.U16(); r.I32();
                    ValidateCount(pointCount, "vertices", 65535);
                    ValidateCount(declaredIndexCount, "triangle indices", 65535);
                    if (pointCount == 0)
                    {
                        doc.EmptyGroupCount++;
                        continue;
                    }

                    // Most character EMD files store an index count here.
                    // Stage records may instead store a face count (3 indices/face),
                    // or include a padded trailing index. Inspect the ACTUAL index
                    // buffer size, rather than always assuming triangles or clipping
                    // one usable index. Ambiguous topology remains an import error.
                    int indexCount = declaredIndexCount;
                    if (indexCount % 3 != 0)
                    {
                        int availableIndices = indexBytes / sizeof(ushort);
                        long possibleFaceIndices = (long)declaredIndexCount * 3L;
                        long expectedFaceBytes = possibleFaceIndices * sizeof(ushort);
                        int extraFaceBytes = (int)(indexBytes - expectedFaceBytes);
                        if (possibleFaceIndices > 0 &&
                            possibleFaceIndices <= 65535 &&
                            extraFaceBytes >= 0 && extraFaceBytes < 16)
                        {
                            // Matches a full triangle list with at most 15 padding bytes.
                            indexCount = (int)possibleFaceIndices;
                            doc.ImportWarnings.Add("EMD " + name +
                                ": nonstandard index field " + declaredIndexCount +
                                " interpreted as triangle count based on " +
                                indexBytes + "-byte index buffer (" + indexCount + " indices).");
                        }
                        else
                        {
                            int paddingCount = indexCount % 3;
                            bool looksPadded = indexBytes >= (long)indexCount * 2L &&
                                indexJump >= 0 &&
                                (long)indexJump + (long)indexCount * 2L <= r.Length;
                            if (looksPadded)
                            {
                                int returnPosition = r.Position;
                                r.Seek(indexJump + (long)(indexCount - paddingCount) * 2L);
                                for (int pad = 0; pad < paddingCount; ++pad)
                                {
                                    ushort marker = r.U16();
                                    if (marker != 0 && marker != 0xFFFF)
                                        looksPadded = false;
                                }
                                r.Seek(returnPosition);
                            }
                            if (looksPadded)
                            {
                                indexCount -= paddingCount;
                                doc.ImportWarnings.Add("EMD " + name +
                                    ": ignored " + paddingCount +
                                    " zero/FFFF index-buffer padding value(s) to form " +
                                    (indexCount / 3) + " complete triangles.");
                            }
                            else
                                throw new InvalidDataException(
                                    "Unsupported EMD index layout in " + name +
                                    ": declared field=" + declaredIndexCount +
                                    ", index buffer bytes=" + indexBytes +
                                    ", capacity=" + availableIndices +
                                    ". Could be a triangle strip or another topology; " +
                                    "no triangle reconstruction attempted.");
                        }
                    }
                    int uvStride = uvLength / pointCount;
                    if (uvStride * pointCount != uvLength || uvStride % 4 != 0 || uvStride < 8 || uvStride > 28)
                        throw new InvalidDataException("Unsupported UV stride " + uvStride + " (" + name + ")");
                    // Stage geometry such as AREA00/jimen_naka can use 28 bytes:
                    // 3 leading UV pairs (12 bytes), ARGB (4), 3 trailing UV pairs
                    // (12). The existing uvSize>=5 decoding branch already
                    // handles these records and selects the first trailing UV set.
                    // All earlier 8..24-byte layouts retain their prior behavior.
                    int uvSize = uvStride / 4;
                    if (vertexBytes / 12 < pointCount)
                        throw new InvalidDataException("Vertex buffer smaller than declared point count in " +
                            name + " / polygon " + p + " / group " + g +
                            ": declared=" + pointCount + " vertices (" + ((long)pointCount * 12L) +
                            " bytes), positionBuffer=" + vertexBytes +
                            " bytes, vertexOffset=0x" + vertexJump.ToString("X") +
                            ", uvLength=" + uvLength + " bytes, indexBytes=" + indexBytes +
                            ", polygonOffset=0x" + start.ToString("X") +
                            ". Geometry layout not confirmed; cannot safely decode this group.");
                    if (indexBytes / sizeof(ushort) < indexCount)
                        throw new InvalidDataException("Triangle index buffer too small in " + name +
                            " (declared " + indexCount + " indices / " + (indexCount * sizeof(ushort)) +
                            " bytes, available " + indexBytes + " bytes).");

                    r.Seek(vertexJump);
                    var vertices = new Vector3[pointCount];
                    for (int v = 0; v < pointCount; ++v)
                        vertices[v] = new Vector3(r.F32(), r.F32(), r.F32());
                    r.Seek(indexJump);
                    var triangles = new int[indexCount];
                    for (int f = 0; f < indexCount / 3; ++f)
                    {
                        int a = r.U16(), b = r.U16(), c = r.U16();
                        if (a >= pointCount || b >= pointCount || c >= pointCount)
                            throw new InvalidDataException("Triangle references invalid vertex in " + name);
                        // Mirrors the MaxScript Face_array [(f3),(f2),(f1)].
                        triangles[f * 3] = c;
                        triangles[f * 3 + 1] = b;
                        triangles[f * 3 + 2] = a;
                    }
                    r.Seek(uvOffset);
                    var uvs = new Vector2[pointCount];
                    var colors = new Color32[pointCount];
                    for (int v = 0; v < pointCount; ++v)
                    {
                        // UVsize 2,3,4: ARGB first, then 1-3 UVs; pick first UV.
                        // UVsize 5,6: 3 UVs first, ARGB, then 1-2 UVs; pick fourth UV.
                        if (uvSize <= 4)
                        {
                            byte a = r.U8(), red = r.U8(), green = r.U8(), blue = r.U8();
                            colors[v] = new Color32(red, green, blue, a);
                            for (int slot = 0; slot < uvSize - 1; ++slot)
                            {
                                // The source MaxScript half converter returns half the IEEE value;
                                // its subsequent multiply-by-two restores the normal UV.
                                float u = r.Half();
                                float t = 1f - r.Half();
                                if (slot == 0) uvs[v] = new Vector2(u, t);
                            }
                        }
                        else
                        {
                            for (int slot = 0; slot < 3; ++slot) { r.Half(); r.Half(); }
                            byte a = r.U8(), red = r.U8(), green = r.U8(), blue = r.U8();
                            colors[v] = new Color32(red, green, blue, a);
                            for (int slot = 0; slot < uvSize - 4; ++slot)
                            {
                                // The source MaxScript half converter returns half the IEEE value;
                                // its subsequent multiply-by-two restores the normal UV.
                                float u = r.Half();
                                float t = 1f - r.Half();
                                if (slot == 0) uvs[v] = new Vector2(u, t);
                            }
                        }
                    }
                    // Some files have no subsequent UV chunk after the final mesh.
                    // Advance when possible, otherwise leave the cursor at EOF.
                    try { uvOffset = r.SkipZeroWords(); }
                    catch (InvalidDataException) { uvOffset = r.Position; }

                    EmdWeight[] weights = new EmdWeight[doc.Bones.Count > 0 ? pointCount : 0];
                    int nextGroupPosition = r.Position;
                    if (doc.Bones.Count > 0)
                    {
                        var boneIdToIndex = new Dictionary<int, int>();
                        for (int i = 0; i < doc.Bones.Count; ++i)
                            if (!boneIdToIndex.ContainsKey(doc.Bones[i].NodeID)) boneIdToIndex.Add(doc.Bones[i].NodeID, i);
                        r.Seek(weightJump);
                        for (int v = 0; v < pointCount; ++v)
                        {
                            var weight = new EmdWeight();
                            for (int slot = 0; slot < 4; ++slot)
                            {
                                weight.Values[slot] = r.U8() / 255f;
                                int rawId = r.U8() + 1;
                                int nodeId = rawId + node1;
                                if (rawId >= node3 + 1) nodeId = rawId + node2 - node3;
                                weight.Indices[slot] = boneIdToIndex.TryGetValue(nodeId, out int found) ? found : -1;
                            }
                            weights[v] = weight;
                        }
                        nextGroupPosition = r.Position;
                    }
                    doc.Groups.Add(new EmdGroup
                    {
                        Name = name + (groupCount > 1 ? "_" + g : ""),
                        MaterialIndex = matIndex,
                        Vertices = vertices, Triangles = triangles,
                        UV = uvs, Colors = colors, Weights = weights
                    });
                    r.Seek(nextGroupPosition);
                }
                if (doc.Groups.Count > groupsBeforePolygon)
                    doc.RecoveredPolygonCount++;
                }
                catch (InvalidDataException ex)
                {
                    if (!recoverSupportedPolygons) throw;
                    doc.SkippedPolygonCount++;
                    doc.ImportWarnings.Add("EMD skipped unsupported polygon #" + p +
                        " at 0x" + start.ToString("X") + ": " + ex.Message +
                        " [Recover Supported Meshes enabled]");
                }
                catch (EndOfStreamException ex)
                {
                    if (!recoverSupportedPolygons) throw;
                    doc.SkippedPolygonCount++;
                    doc.ImportWarnings.Add("EMD skipped truncated polygon #" + p +
                        " at 0x" + start.ToString("X") + ": " + ex.Message +
                        " [Recover Supported Meshes enabled]");
                }
                catch (OverflowException ex)
                {
                    if (!recoverSupportedPolygons) throw;
                    doc.SkippedPolygonCount++;
                    doc.ImportWarnings.Add("EMD skipped polygon with invalid numeric offsets #" + p +
                        " at 0x" + start.ToString("X") + ": " + ex.Message +
                        " [Recover Supported Meshes enabled]");
                }
                // Polygon header's record size is the only trusted boundary to
                // resume from after failure. Do not infer group boundaries.
                r.Seek((long)start + recordSize);
            }
            if (recoverSupportedPolygons && doc.SkippedPolygonCount > 0)
                doc.ImportWarnings.Add("EMD recovery summary: " +
                    doc.Groups.Count + " mesh group(s) decoded, " +
                    doc.RecoveredPolygonCount + " polygon record(s) decoded, " +
                    doc.SkippedPolygonCount + " polygon record(s) skipped. " +
                    "Unsupported data was not converted to geometry.");
            return doc;
        }
    }
}
#endif
