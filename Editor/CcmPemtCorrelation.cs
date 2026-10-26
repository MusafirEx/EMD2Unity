// Evidence-only cross-reference from a CCM action code to nearby PEMT
// motion IDs. This is a NUMERIC candidate search, NOT a decoded event mapping.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace MusafirEx.EMD2Unity
{
    internal sealed class CcmPemtCandidate
    {
        internal string comparison;
        internal int candidateId;
        internal PemtMotionLibrary library;
        internal PemtMotionMetadata motion;
        internal string path;
    }

    internal static class CcmPemtCorrelation
    {
        internal static List<PemtMotionLibrary> FindSiblingPemt(CcmComboLibrary ccm)
        {
            var result = new List<PemtMotionLibrary>();
            if (ccm == null) return result;
            string source = AssetDatabase.GetAssetPath(ccm);
            if (string.IsNullOrEmpty(source)) return result;
            string combo = Path.GetDirectoryName(source);
            if (string.IsNullOrEmpty(combo)) return result;
            string character = Path.GetDirectoryName(combo);
            if (string.IsNullOrEmpty(character)) return result;
            string motionFolder = Path.Combine(character, "motion").Replace('\\', '/');
            if (!Directory.Exists(motionFolder)) return result;

            foreach (string file in Directory.GetFiles(motionFolder, "*", SearchOption.TopDirectoryOnly))
            {
                if (!file.EndsWith(".pemt", StringComparison.OrdinalIgnoreCase)) continue;
                string path = file.Replace('\\', '/');
                var library = AssetDatabase.LoadAssetAtPath<PemtMotionLibrary>(path);
                if (library == null)
                {
                    foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
                        if ((library = sub as PemtMotionLibrary) != null) break;
                }
                if (library != null && string.IsNullOrEmpty(library.importError))
                    result.Add(library);
            }
            result.Sort((a, b) => string.Compare(
                AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b),
                StringComparison.OrdinalIgnoreCase));
            return result;
        }

        internal static List<CcmPemtCandidate> Compare(CcmComboRecord combo,
            List<PemtMotionLibrary> libraries)
        {
            var result = new List<CcmPemtCandidate>();
            if (combo == null || libraries == null) return result;
            int low = combo.codeAt1CIndex;
            int full = (combo.codeAt1CGroup << 16) | low;
            int[] ids = { full, low, low + 0x100, low + 0x200 };
            string[] names =
            {
                "Exact CCM uint32 code",
                "Code low16 as PEMT ID",
                "Code low16 + 0x100",
                "Code low16 + 0x200"
            };
            foreach (PemtMotionLibrary library in libraries)
            {
                if (library == null || library.motions == null) continue;
                string path = AssetDatabase.GetAssetPath(library);
                foreach (PemtMotionMetadata motion in library.motions)
                {
                    if (motion == null) continue;
                    for (int h = 0; h < ids.Length; h++)
                    {
                        if (motion.motionId != ids[h]) continue;
                        result.Add(new CcmPemtCandidate
                        {
                            comparison = names[h],
                            candidateId = ids[h],
                            library = library,
                            motion = motion,
                            path = path
                        });
                    }
                }
            }
            return result;
        }
    }
}
#endif
