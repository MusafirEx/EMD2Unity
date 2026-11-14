// Compatibility facade for EMD material extraction.
// A single implementation in EmdImporter reads the original source EMD
// material-table registry, never mesh renderers or mesh material slots.
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
            string name = Regex.Replace(value ?? "", @"[^A-Za-z0-9_-]+", "_").Trim('_');
            if (name.Length == 0) name = "Material";
            return name.Length > 48 ? name.Substring(0, 48) : name;
        }

        internal static string GetFolder(string emdPath)
        {
            string directory = Path.GetDirectoryName(emdPath);
            return string.IsNullOrEmpty(directory) ? null :
                directory.Replace('\\', '/') + "/material";
        }

        internal static string GetMaterialPath(string emdPath, int sourceIndex, string sourceName)
        {
            string folder = GetFolder(emdPath);
            if (string.IsNullOrEmpty(folder) || sourceIndex < 0) return null;
            return folder + "/" + Path.GetFileNameWithoutExtension(emdPath) +
                "_Mat_" + sourceIndex.ToString("D3") + "_" + SafeName(sourceName) + ".mat";
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

        // Retain compatibility with callers of the original helper, but
        // delegate to the importer-owned source material index extraction.
        internal static bool ExtractAll(string emdPath, out int created,
            out int existing, out int missing)
        {
            created = 0;
            existing = 0;
            missing = 0;
            EmdImporter importer = AssetImporter.GetAtPath(emdPath) as EmdImporter;
            if (importer == null)
                throw new InvalidOperationException(
                    "Select an imported source .emd asset.");
            if (!EditorUtility.DisplayDialog("Extract EMD Materials",
                "Source: " + emdPath + "\n" +
                "Destination: " + GetFolder(emdPath) + "\n\n" +
                "Uses the source EMD material table, NOT the mesh list. " +
                "Existing .mat assets are never overwritten.",
                "Extract Materials", "Cancel"))
                return false;
            importer.ExtractMaterialsToFolder(out created, out existing, out missing);
            importer.SaveAndReimport();
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
#endif
