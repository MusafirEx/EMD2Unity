// EMD2Unity - EXPERIMENTAL PEMT AnimationClip exporter.
// Data-supported: track 0x02040021 = float16 XYZ position delta, 0x02040028 = float16 XYZW quaternion.
// Bone IDs are _j<number> suffixes. Pose composition and interpolation require visual testing.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
namespace MusafirEx.EMD2Unity
{
    internal static class PemtAnimationClipExporter
    {
        private const uint Position = 0x02040021;
        private const uint Rotation = 0x02040028;

        private static uint Be32(byte[] b, int p)
        {
            if (p < 0 || p > b.Length - 4) throw new InvalidDataException("PEMT: invalid float32 offset.");
            return (uint)(b[p] << 24 | b[p+1] << 16 | b[p+2] << 8 | b[p+3]);
        }
        private static float F32(byte[] b, int p) =>
            BitConverter.ToSingle(BitConverter.GetBytes(Be32(b,p)),0);
        private static float Half(byte[] b, int p)
        {
            if (p < 0 || p > b.Length - 2) throw new InvalidDataException("PEMT: invalid float16 offset.");
            uint x=(uint)((b[p]<<8)|b[p+1]), sign=(x&0x8000)<<16, frac=x&0x3ff;
            int e=(int)((x>>10)&31);
            uint raw;
            if(e==0)
            {
                if(frac==0)raw=sign;
                else
                {
                    int pow=-14;
                    while((frac&0x400)==0){frac<<=1;pow--;}
                    raw=sign|((uint)(pow+127)<<23)|((frac&0x3ff)<<13);
                }
            }
            else if(e==31)raw=sign|0x7f800000|(frac<<13);
            else raw=sign|((uint)(e-15+127)<<23)|(frac<<13);
            return BitConverter.ToSingle(BitConverter.GetBytes(raw),0);
        }
        private static void MapBones(Transform tr,string path,
            Dictionary<int,string> paths,Dictionary<int,Transform> bones)
        {
            Match m=Regex.Match(tr.name,@"_j(\d+)$",RegexOptions.IgnoreCase);
            if(m.Success && int.TryParse(m.Groups[1].Value,out int id))
            {
                if(paths.ContainsKey(id))throw new InvalidDataException("Duplicate bone ID j"+id);
                paths.Add(id,path);bones.Add(id,tr);
            }
            for(int i=0;i<tr.childCount;i++)
            {
                Transform child=tr.GetChild(i);
                MapBones(child,path+"/"+child.name,paths,bones);
            }
        }
        private static void Curve(AnimationClip clip,string path,string channel,float[] t,float[] v)
        {
            Keyframe[] keys=new Keyframe[t.Length];
            for(int i=0;i<keys.Length;i++)keys[i]=new Keyframe(t[i],v[i]);
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),channel),
                new AnimationCurve(keys));
        }
        private static void WritePosition(AnimationClip clip,string path,float[] time,Vector3[] vals)
        {
            float[] x=new float[vals.Length],y=new float[vals.Length],z=new float[vals.Length];
            for(int i=0;i<vals.Length;i++){x[i]=vals[i].x;y[i]=vals[i].y;z[i]=vals[i].z;}
            Curve(clip,path,"m_LocalPosition.x",time,x);
            Curve(clip,path,"m_LocalPosition.y",time,y);
            Curve(clip,path,"m_LocalPosition.z",time,z);
        }
        private static void WriteRotation(AnimationClip clip,string path,float[] time,Quaternion[] vals)
        {
            float[] x=new float[vals.Length],y=new float[vals.Length],z=new float[vals.Length],w=new float[vals.Length];
            for(int i=0;i<vals.Length;i++){x[i]=vals[i].x;y[i]=vals[i].y;z[i]=vals[i].z;w[i]=vals[i].w;}
            Curve(clip,path,"m_LocalRotation.x",time,x);
            Curve(clip,path,"m_LocalRotation.y",time,y);
            Curve(clip,path,"m_LocalRotation.z",time,z);
            Curve(clip,path,"m_LocalRotation.w",time,w);
        }
        private static GameObject ResolveSourceEmdModel(GameObject selection)
        {
            GameObject current = selection;
            for (int i = 0; i < 64 && current != null; i++)
            {
                string path = AssetDatabase.GetAssetPath(current);
                if (!string.IsNullOrEmpty(path) &&
                    path.EndsWith(".emd", StringComparison.OrdinalIgnoreCase))
                {
                    GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (source != null) return source;
                }
                // Resolve prefab instances placed in Hierarchy to their imported asset.
                GameObject original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(current);
                if (original != null && original != current) { current = original; continue; }
                GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (prefab != null && prefab != current) { current = prefab; continue; }
                current = current.transform.parent == null ? null : current.transform.parent.gameObject;
            }
            throw new InvalidDataException(
                "Selected GameObject does not resolve to an imported .emd asset. " +
                "Use the Nearby EMD Models dropdown or drag the EMD asset from Project. " +
                "An unpacked scene-only GameObject has no EMD import settings.");
        }

        // Resolves scene prefab instances to the canonical imported EMD asset.
        internal static GameObject GetSourceEmdModel(GameObject selection)
        {
            return ResolveSourceEmdModel(selection);
        }

        // Decode into an unsaved AnimationClip. This is the single keyframe path
        // for preview, selected export and batch export.
        internal static AnimationClip CreateTransientClip(PemtMotionLibrary lib,
            PemtMotionMetadata motion, GameObject model, bool rotateFirst)
        {
            if(lib==null||motion==null||model==null)throw new ArgumentException("Select a PEMT motion and an EMD model.");
            // The Inspector may hold either an EMD Project asset or a scene prefab
            // instance. Resolve a scene instance back to its imported EMD source.
            model = ResolveSourceEmdModel(model);
            string modelPath=AssetDatabase.GetAssetPath(model);
            string sourcePath=AssetDatabase.GetAssetPath(lib);
            if(string.IsNullOrEmpty(sourcePath)||!sourcePath.EndsWith(".pemt",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The source must be an imported .pemt container.");
            var emdImporter=AssetImporter.GetAtPath(modelPath) as EmdImporter;
            if(emdImporter==null)throw new InvalidDataException("Selected model is not imported by EMD2Unity.");
            SerializedObject settings=new SerializedObject(emdImporter);
            bool swap=settings.FindProperty("swapYAndZ").boolValue;
            float scale=settings.FindProperty("modelScale").floatValue;
            Transform armature=model.transform.Find("Armature");
            if(armature==null)throw new InvalidDataException("Imported EMD model has no Armature. Enable Import Skeleton.");
            var paths=new Dictionary<int,string>();
            var bones=new Dictionary<int,Transform>();
            MapBones(armature,"Armature",paths,bones);
            if(paths.Count==0)throw new InvalidDataException("No EMD bones with _j<number> suffix.");
            string name=Path.GetFileNameWithoutExtension(sourcePath)+"_Motion_"+motion.motionId.ToString("X3");
            byte[] b=File.ReadAllBytes(sourcePath);
            int start=motion.blockOffset;
            int end=checked(start+motion.blockByteCount);
            if(start<0||end>b.Length||start>=end)throw new InvalidDataException("Invalid PEMT motion boundaries.");
            var clip=new AnimationClip{name=name,frameRate=60f,legacy=false};
            var seen=new HashSet<string>();
            int bound=0,missing=0;
            try
            {
                foreach(var track in motion.tracks)
                {
                    uint kind=Convert.ToUInt32(track.markerHex.Substring(2),16);
                    if(kind==0x40)continue;
                    if(kind!=Position && kind!=Rotation)
                        throw new InvalidDataException("Unsupported track marker "+track.markerHex);
                    uint info=Convert.ToUInt32(track.infoHex.Substring(2),16);
                    int jointId=(int)(info>>16);
                    int count=(int)(info&0xffff);
                    if(count<1)continue;
                    if(!paths.TryGetValue(jointId,out string path)){missing++;continue;}
                    if(!seen.Add(jointId+":"+kind))
                        throw new InvalidDataException("Repeated channel for bone j"+jointId);
                    int timesAt=checked(start+track.dataStartRelative);
                    int valuesAt=checked(start+track.dataEndRelative);
                    if(track.dataEndRelative<=0||(long)timesAt+count*4L>end||
                        (long)valuesAt+count*8L>end)
                        throw new InvalidDataException("Keyframe payload outside motion for bone j"+jointId);
                    float[] time=new float[count];
                    for(int k=0;k<count;k++)
                    {
                        time[k]=F32(b,timesAt+k*4);
                        if((float.IsNaN(time[k]) || float.IsInfinity(time[k]))||time[k]<0||
                            (k>0&&time[k]<time[k-1]))
                            throw new InvalidDataException("Invalid key time for bone j"+jointId);
                    }
                    Transform joint=bones[jointId];
                    if(kind==Position)
                    {
                        Vector3[] pos=new Vector3[count];
                        for(int k=0;k<count;k++)
                        {
                            int p=valuesAt+k*8;
                            Vector3 delta=new Vector3(Half(b,p),Half(b,p+2),Half(b,p+4));
                            if(swap)delta=new Vector3(delta.x,delta.z,delta.y);
                            pos[k]=joint.localPosition+delta*scale;
                        }
                        WritePosition(clip,path,time,pos);
                    }
                    else
                    {
                        Quaternion[] rot=new Quaternion[count];
                        Quaternion prev=Quaternion.identity;
                        for(int k=0;k<count;k++)
                        {
                            int p=valuesAt+k*8;
                            Quaternion delta=new Quaternion(Half(b,p),Half(b,p+2),Half(b,p+4),Half(b,p+6));
                            if(swap)delta=new Quaternion(-delta.x,-delta.z,-delta.y,delta.w);
                            float sq=delta.x*delta.x+delta.y*delta.y+delta.z*delta.z+delta.w*delta.w;
                            if(sq<0.5f||sq>1.5f)throw new InvalidDataException("Invalid quaternion on bone j"+jointId);
                            float inv=1f/Mathf.Sqrt(sq);
                            delta=new Quaternion(delta.x*inv,delta.y*inv,delta.z*inv,delta.w*inv);
                            Quaternion q=rotateFirst?delta*joint.localRotation:joint.localRotation*delta;
                            if(k>0&&Quaternion.Dot(q,prev)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                            rot[k]=q;prev=q;
                        }
                        WriteRotation(clip,path,time,rot);
                    }
                    bound++;
                }
                if(bound<1)throw new InvalidDataException("No motion tracks matched the selected EMD bones.");
                clip.EnsureQuaternionContinuity();
                return clip;
            }
            catch
            {
                if (!AssetDatabase.Contains(clip))
                    UnityEngine.Object.DestroyImmediate(clip);
                throw;
            }
        }

        // Save an independently decoded clip. Batch mode provides a destination
        // and skips per-animation UI dialogs.
        internal static bool Export(PemtMotionLibrary lib, PemtMotionMetadata motion,
            GameObject model, bool rotateFirst, string batchAssetPath = null,
            bool interactive = true)
        {
            if (lib == null || motion == null || model == null)
                throw new ArgumentException("Select a PEMT motion and an EMD model.");
            string sourcePath = AssetDatabase.GetAssetPath(lib);
            if (string.IsNullOrEmpty(sourcePath) ||
                !sourcePath.EndsWith(".pemt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The source must be an imported .pemt container.");
            string name = Path.GetFileNameWithoutExtension(sourcePath) +
                "_Motion_" + motion.motionId.ToString("X3");
            string save = batchAssetPath;
            if (string.IsNullOrEmpty(save) && interactive)
                save = EditorUtility.SaveFilePanelInProject(
                    "PEMT AnimationClip Export", name, "anim",
                    "Export an experimental keyframed clip; test on a duplicate EMD model.",
                    Path.GetDirectoryName(sourcePath)?.Replace('\\', '/'));
            if (string.IsNullOrEmpty(save)) return false;
            if (!save.StartsWith("Assets/", StringComparison.Ordinal) ||
                !save.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                throw new IOException("AnimationClip destination must be inside Unity's Assets folder.");
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(save) != null)
                throw new IOException("AnimationClip file already exists: " + save);

            AnimationClip clip = CreateTransientClip(lib, motion, model, rotateFirst);
            try
            {
                AssetDatabase.CreateAsset(clip, save);
                if (interactive)
                {
                    AssetDatabase.SaveAssets();
                    Selection.activeObject = clip;
                    EditorGUIUtility.PingObject(clip);
                    Debug.Log("PEMT: saved " + save + " (" + motion.motionIdHex + ")");
                    EditorUtility.DisplayDialog("PEMT Export Complete",
                        "Created " + name + ".\nPreview on a duplicate EMD model to verify pose and orientation.",
                        "OK");
                }
                return true;
            }
            finally
            {
                if (clip != null && !AssetDatabase.Contains(clip))
                    UnityEngine.Object.DestroyImmediate(clip);
            }
        }
    }
}
#endif
