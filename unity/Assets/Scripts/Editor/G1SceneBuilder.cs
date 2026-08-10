// MLOmega V19 â€” E22 / Gate G1
// Builds the G1Gate scene in one click. Authoring a valid .unity YAML by hand
// (GUIDs, fileIDs, component wiring) is error-prone without Unity to validate it,
// so the scene is generated programmatically instead (decision recorded in
// docs/DECISIONS.md). Menu: MLOmega > Build G1 Gate Scene.
using System.IO;
using MLOmega.XR.Core;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MLOmega.XR.Editor
{
    public static class G1SceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/G1Gate.unity";

        [MenuItem("MLOmega/Build G1 Gate Scene")]
        public static void BuildScene()
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- XR camera rig -------------------------------------------------
            // On device the XREAL loader drives the head pose of the main camera.
            var rig = new GameObject("XR Rig");
            var camGo = new GameObject("Main Camera");
            camGo.transform.SetParent(rig.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.04f, 1f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            camGo.AddComponent<AudioListener>();

            // --- Session controller (owns the adapter) -------------------------
            var sessionGo = new GameObject("XR Session");
            var session = sessionGo.AddComponent<XrSessionController>();

            // --- Permission gate ----------------------------------------------
            var permsGo = new GameObject("Permission Gate");
            var perms = permsGo.AddComponent<PermissionGate>();

            // --- Eye preview quad ---------------------------------------------
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Eye Preview Quad";
            quad.transform.SetParent(rig.transform, false);
            quad.transform.localPosition = new Vector3(0f, 1.4f, 2.0f);
            quad.transform.localScale = new Vector3(1.6f, 0.9f, 1f);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            var preview = quad.AddComponent<EyeCapturePreview>();
            AssignPrivate(preview, "_session", session);
            quad.GetComponent<Renderer>().sharedMaterial =
                new Material(Shader.Find("MLOmega/XREAL Runtime Unlit"));

            // --- Pose readout --------------------------------------------------
            var poseGo = new GameObject("Pose Readout");
            var pose = poseGo.AddComponent<PoseReadout>();
            AssignPrivate(pose, "_session", session);

            // --- World-space status canvas ------------------------------------
            var canvasGo = new GameObject("Status Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.SetParent(rig.transform, false);
            canvasRt.sizeDelta = new Vector2(900f, 700f);
            canvasRt.localScale = Vector3.one * 0.0016f;
            canvasRt.localPosition = new Vector3(-1.3f, 1.4f, 2.0f);

            var textGo = new GameObject("Status Text");
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.SetParent(canvasRt, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(16f, 16f);
            textRt.offsetMax = new Vector2(-16f, -16f);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 28f;
            tmp.color = new Color(0.85f, 0.95f, 1f, 1f);
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.text = "MLOmega XR â€” G1 Gate\n(initializing...)";

            var overlay = canvasGo.AddComponent<G1StatusOverlay>();
            AssignPrivate(overlay, "_label", tmp);
            AssignPrivate(overlay, "_session", session);
            AssignPrivate(overlay, "_preview", preview);
            AssignPrivate(overlay, "_pose", pose);
            AssignPrivate(overlay, "_permissions", perms);

            // --- Light ---------------------------------------------------------
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            if (saved)
            {
                Debug.Log($"[G1SceneBuilder] Saved scene to {ScenePath}");
                AssetDatabase.Refresh();
                EnsureSceneInBuildSettings(ScenePath);
            }
            else
            {
                Debug.LogError("[G1SceneBuilder] Failed to save G1Gate scene.");
            }
        }

        private static void AssignPrivate(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning($"[G1SceneBuilder] Field '{field}' not found on {target.GetType().Name}.");
            }
        }

        private static void EnsureSceneInBuildSettings(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == path))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log("[G1SceneBuilder] Added G1Gate to Build Settings.");
            }
        }
    }
}
