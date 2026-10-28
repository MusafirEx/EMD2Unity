// Read-only diagnosis of texture selection for imported EMD model assets.
// Maps source EMD polygon/material references to actual Unity renderer textures.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    internal static class EmdTextureBindingDiagnostics
    {
        [MenuItem("Tools/EMD2Unity/Diagnose Selected EMD Texture Bindings")]
        private static void DiagnoseSelected()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrWhiteSpace(path) ||
                !path.EndsWith(".emd", StringComparison.OrdinalIgnoreCase))
            {
                GameObject active = Selection.activeGameObject;
                if (active != null)
                {
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(active);
                    if (source == null) source = PrefabUtility.GetCorrespondingObjectFromSource(active);
                    if (source != null) path = AssetDatabase.GetAssetPath(source);
                }
            }
            DiagnosePath(path);
        }

        internal static void DiagnosePath(string path)
        {
            try
            {
                string report = BuildReport(path);
                Debug.Log(report);
                EditorGUIUtility.systemCopyBuffer = report;
                EditorUtility.DisplayDialog("EMD2Unity Material & Texture Audit",
                    "A read-only mesh/material/texture report was copied to the " +
                    "clipboard and Unity Console.\n\nNo model files, textures, or " +
                    "material assignments were modified.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("EMD2Unity material audit failed: " + ex);
                EditorUtility.DisplayDialog("EMD Material Audit", ex.Message, "OK");
            }
        }

        private static Texture MainTexture(Material material)
        {
            if (material == null) return null;
            string[] slots = { "_BaseMap", "_MainTex", "_BaseColorMap" };
            foreach (string slot in slots)
            {
                if (material.HasProperty(slot))
                {
                    Texture texture = material.GetTexture(slot);
                    if (texture != null) return texture;
                }
            }
            return null;
        }

        private static string ListNames(IEnumerable<string> values)
        {
            return string.Join(", ", values.Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        internal static string BuildReport(string emdPath)
        {
            if (string.IsNullOrWhiteSpace(emdPath) ||
                !emdPath.EndsWith(".emd", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(emdPath))
                throw new InvalidDataException("Select an imported EMD (.emd) asset or its linked prefab instance.");

            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(emdPath);
            if (root == null)
                throw new InvalidDataException("Cannot load the imported EMD model: " + emdPath);

            var report = new StringBuilder();
            report.AppendLine("EMD2Unity Texture Binding Audit (read-only)");
            report.AppendLine("EMD: " + emdPath);
            var importer = AssetImporter.GetAtPath(emdPath) as EmdImporter;
            if (importer != null)
            {
                SerializedObject so = new SerializedObject(importer);
                SerializedProperty prefer = so.FindProperty("preferUniqueColourMap");
                SerializedProperty emat = so.FindProperty("useEmatTextureReferences");
                report.AppendLine("Prefer Unique _CO Diffuse: " +
                    (prefer == null ? "(setting unavailable)" : prefer.boolValue.ToString()));
                report.AppendLine("Read EMAT References: " +
                    (emat == null ? "(setting unavailable)" : emat.boolValue.ToString()));
            }

            EmdDocument source = null;
            try
            {
                // In diagnostic mode recover otherwise supported groups to
                // report their source material indices. Does not modify import.
                source = EmdParser.Parse(File.ReadAllBytes(emdPath), true);
            }
            catch (Exception ex)
            {
                report.AppendLine("SOURCE PARSE UNAVAILABLE: " + ex.Message +
                    ". Actual renderer bindings below remain valid.");
            }

            var sourceGroups = new Dictionary<string, EmdGroup>(StringComparer.Ordinal);
            if (source != null)
            {
                report.AppendLine("Source material blocks: " + source.Materials.Count +
                    ", recovered mesh groups: " + source.Groups.Count);
                for (int i = 0; i < source.Groups.Count; i++)
                    sourceGroups[source.Groups[i].Name + "_" + i] = source.Groups[i];
                for (int i = 0; i < source.Materials.Count; i++)
                {
                    EmdMaterial mat = source.Materials[i];
                    string ematPath = EmdImporter.FindCompanionEmat(emdPath, mat.Name);
                    var refs = ematPath == null ? new List<string>() :
                        EmdImporter.ReadEmatTextureReferences(ematPath);
                    var colours = mat.SubTextureNames.Concat(refs)
                        .Where(x => x.EndsWith("_CO", StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    report.AppendLine("");
                    report.AppendLine("MATERIAL #" + i + " [" + mat.Name + "]");
                    report.AppendLine("  First EMD submaterial texture: " + mat.TextureName);
                    report.AppendLine("  All EMD submaterial names: " + ListNames(mat.SubTextureNames));
                    report.AppendLine("  Companion EMAT: " + (ematPath ?? "(none)"));
                    report.AppendLine("  EMAT texture names: " + ListNames(refs));
                    report.AppendLine("  Explicit _CO candidates: " + ListNames(colours));
                    if (EmdImporter.IsAuxiliaryTextureName(mat.TextureName))
                    {
                        report.AppendLine("  CHECK: original diffuse candidate has auxiliary suffix.");
                        if (colours.Count == 1)
                            report.AppendLine("  Unique source-supported _CO: " + colours[0]);
                        else if (colours.Count > 1)
                            report.AppendLine("  AMBIGUOUS: multiple _CO references; no guessing.");
                    }
                    foreach (string colour in colours)
                    {
                        var candidatePaths = EmdImporter.FindTextureCandidates(emdPath, colour);
                        if (candidatePaths.Count == 0)
                            report.AppendLine("  No local asset file for _CO: " + colour);
                        foreach (string candidate in candidatePaths)
                            report.AppendLine("  _CO file: " + candidate +
                                " | Unity Texture2D=" +
                                (AssetDatabase.LoadAssetAtPath<Texture2D>(candidate) != null));
                    }
                }
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            report.AppendLine("");
            report.AppendLine("ACTUAL UNITY RENDERER BINDINGS (" + renderers.Length + "):");
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer render = renderers[i];
                bool mapped = sourceGroups.TryGetValue(render.name, out EmdGroup group);
                string groupText = mapped ?
                    ("EMD MaterialIndex=" + group.MaterialIndex +
                     (source != null && group.MaterialIndex >= 0 &&
                      group.MaterialIndex < source.Materials.Count ?
                         " [" + source.Materials[group.MaterialIndex].Name + "]" : " (out of range)")) :
                    "(source group not mapped)";
                report.AppendLine("  " + render.name + ": " + groupText);
                Material[] materials = render.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    report.AppendLine("    No assigned Unity materials.");
                    continue;
                }
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null)
                    {
                        report.AppendLine("    Slot " + slot + ": NULL material");
                        continue;
                    }
                    Texture diffuse = MainTexture(material);
                    string texPath = diffuse == null ? "(missing / unassigned)" :
                        AssetDatabase.GetAssetPath(diffuse);
                    report.AppendLine("    Slot " + slot + ": " + material.name +
                        " | Shader=" + (material.shader == null ? "(none)" : material.shader.name) +
                        " | Diffuse=" + (diffuse == null ? "(none)" : diffuse.name) +
                        " | Source=" + texPath);
                    if (diffuse == null)
                        report.AppendLine("    CHECK: no loadable diffuse texture bound.");
                    else if (EmdImporter.IsAuxiliaryTextureName(diffuse.name))
                        report.AppendLine("    CHECK: auxiliary map used as diffuse; verify intended role.");
                }
            }
            report.AppendLine("");
            report.AppendLine("An _LI/_BL suffix or unique _CO candidate indicates a possible " +
                "mapping issue, not definitive proof. Confirm against original render.");
            return report.ToString();
        }
    }
}
#endif
