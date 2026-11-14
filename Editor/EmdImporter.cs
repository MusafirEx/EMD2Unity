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
    [Serializable]
    internal sealed class EmdUvChannelOverride
    {
        // Exact mesh name from the UV diagnostic (for example obj_00_0).
        public string meshName = "";
        [Range(0, 6)] public int sourceChannel;
    }

    [ScriptedImporter(23, "emd")]
    public sealed class EmdImporter : ScriptedImporter
    {
        // LegacySwapYZ preserves earlier imports. RotateMinus90X is a true
        // right-handed 90-degree basis rotation from Z-up to Y-up.
        private enum UpAxisConversion
        {
            LegacySwapYZ = 0,
            RotateMinus90X = 1
        }

        [Header("Model")]
        [SerializeField, Min(0.00001f)] private float modelScale = 1f;
        [SerializeField] private bool swapYAndZ = true;
        [SerializeField] private UpAxisConversion upAxisConversion = UpAxisConversion.LegacySwapYZ;
        [SerializeField] private bool mirrorX = false;
        [SerializeField] private bool reverseWinding = false;
        [SerializeField] private bool useVertexColors = true;
        // Stage models inspected so far frequently store vertex colors as
        // RGBA (with alpha FF in the fourth byte), while the MaxScript
        // reference assumes ARGB. Keep ARGB as the default for compatibility.
        [SerializeField] private bool vertexColorsRgbaLast = false;
        [SerializeField] private bool recoverSupportedPolygons = false;
        // Off retains the original MaxScript-compatible zero-padding scan.
        // On preserves leading zero words in the following UV block.
        [SerializeField] private bool preserveZeroUvWords = false;
        // Some stage EMD UV streams pad individual vertex-coordinate blocks
        // to an absolute 16-byte file boundary. The padding can contain
        // nonzero bytes, so SkipZeroWords cannot reliably cross it.
        // Opt-in per EMD until validated against all source layouts.
        [SerializeField] private bool alignUvBlocksTo16 = false;
        [SerializeField] private List<EmdUvChannelOverride> uvChannelOverrides =
            new List<EmdUvChannelOverride>();
        // Default false is important for previously imported .emd.meta files
        // that do not yet contain this setting. STG defaults must still apply
        // after the package is upgraded; users can opt out on each asset.
        [SerializeField] private bool customizeStageImport = false;

        // Recognize STG resources by folder rather than just AREA file names:
        // some valid stage assets have other names (e.g. jimen_naka).
        // Folder segments are case-insensitive and safe for Windows paths.
        internal static bool IsStageEmdPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string normalized = "/" + path.Replace('\\', '/').TrimStart('/');
            if (normalized.IndexOf("/stg/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            string[] segments = normalized.Split('/');
            for (int i = 1; i < segments.Length - 1; ++i)
            {
                string segment = segments[i];
                if (segment.Length > 3 &&
                    segment.StartsWith("STG", StringComparison.OrdinalIgnoreCase) &&
                    segment.Substring(3).All(char.IsDigit))
                    return true;
            }
            return false;
        }

        internal bool IsUsingStageDefaults =>
            !customizeStageImport && IsStageEmdPath(assetPath);

        private bool EffectiveSwapYAndZ => !IsUsingStageDefaults && swapYAndZ;
        // STG assets default to mirrored X with the preset enabled.
        // Manual model settings are preserved, and CHR/CSD still default off.
        private bool EffectiveMirrorX => IsUsingStageDefaults || mirrorX;
        // An axis exchange or a single reflection changes triangle parity.
        // A true 90-degree rotation does not. XOR makes independent mirrors compose.
        private bool EffectiveNegativeParity =>
            EffectiveMirrorX ^ (EffectiveSwapYAndZ &&
                upAxisConversion == UpAxisConversion.LegacySwapYZ);
        private bool EffectiveUseVertexColors => IsUsingStageDefaults || useVertexColors;
        internal bool EffectiveVertexColorsRgbaLast =>
            IsUsingStageDefaults || vertexColorsRgbaLast;
        internal bool EffectiveAlignUvBlocksTo16 =>
            IsUsingStageDefaults || alignUvBlocksTo16;
        internal bool EffectivePreserveZeroUvWords =>
            !IsUsingStageDefaults && preserveZeroUvWords;
        private bool UseManualUvChannelOverrides => !IsUsingStageDefaults;

        [Header("Rig")]
        [SerializeField] private bool importSkeleton = true;
        [SerializeField] private bool repairDegenerateBoneAxes = true;
        [Header("Materials")]
        [SerializeField] private bool findNearbyTextures = true;
        [SerializeField] private bool useEmatTextureReferences = true;
        [SerializeField] private bool preferUniqueColourMap = true;
        [SerializeField] private bool linkNormalMaps = false;
        [SerializeField] private bool linkSpecularMaps = false;
        [Tooltip("Uniform smoothness for all generated EMD materials (0 = rough/matte, 1 = glossy). Applies on import/reimport; material shaders without a smoothness property are unaffected.")]
        [SerializeField, Range(0f, 1f)] private float materialSmoothness = 0.5f;
        // Per-source-material references to editable external .mat assets. These
        // references persist in the EMD .meta and survive material renames/moves.
        // Empty slots keep the original generated embedded material.
        [SerializeField] private List<Material> externalMaterials = new List<Material>();
        private const string SourceMaterialIndexTag = "EMD2Unity_SourceMaterialIndex";

        // Explicit Inspector action only. Never create .mat assets from OnImportAsset:
        // writing assets during a ScriptedImporter callback causes import loops.
        internal void ExtractMaterialsToFolder(out int created, out int reused, out int failed)
        {
            created = 0;
            reused = 0;
            failed = 0;
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(".emd", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select a source .emd file to extract materials.");

            // Read the already imported EMD MATERIAL TABLE snapshot only.
            // Do NOT parse mesh/polygon data, inspect renderers or use mesh
            // names as material references during extraction.
            EmdMaterialRegistry registry = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<EmdMaterialRegistry>().FirstOrDefault();
            if (registry == null || registry.generatedMaterials == null ||
                registry.sourceMaterialNames == null ||
                registry.generatedMaterials.Length == 0 ||
                registry.generatedMaterials.Length != registry.sourceMaterialNames.Length)
                throw new InvalidOperationException(
                    "The EMD material table is not available. Reimport this .emd " +
                    "using EMD2Unity v0.6.6+, then extract its materials.");

            string modelFolder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(modelFolder) || !AssetDatabase.IsValidFolder(modelFolder))
                throw new DirectoryNotFoundException("Model directory not found for " + assetPath);
            string materialFolder = modelFolder + "/material";
            if (!AssetDatabase.IsValidFolder(materialFolder))
            {
                string guid = AssetDatabase.CreateFolder(modelFolder, "material");
                if (string.IsNullOrEmpty(guid) ||
                    !AssetDatabase.IsValidFolder(materialFolder))
                    throw new IOException("Unable to create material folder: " + materialFolder);
            }

            // Only generated EMD material templates from the source index table
            // are used; not MeshRenderer, SkinnedMeshRenderer, MeshFilter or mesh names.
            while (externalMaterials.Count < registry.generatedMaterials.Length)
                externalMaterials.Add(null);

            string modelName = Path.GetFileNameWithoutExtension(assetPath);
            for (int i = 0; i < registry.generatedMaterials.Length; i++)
            {
                string originalName = registry.sourceMaterialNames[i];
                if (string.IsNullOrWhiteSpace(originalName))
                    originalName = "EMD_Material_" + i;
                string safeName = Regex.Replace(originalName, @"[^A-Za-z0-9_-]+", "_")
                    .Trim('_');
                if (safeName.Length == 0) safeName = "Material";
                if (safeName.Length > 48) safeName = safeName.Substring(0, 48);
                string targetPath = materialFolder + "/" + modelName + "_Mat_" +
                    i.ToString("D3") + "_" + safeName + ".mat";

                // A manually assigned external .mat may have been renamed or
                // moved. Keep its GUID-backed reference on repeated extraction.
                Material existingLink = externalMaterials[i];
                string existingLinkPath = existingLink == null ? "" :
                    AssetDatabase.GetAssetPath(existingLink);
                if (!string.IsNullOrEmpty(existingLinkPath) &&
                    existingLinkPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                {
                    reused++;
                    continue;
                }

                // Reuse files at the canonical destination, never overwrite
                // shader, maps, smoothness, colors, keywords, or render queue.
                Material targetMaterial = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
                if (targetMaterial != null)
                {
                    externalMaterials[i] = targetMaterial;
                    reused++;
                    continue;
                }
                if (File.Exists(targetPath))
                {
                    failed++;
                    Debug.LogWarning("EMD2Unity: cannot export material to existing non-Material asset " +
                        targetPath + "; existing file was not modified.");
                    continue;
                }

                // The registry indexes materials directly from the .emd source
                // Material[] table, not mesh materials, names or hierarchy.
                Material source = registry.generatedMaterials[i];
                if (source != null &&
                    source.GetTag(SourceMaterialIndexTag, false, "") != i.ToString())
                    source = null;
                if (source == null)
                {
                    failed++;
                    Debug.LogWarning("EMD2Unity: no embedded material available for " +
                        modelName + " source material #" + i + " (" + originalName +
                        "). Reimport the EMD and retry extraction.");
                    continue;
                }
                Material clone = null;
                try
                {
                    clone = new Material(source) { name = originalName };
                    AssetDatabase.CreateAsset(clone, targetPath);
                    externalMaterials[i] = clone;
                    created++;
                }
                catch (Exception ex)
                {
                    failed++;
                    if (clone != null && !AssetDatabase.Contains(clone))
                        UnityEngine.Object.DestroyImmediate(clone);
                    Debug.LogWarning("EMD2Unity: could not extract material #" + i +
                        " to " + targetPath + ": " + ex.Message);
                }
            }
            // The Inspector calls SaveAndReimport afterwards to make renderers
            // use these external references without touching the .mat contents.
            EditorUtility.SetDirty(this);
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            try
            {
                byte[] data = File.ReadAllBytes(ctx.assetPath);
                var document = EmdParser.Parse(data, recoverSupportedPolygons, EffectivePreserveZeroUvWords,
                    EffectiveAlignUvBlocksTo16, EffectiveVertexColorsRgbaLast);
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

        private Vector3 ConvertBasisVector(Vector3 v)
        {
            float x = EffectiveMirrorX ? -v.x : v.x;
            if (EffectiveSwapYAndZ)
                return new Vector3(x, v.z,
                    upAxisConversion == UpAxisConversion.RotateMinus90X ? -v.y : v.y);
            return new Vector3(x, v.y, v.z);
        }

        private Vector3 ConvertVector(Vector3 v) => ConvertBasisVector(v) * modelScale;

        // PEMT must follow the EFFECTIVE mesh/skeleton basis, including STG
        // preset overrides, rather than reading the raw swapYAndZ field.
        internal Vector3 ConvertPemtPositionDelta(Vector3 sourceDelta) =>
            ConvertVector(sourceDelta);

        internal Quaternion ConvertPemtRotationDelta(Quaternion sourceDelta)
        {
            // For any orthogonal basis B, B*R(q)*B^-1 corresponds to
            // q'=(det(B)*B*(q.x,q.y,q.z), q.w). Negative determinant matters:
            // the legacy Y/Z swap and mirror-X are reflections, not rotations.
            Vector3 vector = ConvertBasisVector(
                new Vector3(sourceDelta.x, sourceDelta.y, sourceDelta.z));
            if (EffectiveNegativeParity) vector = -vector;
            return new Quaternion(vector.x, vector.y, vector.z, sourceDelta.w);
        }

        internal Vector3 ConvertPemtScaleFactors(Vector3 sourceFactors)
        {
            // Local scale multipliers are axis quantities, not signed vectors;
            // mirroring an axis does not negate its scale multiplier. 0x22
            // remains a preview-only, unverified experimental interpretation.
            return EffectiveSwapYAndZ ?
                new Vector3(sourceFactors.x, sourceFactors.z, sourceFactors.y) :
                sourceFactors;
        }

        private Matrix4x4 ConvertBoneMatrix(Matrix4x4 source)
        {
            Matrix4x4 basis = Matrix4x4.identity;
            if (EffectiveMirrorX) basis.m00 = -1f;
            if (EffectiveSwapYAndZ)
            {
                basis.m11 = 0f; basis.m22 = 0f;
                basis.m12 = 1f;
                basis.m21 = upAxisConversion == UpAxisConversion.RotateMinus90X ? -1f : 1f;
            }
            // All supported bases are orthogonal: inverse(basis) = transpose(basis).
            // Conjugation changes the coordinate frame without breaking bone hierarchy.
            source = basis * source * basis.transpose;
            source.m03 *= modelScale;
            source.m13 *= modelScale;
            source.m23 *= modelScale;
            return source;
        }

        private static bool FiniteAxis(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static Vector3 PerpendicularUnit(Vector3 unitAxis)
        {
            Vector3 reference = Mathf.Abs(unitAxis.y) < 0.9f ? Vector3.up : Vector3.right;
            return (reference - unitAxis * Vector3.Dot(reference, unitAxis)).normalized;
        }

        // Returns a warning description only when a synthetic frame was needed.
        // Never alter complete, nondegenerate source transforms.
        private static string SetLocalMatrix(Transform transform, Matrix4x4 matrix,
            bool repairMissingAxes)
        {
            const float MinLength = 1e-6f;
            Vector3 right = new Vector3(matrix.m00, matrix.m10, matrix.m20);
            Vector3 up = new Vector3(matrix.m01, matrix.m11, matrix.m21);
            Vector3 forward = new Vector3(matrix.m02, matrix.m12, matrix.m22);
            Vector3 position = new Vector3(matrix.m03, matrix.m13, matrix.m23);
            if (!FiniteAxis(right) || !FiniteAxis(up) ||
                !FiniteAxis(forward) || !FiniteAxis(position))
                throw new InvalidDataException("Bone matrix has non-finite values; cannot reconstruct axes.");

            float xLen = right.magnitude, yLen = up.magnitude, zLen = forward.magnitude;
            if (float.IsInfinity(xLen) || float.IsInfinity(yLen) || float.IsInfinity(zLen))
                throw new InvalidDataException("Bone matrix axis magnitude is not finite.");

            bool missingX = xLen < MinLength;
            bool missingY = yLen < MinLength;
            bool missingZ = zLen < MinLength;
            int missingCount = (missingX ? 1 : 0) + (missingY ? 1 : 0) +
                               (missingZ ? 1 : 0);
            string repairDescription = null;
            if (missingCount > 0)
            {
                string names = (missingX ? "X " : "") +
                               (missingY ? "Y " : "") +
                               (missingZ ? "Z " : "");
                if (!repairMissingAxes)
                    throw new InvalidDataException(
                        "Bone transform has zero-length axis: " + names.Trim() +
                        " (X=" + xLen + ", Y=" + yLen + ", Z=" + zLen + "). " +
                        "Enable Repair Missing Bone Axes on the Rig tab to use synthetic axes.");

                // A single missing axis is recovered from the other two if
                // their directions span a plane. Cross products choose a
                // right-handed basis; the original missing-axis sign is unknown.
                bool reconstructed = false;
                if (missingCount == 1)
                {
                    Vector3 cross = missingX ? Vector3.Cross(up / yLen, forward / zLen) :
                                    missingY ? Vector3.Cross(forward / zLen, right / xLen) :
                                               Vector3.Cross(right / xLen, up / yLen);
                    if (cross.sqrMagnitude > 1e-8f)
                    {
                        if (missingX) right = cross.normalized;
                        else if (missingY) up = cross.normalized;
                        else forward = cross.normalized;
                        reconstructed = true;
                        repairDescription = "cross-product reconstruction (unit-length replacement)";
                    }
                }

                if (!reconstructed)
                {
                    // Two/three missing axes, or near-parallel valid axes:
                    // keep one known direction as an anchor and construct
                    // a unit right-handed reference frame around it.
                    if (!missingZ)
                    {
                        Vector3 z = forward / zLen;
                        Vector3 y = PerpendicularUnit(z);
                        right = Vector3.Cross(y, z);
                        up = y;
                        repairDescription = "fallback orthogonal frame anchored to Z";
                    }
                    else if (!missingY)
                    {
                        Vector3 y = up / yLen;
                        Vector3 z = PerpendicularUnit(y);
                        right = Vector3.Cross(y, z);
                        forward = z;
                        repairDescription = "fallback orthogonal frame anchored to Y";
                    }
                    else if (!missingX)
                    {
                        Vector3 x = right / xLen;
                        Vector3 y = PerpendicularUnit(x);
                        up = y;
                        forward = Vector3.Cross(x, y);
                        repairDescription = "fallback orthogonal frame anchored to X";
                    }
                    else
                    {
                        right = Vector3.right;
                        up = Vector3.up;
                        forward = Vector3.forward;
                        repairDescription = "identity fallback (all three axes missing)";
                    }
                }
                repairDescription = "original missing axes [" + names.Trim() + "]; " +
                    repairDescription + "; original magnitudes X=" +
                    xLen.ToString("G5") + " Y=" + yLen.ToString("G5") +
                    " Z=" + zLen.ToString("G5") +
                    ". Reconstructed axes/orientation require visual validation.";
            }

            Vector3 scale = new Vector3(right.magnitude, up.magnitude, forward.magnitude);
            if (scale.x < MinLength || scale.y < MinLength || scale.z < MinLength)
                throw new InvalidDataException("Bone axes could not be reconstructed.");
            if (Vector3.Dot(Vector3.Cross(right, up), forward) < 0f)
            {
                scale.x = -scale.x;
                right = -right;
            }
            transform.localPosition = position;
            transform.localRotation = Quaternion.LookRotation(forward / scale.z, up / scale.y);
            transform.localScale = scale;
            return repairDescription;
        }

        private void CreateUnityAssets(AssetImportContext ctx, EmdDocument doc)
        {
            string name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            GameObject root = new GameObject(name);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Count normal-link outcomes per source EMD so missing/incorrect
            // TextureImporter settings are visible without flooding the Console.
            int normalBoundCount = 0;
            int normalUnpreparedCount = 0;
            int normalUnloadableCount = 0;
            int normalShaderUnsupportedCount = 0;
            int normalMissingCount = 0;
            var materials = new Material[doc.Materials.Count];
            var generatedMaterials = new Material[doc.Materials.Count];
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
                // Stable mapping for extracting even duplicate-named submaterials.
                mat.SetOverrideTag(SourceMaterialIndexTag, i.ToString());
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
                    if (preferUniqueColourMap && IsAuxiliaryTextureName(texName))
                    {
                        var candidates = info.SubTextureNames.Concat(ematTextures)
                            .Where(t => !string.IsNullOrEmpty(t) &&
                                t.EndsWith("_CO", StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        string matchedColour = FindMatchingMaterialColour(info.Name, candidates);
                        string chosenColour = candidates.Count == 1 ? candidates[0] : matchedColour;
                        if (!string.IsNullOrEmpty(chosenColour))
                        {
                            // Only switch to an explicitly referenced texture
                            // that Unity can actually load (no unimportable DDS).
                            bool loadable = FindTextureCandidates(ctx.assetPath, chosenColour)
                                .Any(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p) != null);
                            if (loadable)
                            {
                                ctx.LogImportWarning("EMD material '" + info.Name +
                                    "' switched diffuse from auxiliary '" + texName +
                                    "' to " + (candidates.Count == 1 ?
                                        "unique" : "material-name-matched") +
                                    " referenced colour '" + chosenColour +
                                    "'. For layered STG materials this is only a " +
                                    "single-texture fallback. Disable Prefer Referenced " +
                                    "_CO for Diffuse to restore the original binding.");
                                texName = chosenColour;
                            }
                            else
                                ctx.LogImportWarning("EMD material '" + info.Name +
                                    "' has referenced colour '" + chosenColour +
                                    "' but it is not a loadable local Texture2D. " +
                                    "Kept the original auxiliary binding.");
                        }
                        else if (candidates.Count > 1)
                        {
                            ctx.LogImportWarning("EMD material '" + info.Name +
                                "' has " + candidates.Count + " referenced _CO variants " +
                                "without an exact material-stem match: " +
                                string.Join(", ", candidates.ToArray()) +
                                ". Kept original binding to avoid guessing. " +
                                "Use Diagnose EMD Texture Bindings.");
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
                    if (diffuse == null && !string.IsNullOrWhiteSpace(texName) && texName != "null")
                        ctx.LogImportWarning("EMD material '" + info.Name +
                            "' could not load diffuse Texture2D '" + texName +
                            "' from the model's local texture folders.");
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
                            // EMAT is optional in some character archives. Match a
                            // local normal map by the EXACT selected colour-map stem,
                            // never by an unrelated material or the first *_NO file.
                            string normalName = stem + "_NO";
                            List<string> candidates = FindTextureCandidates(ctx.assetPath, normalName);
                            if (candidates.Count == 0)
                            {
                                normalMissingCount++;
                            }
                            else
                            {
                                bool linked = false;
                                bool needsPreparation = false;
                                bool unsupportedShader = false;
                                foreach (string normalPath in candidates)
                                {
                                    ctx.DependsOnSourceAsset(normalPath);
                                    var textureImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
                                    // Never connect a raw colour-imported DDS to a
                                    // tangent-space normal slot: Unity must first
                                    // import it as TextureImporterType.NormalMap.
                                    if (textureImporter == null ||
                                        textureImporter.textureType != TextureImporterType.NormalMap)
                                    {
                                        needsPreparation = true;
                                        continue;
                                    }
                                    Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                                    if (normal == null) continue;

                                    bool assigned = false;
                                    if (mat.HasProperty("_BumpMap"))
                                    {
                                        mat.SetTexture("_BumpMap", normal); // Built-in Standard / URP Lit
                                        assigned = true;
                                    }
                                    if (mat.HasProperty("_NormalMap"))
                                    {
                                        mat.SetTexture("_NormalMap", normal); // HDRP Lit
                                        assigned = true;
                                    }
                                    if (!assigned)
                                    {
                                        unsupportedShader = true;
                                        break;
                                    }
                                    if (mat.HasProperty("_BumpScale"))
                                        mat.SetFloat("_BumpScale", 1f);
                                    if (mat.HasProperty("_NormalScale"))
                                        mat.SetFloat("_NormalScale", 1f);
                                    mat.EnableKeyword("_NORMALMAP");
                                    if (mat.HasProperty("_NormalMap"))
                                        mat.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
                                    linked = true;
                                    break;
                                }
                                if (linked) normalBoundCount++;
                                else if (unsupportedShader) normalShaderUnsupportedCount++;
                                else if (needsPreparation) normalUnpreparedCount++;
                                else normalUnloadableCount++;
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
                // The same importer-owned value applies across all EMD material
                // sub-assets. Standard uses _Glossiness; URP/HDRP Lit use
                // _Smoothness. When Standard uses a specular/gloss texture,
                // _GlossMapScale controls the map's gloss contribution.
                // Skip Unlit/other shaders that do not expose these properties.
                float smoothness = Mathf.Clamp01(materialSmoothness);
                if (mat.HasProperty("_Glossiness"))
                    mat.SetFloat("_Glossiness", smoothness);
                if (mat.HasProperty("_Smoothness"))
                    mat.SetFloat("_Smoothness", smoothness);
                if (mat.HasProperty("_GlossMapScale"))
                    mat.SetFloat("_GlossMapScale", smoothness);
                Material linked = externalMaterials != null && i < externalMaterials.Count
                    ? externalMaterials[i] : null;
                if (linked != null)
                {
                    // Reference the editable external asset; do not copy any
                    // imported defaults over user customizations on reimport.
                    string linkedPath = AssetDatabase.GetAssetPath(linked);
                    if (!string.IsNullOrEmpty(linkedPath) && linkedPath != ctx.assetPath)
                    {
                        ctx.DependsOnArtifact(linkedPath);
                        materials[i] = linked;
                    }
                    else
                        materials[i] = mat;
                }
                else
                    materials[i] = mat;
                // Keep stable generated subassets as extraction sources and
                // fallback when an external material is unassigned/missing.
                generatedMaterials[i] = mat;
                ctx.AddObjectToAsset("material_" + i, mat);
            }

            // Snapshot the EMD material declaration order once during import.
            // Extraction reads ONLY this registry: it never reads mesh geometry,
            // renderer material arrays or scene objects as material evidence.
            var materialRegistry = ScriptableObject.CreateInstance<EmdMaterialRegistry>();
            materialRegistry.name = name + " [EMD Material Table]";
            materialRegistry.generatedMaterials = generatedMaterials;
            materialRegistry.sourceMaterialNames = doc.Materials
                .Select(m => m.Name ?? "").ToArray();
            ctx.AddObjectToAsset("material_registry", materialRegistry);

            if (findNearbyTextures && linkNormalMaps)
            {
                string summary = "EMD2Unity normal map binding for " + name + ": " +
                    normalBoundCount + " linked, " +
                    normalUnpreparedCount + " need Normal Map import preparation, " +
                    normalUnloadableCount + " files could not load, " +
                    normalShaderUnsupportedCount + " shader(s) lack a normal slot, " +
                    normalMissingCount + " materials have no exact local *_NO match. " +
                    "Matching uses the selected *_CO texture stem even without an EMAT reference.";
                if (normalUnpreparedCount > 0 || normalUnloadableCount > 0 ||
                    normalShaderUnsupportedCount > 0 || normalBoundCount == 0)
                    ctx.LogImportWarning(summary +
                        " If the matching *_NO exists, use Materials > " +
                        "Prepare Nearby _NO Textures as Normal Maps, then Apply.");
                else
                    Debug.Log(summary);
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
                    try
                    {
                        string boneRepair = SetLocalMatrix(
                            bones[i], ConvertBoneMatrix(doc.Bones[i].LocalMatrix),
                            repairDegenerateBoneAxes);
                        if (!string.IsNullOrEmpty(boneRepair))
                            doc.ImportWarnings.Add("EMD synthesized bone transform #" + i +
                                " (node ID " + doc.Bones[i].NodeID +
                                ", name '" + bones[i].name + "'): " + boneRepair);
                    }
                    catch (InvalidDataException ex)
                    {
                        throw new InvalidDataException(
                            "Bone #" + i + " (node ID " + doc.Bones[i].NodeID +
                            ", name '" + bones[i].name + "'): " + ex.Message, ex);
                    }
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
                if (reverseWinding ^ EffectiveNegativeParity)
                    for (int t = 0; t < triangles.Length; t += 3)
                        { int temp = triangles[t]; triangles[t] = triangles[t + 2]; triangles[t + 2] = temp; }
                mesh.triangles = triangles;
                Vector2[] chosenUv = info.UV;
                string meshName = info.Name + "_" + i;
                for (int ruleIndex = 0; ruleIndex <
                    (!UseManualUvChannelOverrides || uvChannelOverrides == null ?
                        0 : uvChannelOverrides.Count); ruleIndex++)
                {
                    EmdUvChannelOverride rule = uvChannelOverrides[ruleIndex];
                    if (rule == null || string.IsNullOrWhiteSpace(rule.meshName) ||
                        (!rule.meshName.Equals(meshName, StringComparison.OrdinalIgnoreCase) &&
                         !rule.meshName.Equals(info.Name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (info.SourceUvChannels != null && rule.sourceChannel >= 0 &&
                        rule.sourceChannel < info.SourceUvChannels.Length)
                        chosenUv = info.SourceUvChannels[rule.sourceChannel];
                    else
                        ctx.LogImportWarning("EMD UV override for '" + meshName +
                            "' requests unavailable source channel " + rule.sourceChannel +
                            ". Available: 0.." + (info.SourceUvChannels == null ?
                                -1 : info.SourceUvChannels.Length - 1) +
                            ". Keeping original UV0.");
                    break;
                }
                if (chosenUv != null && chosenUv.Length == verts.Length) mesh.uv = chosenUv;
                // Preserve source channels in UV1..UV6; UV0 is the
                // legacy-selected (or explicitly overridden) main texture UV.
                if (info.SourceUvChannels != null)
                    for (int uvChannel = 0; uvChannel < info.SourceUvChannels.Length && uvChannel < 7; uvChannel++)
                        if (info.SourceUvChannels[uvChannel] != null &&
                            info.SourceUvChannels[uvChannel].Length == verts.Length)
                            mesh.SetUVs(uvChannel + 1, info.SourceUvChannels[uvChannel]);
                if (EffectiveUseVertexColors && info.Colors.Length == verts.Length) mesh.colors32 = info.Colors;
                mesh.RecalculateNormals();
                if (chosenUv != null && chosenUv.Length == verts.Length) mesh.RecalculateTangents();
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

        // EMD normal preparation is an explicit Inspector action, never run
        // during OnImportAsset (which would create nested imports).
        // Unity 2022.3 uses IHVImageFormatImporter for DDS: it has no
        // TextureImporterType.NormalMap setting. Generate a non-destructive
        // PNG companion instead, and configure that PNG as a Unity Normal Map.
        private static bool TryCreateDdsNormalPng(string ddsPath, out string pngPath)
        {
            pngPath = Path.ChangeExtension(ddsPath, ".png").Replace('\\', '/');
            // Never overwrite user-created PNGs or repeated conversions.
            if (File.Exists(pngPath)) return true;
            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(ddsPath);
            if (source == null || source.width <= 0 || source.height <= 0 ||
                source.width > 8192 || source.height > 8192)
            {
                Debug.LogWarning("EMD2Unity: cannot convert DDS normal map " + ddsPath +
                    " to a PNG companion (unloadable or dimensions exceed 8192).");
                return false;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            Texture2D pixels = null;
            try
            {
                // GPU readback works with many hardware-compressed DDS formats
                // without changing readability or the original DDS source.
                target = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                pixels = new Texture2D(source.width, source.height,
                    TextureFormat.RGBA32, false, true);
                pixels.ReadPixels(new Rect(0, 0, source.width, source.height),
                    0, 0, false);
                pixels.Apply(false, false);
                byte[] encoded = pixels.EncodeToPNG();
                if (encoded == null || encoded.Length == 0)
                    throw new IOException("PNG encoder produced no bytes.");
                File.WriteAllBytes(pngPath, encoded);
                AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
                Debug.Log("EMD2Unity: created editable normal-map companion " +
                    pngPath + " from " + ddsPath +
                    ". Original DDS was not modified. Inspect normal orientation visually.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("EMD2Unity: DDS normal map conversion failed for " +
                    ddsPath + ": " + ex.Message);
                // Do not leave a damaged partially written source asset.
                if (File.Exists(pngPath) &&
                    AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath) == null)
                    AssetDatabase.DeleteAsset(pngPath);
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        // Prepare adjacent *_NO textures. PNG/TGA/etc use TextureImporter,
        // but DDS imported through IHVImageFormatImporter cannot set TextureType.
        // For those, generate a sibling *_NO.png normal asset on explicit click.
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
                // Snapshot before creating PNG files to avoid mutating a live
                // Directory.EnumerateFiles iterator during asset import.
                string[] files = Directory.GetFiles(directory);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    if (!Path.GetFileNameWithoutExtension(file).EndsWith("_NO",
                        StringComparison.OrdinalIgnoreCase)) continue;
                    if (!extensions.Contains(Path.GetExtension(file),
                        StringComparer.OrdinalIgnoreCase)) continue;
                    string assetPath = file.Replace('\\', '/');
                    if (!visited.Add(assetPath)) continue;
                    AssetImporter importer = AssetImporter.GetAtPath(assetPath);
                    if (importer is TextureImporter imageImporter)
                    {
                        matched++;
                        if (imageImporter.textureType == TextureImporterType.NormalMap)
                            continue;
                        imageImporter.textureType = TextureImporterType.NormalMap;
                        imageImporter.sRGBTexture = false;
                        imageImporter.SaveAndReimport();
                        updated++;
                        continue;
                    }
                    if (!(importer is IHVImageFormatImporter) ||
                        !assetPath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                        continue;

                    matched++;
                    if (!TryCreateDdsNormalPng(assetPath, out string pngPath))
                        continue;
                    var pngImporter = AssetImporter.GetAtPath(pngPath) as TextureImporter;
                    if (pngImporter == null)
                    {
                        Debug.LogWarning("EMD2Unity: generated PNG has no TextureImporter: " +
                            pngPath);
                        continue;
                    }
                    if (pngImporter.textureType != TextureImporterType.NormalMap)
                    {
                        pngImporter.textureType = TextureImporterType.NormalMap;
                        pngImporter.sRGBTexture = false;
                        pngImporter.SaveAndReimport();
                        updated++;
                    }
                }
            }
            return updated;
        }

        // EMAT v1 discovery only: the observed files use the "TAME" byte order and
        // hold null-terminated texture names. We do not infer sampler order, UV sets,
        // render states or shader parameters from these strings.
        // A material such as stg31_ground02_BL can explicitly reference
        // both ground02_CO and ground03_CO. Choose its own ground02_CO as
        // the provisional diffuse, never the first arbitrary _CO variant.
        // No global filename search and no guess for unrelated names.
        internal static string FindMatchingMaterialColour(
            string materialName, IEnumerable<string> referencedColours)
        {
            if (string.IsNullOrWhiteSpace(materialName) || referencedColours == null)
                return null;
            string baseName = Regex.Replace(materialName,
                @"_(?:BL|LI|SP|NO)\d*$", "", RegexOptions.IgnoreCase);
            if (baseName == materialName || baseName.Length == 0) return null;
            string expected = baseName + "_CO";
            var matches = referencedColours
                .Where(s => !string.IsNullOrWhiteSpace(s) &&
                    s.Equals(expected, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        internal static bool IsAuxiliaryTextureName(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName)) return false;
            return textureName.EndsWith("_LI", StringComparison.OrdinalIgnoreCase) ||
                textureName.EndsWith("_BL", StringComparison.OrdinalIgnoreCase) ||
                textureName.EndsWith("_SP", StringComparison.OrdinalIgnoreCase) ||
                textureName.EndsWith("_NO", StringComparison.OrdinalIgnoreCase);
        }

        internal static List<string> ReadEmatTextureReferences(string path)
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

        internal static string FindCompanionEmat(string emdPath, string materialName)
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
            if (string.IsNullOrWhiteSpace(textureName) ||
                textureName.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (string assetPath in FindTextureCandidates(emdPath, textureName))
            {
                ctx.DependsOnSourceAsset(assetPath);
                Texture2D image = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (image != null) return image;
                // A malformed/unimportable DDS is not a usable Texture2D.
                // Try another local file with the exact same basename.
            }
            return null;
        }

        // Directory and extension order are explicit. Directory.EnumerateFiles()
        // alone is filesystem-order-dependent, producing different assignments.
        // Never match an approximate texture name or a neighbouring stage folder.
        internal static List<string> FindTextureCandidates(string emdPath, string textureName)
        {
            var candidates = new List<string>();
            string parent = Path.GetDirectoryName(emdPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) ||
                string.IsNullOrWhiteSpace(textureName)) return candidates;
            string[] dirs = { parent + "/textures", parent + "/Textures", parent };
            string[] exts = { ".dds", ".png", ".tga", ".jpg", ".jpeg", ".bmp" };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                string[] files = Directory.GetFiles(dir);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string ext in exts)
                    foreach (string filename in files)
                    {
                        if (!Path.GetFileNameWithoutExtension(filename).Equals(
                                textureName, StringComparison.OrdinalIgnoreCase) ||
                            !Path.GetExtension(filename).Equals(ext, StringComparison.OrdinalIgnoreCase))
                            continue;
                        string path = filename.Replace('\\', '/');
                        if (seen.Add(path)) candidates.Add(path);
                    }
            }
            return candidates;
        }

        private static string FindTexture(string emdPath, string textureName)
        {
            var candidates = FindTextureCandidates(emdPath, textureName);
            return candidates.Count == 0 ? null : candidates[0];
        }
    }

    internal sealed class EmdBone
    {
        public string Name;
        public int Parent;
        public int NodeID;
        public Matrix4x4 LocalMatrix;
    }
    internal sealed class EmdMaterial
    {
        public string Name;
        public string TextureName;
        public readonly List<string> SubTextureNames = new List<string>();
    }
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
        public Vector2[][] SourceUvChannels;
        public int UvStride;
        public int UvStartOffset;
        public int UvSkippedZeroBytes;
        public int UvGroupHeaderOffset;
        public int UvHeaderPointerRaw;
        public int UvHeaderSecondaryRaw;
        public Color32[] Colors;
        public EmdWeight[] Weights;
    }
    internal sealed class EmdDocument
    {
        public readonly List<EmdBone> Bones = new List<EmdBone>();
        public readonly List<EmdMaterial> Materials = new List<EmdMaterial>();
        public readonly List<EmdGroup> Groups = new List<EmdGroup>();
        public int DeclaredPolygonCount;
        public int FileUvOffset;
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

        public static EmdDocument Parse(byte[] data, bool recoverSupportedPolygons = false,
            bool preserveZeroUvWords = false, bool alignUvBlocksTo16 = false,
            bool vertexColorsRgbaLast = false)
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
            doc.FileUvOffset = uvOffset;
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
                var allSubTextures = new List<string>();
                r.Seek((long)start + blockSize);
                for (int sub = 0; sub < subCount; ++sub)
                {
                    int subStart = r.Position;
                    r.I32(); int subSize = r.I32();
                    if (subSize < 16) throw new InvalidDataException("Malformed submaterial at 0x" + subStart.ToString("X"));
                    r.I32(); r.I32();
                    string subName = r.CString(255);
                    allSubTextures.Add(subName);
                    if (sub == 0) tex = subName;
                    r.Seek((long)subStart + subSize);
                }
                var material = new EmdMaterial { Name = name, TextureName = tex };
                material.SubTextureNames.AddRange(allSubTextures);
                doc.Materials.Add(material);
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
                List<int> stage200Headers = null;
                if (headerType == 0x204)
                {
                    groupCount = r.U16(); r.U16(); r.Skip(12 + 16);
                }
                else if (headerType == 0x200 && doc.Bones.Count == 0)
                {
                    // The 0x200 polygon has a routing/preamble record before its
                    // real submeshes. Probe complete in-polygon descriptors
                    // instead of treating the preamble as an empty mesh.
                    stage200Headers = EmdStageBinaryDiagnostics.ProbeConventionalMeshHeaders(
                        null, data, start, (long)start + recordSize);
                    if (stage200Headers.Count > 0)
                    {
                        groupCount = stage200Headers.Count;
                        doc.ImportWarnings.Add("EMD stage " + name +
                            " / polygon " + p + ": found " + groupCount +
                            " validated submeshes inside type-0x200 container.");
                    }
                }
                ValidateCount(groupCount, "mesh groups", 512);

                for (int g = 0; g < groupCount; ++g)
                {
                    if (stage200Headers != null && stage200Headers.Count > 0)
                        r.Seek(stage200Headers[g]);
                    else if (g > 0) r.Skip(0x50);
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
                    int uvHeaderPointerRaw = r.I32(); // +0x34: labelled ActualUVStart by original MaxScript
                    int uvHeaderSecondaryRaw = r.I32(); // +0x38: unknown auxiliary value
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
                    // Stage-EMD evidence (STG31/AREA01): some static polygons
                    // report 0x4000 position bytes although the actual XYZ stream
                    // continues contiguously past that boundary. We only accept the
                    // longer stream when it is completely inside this SAME polygon,
                    // follows the complete index buffer without any gap, and indices
                    // demonstrably address vertices beyond the nominal 0x4000 chunk.
                    // Other EMD layouts keep the original strict size validation.
                    long requiredPositionBytes = (long)pointCount * 12L;
                    if (requiredPositionBytes > vertexBytes)
                    {
                        long polygonEnd = (long)start + recordSize;
                        long physicalPositionEnd = (long)vertexJump + requiredPositionBytes;
                        long physicalIndexEnd = (long)indexJump + indexBytes;
                        bool contiguousStaticStage =
                            ((headerType == 0 && groupCount == 1 && g == 0) ||
                             (headerType == 0x200 && stage200Headers != null &&
                              stage200Headers.Count > 0 && doc.Bones.Count == 0)) &&
                            vertexBytes == 0x4000 &&
                            indexJump >= start &&
                            indexCount > 0 &&
                            physicalIndexEnd == vertexJump &&
                            physicalPositionEnd <= polygonEnd &&
                            physicalPositionEnd <= r.Length &&
                            (long)indexJump + indexCount * 2L <= vertexJump;

                        int highestReferencedVertex = -1;
                        if (contiguousStaticStage)
                        {
                            // Read only the known index span and preserve the
                            // group-header cursor before continuing. Invalid
                            // indices still fail rather than making corrupt mesh.
                            int savedPosition = r.Position;
                            try
                            {
                                r.Seek(indexJump);
                                for (int index = 0; index < indexCount; ++index)
                                {
                                    int referenced = r.U16();
                                    if (referenced >= pointCount)
                                        throw new InvalidDataException(
                                            "Triangle references invalid stage vertex " +
                                            referenced + " in " + name + " (pointCount=" + pointCount + ").");
                                    if (referenced > highestReferencedVertex)
                                        highestReferencedVertex = referenced;
                                }
                            }
                            finally { r.Seek(savedPosition); }
                        }
                        if (!contiguousStaticStage ||
                            highestReferencedVertex < vertexBytes / 12)
                            throw new InvalidDataException("Vertex buffer smaller than declared point count in " +
                                name + " / polygon " + p + " / group " + g +
                                ": declared=" + pointCount + " vertices (" + requiredPositionBytes +
                                " bytes), positionBuffer=" + vertexBytes +
                                " bytes, vertexOffset=0x" + vertexJump.ToString("X") +
                                ", uvLength=" + uvLength + " bytes, indexBytes=" + indexBytes +
                                ", polygonOffset=0x" + start.ToString("X") +
                                ", polygonEnd=0x" + polygonEnd.ToString("X") +
                                ", contiguousIndexEnd=0x" + physicalIndexEnd.ToString("X") +
                                ", fullVertexEnd=0x" + physicalPositionEnd.ToString("X") +
                                ", maximumIndex=" + highestReferencedVertex +
                                ". No verified contiguous stage position layout.");

                        doc.ImportWarnings.Add("EMD stage " + name + " / polygon " + p +
                            ": accepted contiguous XYZ vertex stream of " +
                            requiredPositionBytes + " bytes beyond reported 0x4000 chunk. " +
                            "Index max=" + highestReferencedVertex +
                            ", position end=0x" + physicalPositionEnd.ToString("X") +
                            ", polygon end=0x" + polygonEnd.ToString("X") +
                            ". Verify mesh visually in Scene view.");
                    }
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
                    int sourceUvStart = uvOffset;
                    r.Seek(uvOffset);
                    var uvs = new Vector2[pointCount];
                    // Source UVs are retained for comparing every channel.
                    var sourceUvChannels = new Vector2[uvSize - 1][];
                    for (int channel = 0; channel < sourceUvChannels.Length; channel++)
                        sourceUvChannels[channel] = new Vector2[pointCount];
                    var colors = new Color32[pointCount];
                    for (int v = 0; v < pointCount; ++v)
                    {
                        // UVsize 2,3,4: ARGB first, then 1-3 UVs; pick first UV.
                        // UVsize 5,6: 3 UVs first, ARGB, then 1-2 UVs; pick fourth UV.
                        if (uvSize <= 4)
                        {
                            byte first = r.U8(), second = r.U8();
                            byte third = r.U8(), fourth = r.U8();
                            colors[v] = vertexColorsRgbaLast ?
                                new Color32(first, second, third, fourth) :
                                new Color32(second, third, fourth, first);
                            for (int slot = 0; slot < uvSize - 1; ++slot)
                            {
                                // The source MaxScript half converter returns half the IEEE value;
                                // its subsequent multiply-by-two restores the normal UV.
                                float u = r.Half();
                                float t = 1f - r.Half();
                                Vector2 coordinate = new Vector2(u, t);
                                sourceUvChannels[slot][v] = coordinate;
                                if (slot == 0) uvs[v] = coordinate;
                            }
                        }
                        else
                        {
                            for (int slot = 0; slot < 3; ++slot)
                            {
                                float u = r.Half();
                                float t = 1f - r.Half();
                                sourceUvChannels[slot][v] = new Vector2(u, t);
                            }
                            byte first = r.U8(), second = r.U8();
                            byte third = r.U8(), fourth = r.U8();
                            colors[v] = vertexColorsRgbaLast ?
                                new Color32(first, second, third, fourth) :
                                new Color32(second, third, fourth, first);
                            for (int slot = 0; slot < uvSize - 4; ++slot)
                            {
                                // The source MaxScript half converter returns half the IEEE value;
                                // its subsequent multiply-by-two restores the normal UV.
                                float u = r.Half();
                                float t = 1f - r.Half();
                                Vector2 coordinate = new Vector2(u, t);
                                sourceUvChannels[slot + 3][v] = coordinate;
                                if (slot == 0) uvs[v] = coordinate;
                            }
                        }
                    }
                    // Some files have no subsequent UV chunk after the final mesh.
                    // Advance when possible, otherwise leave the cursor at EOF.
                    int uvBlockEnd = r.Position;
                    if (alignUvBlocksTo16)
                    {
                        // STG30/AREA00 forensic evidence: 24-byte UV records
                        // start at 0x126B0, 0x166A0, 0x24910, 0x250E0,
                        // 0x28590. Earlier sequential positions were offset by
                        // +8/+16 bytes because individual UV blocks were
                        // rounded up to the next absolute 16-byte boundary.
                        // Padding is not necessarily all zero.
                        long alignedEnd = ((long)uvBlockEnd + 15L) & ~15L;
                        // The last UV block may reach EOF without trailing
                        // alignment padding. In that case the cursor is never
                        // used for another group, so retain its valid end.
                        uvOffset = alignedEnd <= r.Length ?
                            (int)alignedEnd : uvBlockEnd;
                    }
                    else if ((stage200Headers != null && stage200Headers.Count > 0) ||
                             preserveZeroUvWords)
                    {
                        // Type 0x200 static-container UV blocks are contiguous.
                        // Opt-in also preserves zero words between other blocks.
                        uvOffset = uvBlockEnd;
                    }
                    else
                    {
                        try { uvOffset = r.SkipZeroWords(); }
                        catch (InvalidDataException) { uvOffset = r.Position; }
                    }
                    int skippedZeroBytes = uvOffset - uvBlockEnd;
                    // Normal multi-group records also use this cursor when
                    // locating a following group header. Follow the aligned
                    // boundary consistently instead of leaving it inside pad.
                    if (alignUvBlocksTo16 && uvOffset > r.Position)
                        r.Seek(uvOffset);

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
                        UV = uvs, SourceUvChannels = sourceUvChannels,
                        UvStride = uvStride, UvStartOffset = sourceUvStart,
                        UvSkippedZeroBytes = skippedZeroBytes,
                        UvGroupHeaderOffset = basePos,
                        UvHeaderPointerRaw = uvHeaderPointerRaw,
                        UvHeaderSecondaryRaw = uvHeaderSecondaryRaw,
                        Colors = colors, Weights = weights
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
