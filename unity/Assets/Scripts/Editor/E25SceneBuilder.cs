// MLOmega V19 â€” E25
// Builds the E25 UI demo scene in one click (menu: MLOmega > Build E25 UI Scene).
// As with G1SceneBuilder, authoring a valid .unity by hand is error-prone without
// Unity to validate GUIDs/fileIDs, so the scene is generated programmatically and
// the wiring is auditable here. The scene contains: an XR camera rig, a SceneCache
// + SceneCacheConfig, a UIIntentBroker, a LiquidGlass material, a UIRuntime, a
// permanent StatusBar, a LocalIntentSource, and an E25DemoDriver that injects one
// intent of each component (with simulated tracks) so the whole design system is
// visible in Play mode without a PC/transport.
using System.IO;
using MLOmega.XR.Core;
using MLOmega.XR.Scene;
using MLOmega.XR.UI;
using MLOmega.XR.UI.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MLOmega.XR.Editor
{
    public static class E25SceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/E25UI.unity";
        private const string ThemePath = "Assets/Settings/UITheme.asset";
        private const string ConfigPath = "Assets/Settings/SceneCacheConfig.asset";

        [MenuItem("MLOmega/Build E25 UI Scene")]
        public static void BuildScene()
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Config assets (created once, reused) --------------------------
            UITheme theme = LoadOrCreate<UITheme>(ThemePath);
            SceneCacheConfig config = LoadOrCreate<SceneCacheConfig>(ConfigPath);

            // --- Glass material ------------------------------------------------
            Shader glassShader = Shader.Find("MLOmega/LiquidGlass");
            Material glass = glassShader != null ? new Material(glassShader) : null;

            // --- Camera rig ----------------------------------------------------
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

            // --- SceneCache ----------------------------------------------------
            var cacheGo = new GameObject("Scene Cache");
            var cache = cacheGo.AddComponent<SceneCache>();
            AssignPrivate(cache, "_config", config);

            // --- Broker --------------------------------------------------------
            var brokerGo = new GameObject("UI Intent Broker");
            var broker = brokerGo.AddComponent<UIIntentBroker>();
            AssignPrivate(broker, "_sceneCache", cache);
            AssignPrivate(broker, "_config", config);

            // --- Local intent source ------------------------------------------
            var sourceGo = new GameObject("Local Intent Source");
            var source = sourceGo.AddComponent<LocalIntentSource>();

            // --- Receipt sink (editor: logs; no transport in editor) ----------
            var sinkGo = new GameObject("Receipt Sink");
            var sink = sinkGo.AddComponent<UIReceiptTransportSink>();

            // --- UIRuntime -----------------------------------------------------
            var runtimeGo = new GameObject("UI Runtime");
            var runtime = runtimeGo.AddComponent<UIRuntime>();
            AssignPrivate(runtime, "_broker", broker);
            AssignPrivate(runtime, "_sceneCache", cache);
            AssignPrivate(runtime, "_theme", theme);
            AssignPrivate(runtime, "_camera", cam);
            AssignPrivate(runtime, "_glassMaterial", glass);
            AssignPrivate(runtime, "_receiptSinkBehaviour", sink);

            // --- StatusBar (permanent, head-locked) ---------------------------
            var statusGo = new GameObject("Status Bar");
            statusGo.transform.SetParent(rig.transform, false);
            var status = statusGo.AddComponent<StatusBar>();
            AssignPrivate(status, "_theme", theme);
            AssignPrivate(status, "_glassMaterial", glass);
            AssignPrivate(status, "_camera", cam);

            // --- Demo driver: register the source + inject one intent per comp -
            var driverGo = new GameObject("E25 Demo Driver");
            var driver = driverGo.AddComponent<E25DemoDriver>();
            AssignPrivate(driver, "_sceneCache", cache);
            AssignPrivate(driver, "_source", source);
            AssignPrivate(driver, "_broker", broker);

            // The broker must know the source. A tiny bootstrap wires it at start.
            var bootGo = new GameObject("Source Bootstrap");
            var boot = bootGo.AddComponent<E25SourceBootstrap>();
            AssignPrivate(boot, "_broker", broker);
            AssignPrivate(boot, "_source", source);

            // --- EventSystem ---------------------------------------------------
            // CorrectionChip supports pointer clicks (IPointerClickHandler), but on
            // device it is normally driven by gesture/voice via Activate(); we add a
            // bare EventSystem (no input module, to avoid coupling to a specific
            // input backend) so the UI hierarchy is complete.
            var esGo = new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem));

            // --- Light ---------------------------------------------------------
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            if (saved)
            {
                Debug.Log($"[E25SceneBuilder] Saved scene to {ScenePath}. Enter Play mode to see the design system.");
                AssetDatabase.Refresh();
                EnsureSceneInBuildSettings(ScenePath);
            }
            else
            {
                Debug.LogError("[E25SceneBuilder] Failed to save E25UI scene.");
            }
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
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
                Debug.LogWarning($"[E25SceneBuilder] Field '{field}' not found on {target.GetType().Name}.");
            }
        }

        private static void EnsureSceneInBuildSettings(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == path))
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log("[E25SceneBuilder] Added E25UI to Build Settings.");
            }
        }
    }
}
