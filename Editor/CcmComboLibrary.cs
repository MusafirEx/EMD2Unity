// CCM metadata types. Deliberately read-only: record fields are preserved as
// raw little-endian words, without assigning unverified gameplay semantics.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    [Serializable]
    public sealed class CcmComboRecord
    {
        public int recordIndex;
        public int tableId;
        public int recordId;
        public int recordOffset;
        public string valueAt04Hex;
        public string valueAt10Hex;
        public int valueAt14; // Observed Timing A, units not verified.
        public int valueAt18; // Observed Timing B, units not verified.
        public int differenceAminusB;
        public string codeAt1CHex;
        public int codeAt1CGroup;
        public int codeAt1CIndex;
        public string linkAt68Hex;
        public int linkAt68Group;
        public int linkAt68Index;
        public bool linkIndexIsPresent;
        public string[] rawWordsHex = new string[32];
    }

    public sealed class CcmComboLibrary : ScriptableObject
    {
        [HideInInspector] public string importError;
        [HideInInspector] public int sourceByteCount;
        [HideInInspector] public string versionHex;
        [HideInInspector] public int recordCount;
        [HideInInspector] public int firstRecordOffset;
        [HideInInspector] public int footerOffset;
        [HideInInspector] public bool allDifferences20;
        [HideInInspector] public bool allLinksPointToRecordIds;
        [HideInInspector] public List<CcmComboRecord> records = new List<CcmComboRecord>();
    }
}
#endif
