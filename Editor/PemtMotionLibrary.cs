// PEMT motion-container data asset. The ScriptableObject type must be in
// PemtMotionLibrary.cs for Unity to associate the MonoScript with this class.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    [Serializable]
    public sealed class PemtTrackMetadata
    {
        public int index;
        public string markerHex;
        public string infoHex;
        public float apparentDurationSeconds;
        public int dataStartRelative;
        public int dataEndRelative;
        public string unknownWordHex;
        public string reserved1Hex;
        public string reserved2Hex;
    }

    [Serializable]
    public sealed class PemtMotionMetadata
    {
        public int motionId;
        public string motionIdHex;
        public int blockOffset;
        public int blockByteCount;
        public float apparentDurationSeconds;
        public int nominalFramesAt60Hz;
        public List<PemtTrackMetadata> tracks = new List<PemtTrackMetadata>();
    }

    public sealed class PemtMotionLibrary : ScriptableObject
    {
        [HideInInspector] public int sourceByteCount;
        [HideInInspector] public int pointerTableEntries;
        [HideInInspector] public int occupiedEntries;
        [HideInInspector] public int trackCount;
        [HideInInspector] public string importError;
        [HideInInspector] public List<PemtMotionMetadata> motions = new List<PemtMotionMetadata>();
    }

}
#endif
