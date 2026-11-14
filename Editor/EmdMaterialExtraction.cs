// Export EMD's generated material sub-assets as independently editable .mat files.
// Runs only from an explicit Inspector button, never inside OnImportAsset.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    internal static class EmdMaterialExtraction
    {
        private static string SafeName(string value)
        {
            string name = Regex.Replace(value ?? "", @"[^A-Za-z0-9_.-]+", "_").Trim('_', '.');
            if (name.Length == 0) name = "Material";
            return name.Length > 64 ? name.Substring(0, 64) : name;
        }

        internal static string GetFolder(string emdPath)
        {
            string directory = Path.GetDirectoryName(emdPath);
            return string.IsNullOrEmpty(directory) ? null :
                directory.Replace('\\', '/') + "/materials";
        }

        internal static string GetMaterialPath(string emdPath, int sourceIndex, string sourceName)
        {
            string folder = GetFolder(emdPath);
            if (string.IsNullOrEmpty(folder) || sourceIndex < 0) return null;
            return folder + "/" + SafeName(Path.GetFileNameWithoutExtension(emdPath)) +
                "_" + sourceIndex.ToString("D3") + "_" + SafeName(sourceName) + ".mat";
        }

        internal static bool TryGetExternal(string emdPath, int sourceIndex,
            string sourceName, out Material material)
        {
            material = null;
            string path = GetMaterialPath(emdPath, sourceIndex, sourceName);
            if (string.IsNullOrEmpty(path)) return false;
            material = AssetDatabase.LoadAssetAtPath<Material>(path);
            return material != null;
        }

        internal static bool ExtractAll(string emdPath, out int created, out int existing,
            out int missing)
        {
            created = 0;
            existing = 0;
            missing = 0;
            if (string.IsNullOrEmpty(emdPath) ||
                !emdPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !emdPath.EndsWith(".emd", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select a source .emd asset under Assets.");

            EmdMaterialRegistry registry = null;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(emdPath))
            {
                registry = asset as EmdMaterialRegistry;
                if (registry != null) break;
            }
            if (registry == null || registry.generatedMaterials == null ||
                registry.generatedMaterials.Length == 0)
                throw new InvalidOperationException(
                    "No EMD material registry found. Reimport the .emd with EMD2Unity " +
                    "v0.6.6+ before extracting materials.");

            string folder = GetFolder(emdPath);
            string parent = Path.GetDirectoryName(emdPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || !AssetDatabase.IsValidFolder(parent))
                throw new InvalidOperationException("The EMD source folder is not a valid Assets folder.");

            int candidates = 0;
            for (int i = 0; i < registry.generatedMaterials.Length; i++)
                if (registry.generatedMaterials[i] != null) candidates++;
            if (candidates == 0)
                throw new InvalidOperationException("This EMD has no generated Material sub-assets.");

            if (!EditorUtility.DisplayDialog("Extract EMD Materials",
                "Source: " + emdPath + "\n" +
                "Materials: " + candidates + "\n" +
                "Destination: " + folder + "\n\n" +
                "Existing .mat assets are preserved. After extraction the imported " +
                "EMD model will use these external materials. You can edit their " +
                "Normal Map, shader, smoothness and other settings independently.",
                "Extract Materials", "Cancel"))
                return false;

            if (!AssetDatabase.IsValidFolder(folder))
            {
                string guid = AssetDatabase.CreateFolder(parent, "materials");
                if (string.IsNullOrEmpty(guid) || !AssetDatabase.IsValidFolder(folder))
                    throw new IOException("Could not create " + folder);
            }

            try
            {
                for (int i = 0; i < registry.generatedMaterials.Length; i++)
                {
                    Material template = registry.generatedMaterials[i];
                    if (template == null)
                    {
                        missing++;
                        continue;
                    }
                    string materialName = registry.sourceMaterialNames != null &&
                        i < registry.sourceMaterialNames.Length ?
                        registry.sourceMaterialNames[i] : template.name;
                    string destination = GetMaterialPath(emdPath, i, materialName);
                    if (AssetDatabase.LoadAssetAtPath<Material>(destination) != null)
                    {
                        existing++;
                        continue;
                    }
                    if (File.Exists(destination) ||
                        AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(destination) != null)
                    {
                        // Never overwrite a non-Material asset with a colliding name.
                        missing++;
                        Debug.LogWarning("EMD2Unity: extraction path occupied by a non-Material asset: " +
                            destination);
                        continue;
                    }
                    Material extracted = new Material(template) { name = template.name };
                    try
                    {
                        AssetDatabase.CreateAsset(extracted, destination);
                        created++;
                    }
                    catch
                    {
                        if (extracted != null && !AssetDatabase.Contains(extracted))
                            UnityEngine.Object.DestroyImmediate(extracted);
                        throw;
                    }
                }
            }
            finally
            {
                AssetDatabase.SaveAssets();
            }
            Debug.Log("EMD2Unity material extraction " + emdPath +
                ": created=" + created + ", existing preserved=" + existing +
                ", unavailable/skipped=" + missing +
                ". Extracted .mat files are editable and are never overwritten by EMD reimport.");
            return true;
        }
    }
}
#endif
