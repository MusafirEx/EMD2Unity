// Retains the importer-generated material templates by EMD source index.
// External materials are separate assets and are never overwritten by reimport.
#if UNITY_EDITOR
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public sealed class EmdMaterialRegistry : ScriptableObject
    {
        [HideInInspector] public Material[] generatedMaterials;
        [HideInInspector] public string[] sourceMaterialNames;
    }
}
#endif
