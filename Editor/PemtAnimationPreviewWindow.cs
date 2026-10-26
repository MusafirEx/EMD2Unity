// Non-destructive PEMT motion preview in an isolated Editor rendering scene.
// Transient AnimationClip curves come from the SAME PEMT decoder as .anim export.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.EMD2Unity
{
    public sealed class PemtAnimationPreviewWindow : EditorWindow
    {
        [SerializeField] private PemtMotionLibrary pemt;
        [SerializeField] private GameObject emd;
        [SerializeField] private int motionId = -1;
        [SerializeField] private bool deltaRotationFirst;
        [SerializeField] private bool loop = true;
        [SerializeField] private float speed = 1f;
        [SerializeField] private float time;

        private PreviewRenderUtility utility;
        private GameObject modelCopy;
        private AnimationClip clip;
        private Bounds bounds;
        private float duration;
        private bool playing;
        private double lastTime;
        private float yaw = -25f, pitch = 15f, zoom = 1.35f;
        private string feedback = "Choose a PEMT, a matching EMD and a Motion ID, then Build Preview.";

        [MenuItem("Tools/EMD2Unity/PEMT Animation Preview")]
        private static void OpenMenu()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            OpenForSource(AssetDatabase.LoadAssetAtPath<PemtMotionLibrary>(path), null, -1, false);
        }

        internal static void OpenForSource(PemtMotionLibrary library, GameObject model, int selectedId, bool first)
        {
            var window = GetWindow<PemtAnimationPreviewWindow>("PEMT Animation Preview");
            window.minSize = new Vector2(430f, 525f);
            if (library != null) window.pemt = library;
            if (model != null) window.emd = model;
            if (selectedId >= 0) window.motionId = selectedId;
            window.deltaRotationFirst = first;
            window.ResetPreview();
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            DisposePreview();
        }

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = (float)Math.Max(0d, Math.Min(0.1d, now - lastTime));
            lastTime = now;
            if (!playing || clip == null || modelCopy == null) return;
            time += delta * speed;
            if (time >= duration)
            {
                if (loop) time %= duration;
                else { time = duration; playing = false; }
            }
            Sample();
            Repaint();
        }

        private void Sample()
        {
            if (clip != null && modelCopy != null)
                clip.SampleAnimation(modelCopy, Mathf.Clamp(time, 0f, duration));
        }

        private void DisposePreview()
        {
            playing = false;
            if (utility != null)
            {
                utility.Cleanup(); // also disposes objects added to its preview scene
                utility = null;
            }
            modelCopy = null;
            if (clip != null)
            {
                DestroyImmediate(clip);
                clip = null;
            }
            duration = 0f;
        }

        private void ResetPreview()
        {
            DisposePreview();
            time = 0f;
            feedback = "Click Build / Refresh Preview to prepare this motion.";
        }

        private static Bounds CalculateBounds(GameObject source)
        {
            bool found = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.one);
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh == null) continue;
                if (renderer is MeshRenderer meshRenderer)
                {
                    MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                }
                if (!found) { result = renderer.bounds; found = true; }
                else result.Encapsulate(renderer.bounds);
            }
            if (!found) throw new InvalidOperationException(
                "The selected EMD has no renderable meshes. Use an imported character EMD with an Armature, not an empty stage model.");
            return result;
        }

        private void Build(PemtMotionMetadata selected)
        {
            ResetPreview();
            if (pemt == null || emd == null || selected == null)
                throw new InvalidOperationException("Select a PEMT, matching EMD, and Motion ID.");
            try
            {
                // Decoder checks the imported rig and resolves linked prefab instances.
                clip = PemtAnimationClipExporter.CreateTransientClip(pemt, selected, emd, deltaRotationFirst);
                clip.hideFlags = HideFlags.HideAndDontSave;
                GameObject source = PemtAnimationClipExporter.GetSourceEmdModel(emd);
                utility = new PreviewRenderUtility();
                modelCopy = Instantiate(source);
                modelCopy.name = source.name + " [PEMT Preview]";
                modelCopy.hideFlags = HideFlags.HideAndDontSave;
                utility.AddSingleGO(modelCopy); // isolated PreviewScene, not the active Scene
                utility.ambientColor = new Color(0.4f, 0.4f, 0.4f);
                utility.lights[0].intensity = 1.3f;
                utility.lights[0].transform.rotation = Quaternion.Euler(40f, 35f, 0f);
                utility.lights[1].intensity = 0.85f;
                duration = Mathf.Max(0.01f, Mathf.Max(clip.length, selected.apparentDurationSeconds));
                Sample();
                bounds = CalculateBounds(modelCopy);
                feedback = "Loaded " + selected.motionIdHex + " | " +
                    duration.ToString("F3") + " s | 60 Hz keyframe timing";
            }
            catch
            {
                // Clean up even if construction, sampling, or rendering preparation fails.
                if (modelCopy != null && utility == null) DestroyImmediate(modelCopy);
                DisposePreview();
                throw;
            }
            Repaint();
        }

        private void DrawInputs()
        {
            EditorGUILayout.LabelField("PEMT Animation Preview", EditorStyles.boldLabel);
            PemtMotionLibrary next = (PemtMotionLibrary)EditorGUILayout.ObjectField(
                "PEMT Container", pemt, typeof(PemtMotionLibrary), false);
            if (next != pemt) { pemt = next; emd = null; motionId = -1; ResetPreview(); }
            if (GUILayout.Button("Use PEMT Selected in Project"))
            {
                string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                PemtMotionLibrary projectPemt = AssetDatabase.LoadAssetAtPath<PemtMotionLibrary>(selectedPath);
                if (projectPemt != null && projectPemt != pemt)
                {
                    pemt = projectPemt; emd = null; motionId = -1; ResetPreview();
                }
            }
            if (pemt == null)
            {
                EditorGUILayout.HelpBox("Select a .pemt file from Project or assign PEMT Container above.", MessageType.Info);
                return;
            }
            if (!string.IsNullOrEmpty(pemt.importError))
            {
                EditorGUILayout.HelpBox(pemt.importError, MessageType.Error);
                return;
            }
            if (pemt.motions == null || pemt.motions.Count == 0)
            {
                EditorGUILayout.HelpBox("Selected PEMT contains no motion entries.", MessageType.Warning);
                return;
            }
            List<PemtMotionMetadata> motions = pemt.motions;
            string[] names = new string[motions.Count];
            int current = -1;
            for (int i = 0; i < motions.Count; i++)
            {
                PemtMotionMetadata motion = motions[i];
                names[i] = motion.motionIdHex + " | " +
                    motion.apparentDurationSeconds.ToString("F3") + "s | " +
                    motion.tracks.Count + " tracks";
                if (motion.motionId == motionId) current = i;
            }
            if (current < 0)
            {
                current = motions.FindIndex(m => m.motionId != 0 && m.tracks.Count > 0);
                if (current < 0) current = 0;
                motionId = motions[current].motionId;
            }
            int chosenMotion = EditorGUILayout.Popup("Motion ID", current, names);
            if (chosenMotion != current && chosenMotion >= 0 && chosenMotion < motions.Count)
            {
                motionId = motions[chosenMotion].motionId;
                ResetPreview();
            }

            GameObject chosenModel = (GameObject)EditorGUILayout.ObjectField(
                "EMD Model", emd, typeof(GameObject), true);
            if (chosenModel != emd) { emd = chosenModel; ResetPreview(); }
            var nearby = PemtMotionPanel.FindNearbyEmdModels(pemt);
            if (nearby.Count > 0)
            {
                string[] modelNames = new string[nearby.Count + 1];
                modelNames[0] = "(Choose character EMD)";
                int active = 0;
                for (int i = 0; i < nearby.Count; i++)
                {
                    modelNames[i + 1] = nearby[i].name;
                    if (emd == nearby[i]) active = i + 1;
                }
                int choice = EditorGUILayout.Popup("Nearby EMD Models", active, modelNames);
                if (choice != active && choice > 0)
                {
                    emd = nearby[choice - 1];
                    ResetPreview();
                }
            }
            bool first = EditorGUILayout.Toggle("Delta Rotation First", deltaRotationFirst);
            if (first != deltaRotationFirst) { deltaRotationFirst = first; ResetPreview(); }
            using (new EditorGUI.DisabledScope(emd == null))
            {
                if (GUILayout.Button("Build / Refresh Preview", GUILayout.Height(27f)))
                {
                    try { Build(motions.Find(m => m.motionId == motionId)); }
                    catch (Exception ex)
                    {
                        feedback = "Cannot preview: " + ex.Message;
                        Debug.LogException(ex);
                        ShowNotification(new GUIContent(ex.Message));
                    }
                }
            }
        }

        private void DrawPlayback()
        {
            using (new EditorGUI.DisabledScope(clip == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(playing ? "Pause" : "Play"))
                {
                    playing = !playing;
                    lastTime = EditorApplication.timeSinceStartup;
                }
                if (GUILayout.Button("Stop"))
                {
                    playing = false; time = 0f; Sample();
                }
                if (GUILayout.Button("-1 Frame"))
                {
                    playing = false;
                    time = Mathf.Max(0f, time - 1f / 60f); Sample();
                }
                if (GUILayout.Button("+1 Frame"))
                {
                    playing = false;
                    time = Mathf.Min(duration, time + 1f / 60f); Sample();
                }
                EditorGUILayout.EndHorizontal();
                float nextTime = EditorGUILayout.Slider("Time", time, 0f, Mathf.Max(duration, 0.01f));
                if (!Mathf.Approximately(time, nextTime))
                {
                    playing = false;
                    time = nextTime; Sample();
                }
                EditorGUILayout.LabelField("Time", time.ToString("F3") + " / " +
                    duration.ToString("F3") + " seconds");
                loop = EditorGUILayout.Toggle("Loop", loop);
                speed = EditorGUILayout.Slider("Speed", speed, 0.1f, 2f);
            }
        }

        private void DrawModel(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.10f, 0.13f, 0.16f));
            if (utility == null || modelCopy == null)
            {
                GUI.Label(rect, "Build Preview to display the model",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
                return;
            }
            Event evt = Event.current;
            if (rect.Contains(evt.mousePosition))
            {
                if (evt.type == EventType.MouseDrag && evt.button == 0)
                {
                    yaw += evt.delta.x * 0.65f;
                    pitch = Mathf.Clamp(pitch - evt.delta.y * 0.65f, -85f, 85f);
                    evt.Use(); Repaint();
                }
                else if (evt.type == EventType.ScrollWheel)
                {
                    zoom = Mathf.Clamp(zoom * (1f + evt.delta.y * 0.075f), 0.35f, 6f);
                    evt.Use(); Repaint();
                }
            }
            if (evt.type != EventType.Repaint || rect.width <= 1f || rect.height <= 1f) return;
            float radius = Mathf.Max(0.1f, bounds.extents.magnitude);
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            Camera camera = utility.camera;
            camera.clearFlags = CameraClearFlags.Color;
            camera.backgroundColor = new Color(0.13f, 0.16f, 0.20f);
            camera.fieldOfView = 32f;
            camera.aspect = rect.width / rect.height;
            float distance = radius / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            distance *= zoom / Mathf.Min(1f, camera.aspect);
            camera.transform.rotation = orbit;
            camera.transform.position = bounds.center + orbit * (Vector3.back * distance);
            camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2.5f);
            camera.farClipPlane = Mathf.Max(100f, distance + radius * 4f);
            utility.BeginPreview(rect, GUIStyle.none);
            try { utility.Render(true, false); }
            finally { utility.EndAndDrawPreview(rect); }
        }

        private void OnGUI()
        {
            DrawInputs();
            EditorGUILayout.Space(4f);
            DrawPlayback();
            Rect rect = GUILayoutUtility.GetRect(1f, 300f, GUILayout.ExpandWidth(true));
            DrawModel(rect);
            EditorGUILayout.HelpBox(feedback, MessageType.Info);
            EditorGUILayout.LabelField("Drag to orbit, mouse wheel to zoom. Only a disposable preview copy is animated; no Scene changes or .anim files.", EditorStyles.wordWrappedMiniLabel);
        }
    }
}
#endif
