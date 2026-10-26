// Binary SEQ script metadata. Must live in SeqScriptLibrary.cs for Unity's
// ScriptedImporter ScriptableObject registration. All contents are read-only.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    [Serializable]
    public sealed class SeqStringReference
    {
        public int byteOffset;
        public string value;
        public string category;
        // Exact BE uint32 occurrences BEFORE the string, unverified as bytecode
        // instruction boundaries. Some may be data rather than instructions.
        public List<int> rawPointerOffsets = new List<int>();
        // Subset where preceding four bytes are 16 00 00 00.
        public List<int> prefix16PointerOffsets = new List<int>();
        public bool referencesTruncated;
    }

    public sealed class SeqScriptLibrary : ScriptableObject
    {
        [HideInInspector] public int sourceByteCount;
        [HideInInspector] public string word0;
        [HideInInspector] public string word1;
        [HideInInspector] public string word2;
        [HideInInspector] public string word3;
        [HideInInspector] public string firstBytesHex;
        [HideInInspector] public int marker12RawCount;
        [HideInInspector] public int marker16RawCount;
        [HideInInspector] public int marker2013RawCount;
        [HideInInspector] public int referencedStringCount;
        [HideInInspector] public int prefix16StringCount;
        [HideInInspector] public string importError;
        [HideInInspector] public List<SeqStringReference> strings = new List<SeqStringReference>();
    }
}
#endif
