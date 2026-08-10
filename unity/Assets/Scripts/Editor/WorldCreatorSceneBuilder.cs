using System;
using System.IO;
using MLOmega.XR.Core;
using MLOmega.XR.Reflex;
using MLOmega.XR.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MLOmega.XR.Editor
{
    public static class WorldCreatorSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/XrealWorldCreator.unity";
        public const string LabScenePath = "Assets/Scenes/XrealWorldLab.unity";
        public const string OsScenePath = "Assets/Scenes/XReelOs.unity";
        public const string SecureSurfaceScenePath =
            "Assets/Scenes/XrealSecureSurfaceSpike.unity";
        private const string OfficialRigPrefabPath =
            "Packages/com.xreal.xr/Runtime/Prefabs/" +
            "XR Interaction Hands Setup.prefab";

        [MenuItem("MLOmega/XREAL/Build World Atelier Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            // HelloMR is the hardware-proven reference for One Pro + Eye on
            // the S24. Reuse its exact XREAL/XRI rig instead of rebuilding a
            // partial XR Origin by hand. This also brings the official input
            // actions, EventSystem and controller/hand interactors with it.
            GameObject rigPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    OfficialRigPrefabPath);
            if (rigPrefab == null)
                throw new FileNotFoundException(
                    "Official XREAL interaction rig missing.",
                    OfficialRigPrefabPath);
            GameObject rig = PrefabUtility.InstantiatePrefab(
                rigPrefab,
                scene) as GameObject;
            if (rig == null)
                throw new InvalidOperationException(
                    "Unable to instantiate the official XREAL interaction rig.");
            rig.name = "XR Interaction Hands Setup (Official)";
            // The official rig includes handset-oriented XRI reticles. They are
            // useful in HelloMR but create the red/white rays that follow S24
            // orientation. Atelier selection is gaze + Eye/MediaPipe pinch, so
            // remove only those visuals while retaining the proven tracking rig.
            foreach (MonoBehaviour behaviour in
                     rig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                // Some optional scripts in the imported XRI sample are absent
                // from the player assembly. Unity exposes those slots as null.
                if (behaviour == null) continue;
                string typeName = behaviour.GetType().FullName ?? string.Empty;
                if (
                    typeName.EndsWith("XRInteractorReticleVisual") ||
                    typeName.EndsWith("XRInteractorLineVisual"))
                    behaviour.enabled = false;
            }

            Camera camera = rig.GetComponentInChildren<Camera>(true);
            if (camera == null)
                throw new InvalidOperationException(
                    "Official XREAL interaction rig has no camera.");
            // Do not override the nested camera. HelloMR instantiates this
            // exact prefab without changing its clear path, render pipeline or
            // controller actions; that configuration is hardware-proven on the
            // S24 + One Pro + Eye. Atelier content is only added world-space.

            var root = new GameObject("MLOmega World Atelier");
            var runtimeAssets = root.AddComponent<XrealRuntimeAssets>();
            Assign(
                runtimeAssets,
                "_yuv420ToRgb",
                RequiredShader(PhoneOnlySceneBuilder.XrealYuvShaderPath));
            var permissions = root.AddComponent<PermissionGate>();
            var session = root.AddComponent<XrSessionController>();
            var pose = root.AddComponent<PosePublisher>();
            var capture = root.AddComponent<EyeCaptureSource>();
            var modelInstaller =
                root.AddComponent<StreamingAssetsModelInstaller>();
            var eyeGestures = root.AddComponent<GestureBridge>();
            var exchange = root.AddComponent<WorldMapDocumentExchange>();
            var creator = root.AddComponent<WorldCreatorController>();
            Assign(session, "_permissions", permissions);
            Assign(pose, "_session", session);
            Assign(capture, "_session", session);
            Assign(capture, "_pose", pose);
            Assign(eyeGestures, "_capture", capture);
            // Hardware capture is validated: keep Eye frames strictly ephemeral.
            // Diagnostics can still be re-enabled explicitly for a future gate.
            Assign(eyeGestures, "_deviceDiagnostics", false);
            Assign(eyeGestures, "_useDedicatedEyePinchPipeline", true);
            Assign(eyeGestures, "_modelRelativePath", "models/hand_landmarker.task");
            // Keep the hardware-proven 768 px Eye geometry. The Atelier is
            // short-lived and may use 25 fps; product remains 12/15.
            Assign(eyeGestures, "_maxDimension", 768);
            Assign(eyeGestures, "_targetFps", 25f);
            Assign(eyeGestures, "_numHands", 2);
            Assign(creator, "_camera", camera);
            Assign(creator, "_exchange", exchange);

            Type spatialType = Type.GetType(
                "MLOmega.XR.UI.XrealSpatialProvider, MLOmega.XR.XrealSpatial",
                false);
            if (
                spatialType == null ||
                !typeof(MonoBehaviour).IsAssignableFrom(spatialType))
                throw new InvalidOperationException(
                    "XREAL spatial assembly unavailable; run PrepareDefines first.");
            Component spatial = root.AddComponent(spatialType);
            Assign(spatial, "_creatorMode", true);
            Assign(spatial, "_camera", camera);
            Assign(
                spatial,
                "_depthOcclusionShader",
                RequiredShader(PhoneOnlySceneBuilder.XrealDepthOcclusionShaderPath));
            Assign(
                spatial,
                "_freeGuyMeshShader",
                RequiredShader(PhoneOnlySceneBuilder.XrealFreeGuyMeshShaderPath));
            Assign(creator, "_spatialBehaviour", spatial);

            Type pointerType = Type.GetType(
                "MLOmega.XR.UI.XrealNativeHandPointer, " +
                "MLOmega.XR.XrealSpatial",
                false);
            if (
                pointerType == null ||
                !typeof(MonoBehaviour).IsAssignableFrom(pointerType))
                throw new InvalidOperationException(
                    "XREAL native hand pointer assembly unavailable.");
            Component pointer = root.AddComponent(pointerType);
            Assign(pointer, "_camera", camera);
            Assign(pointer, "_creator", creator);
            Assign(pointer, "_eyeGestures", eyeGestures);
            Assign(pointer, "_modelInstaller", modelInstaller);
            Assign(pointer, "_activateEyeGesturesContinuously", true);
            Assign(pointer, "_allowPhoneController", false);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException(
                    "Unable to save World Atelier scene.");
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[WorldCreatorSceneBuilder] isolated XREAL Atelier ready: " +
                ScenePath);
        }

        /// <summary>
        /// Clone the hardware-validated Atelier scene and add the experimental
        /// spatial browser shell only to the clone. The stable scene remains a
        /// byte-for-byte independent build input.
        /// </summary>
        [MenuItem("MLOmega/XREAL/Build World Lab Scene")]
        public static void BuildLaboratoryScene()
        {
            BuildScene();
            var scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            GameObject root = GameObject.Find("MLOmega World Atelier");
            if (root == null)
                throw new InvalidOperationException(
                    "Validated Atelier root missing while creating Lab scene.");
            Type labType = Type.GetType(
                "MLOmega.XR.UI.WorldCreatorLabShell, MLOmega.XR.Lab",
                false);
            if (
                labType == null ||
                !typeof(MonoBehaviour).IsAssignableFrom(labType))
                throw new InvalidOperationException(
                    "Isolated MLOmega XR Lab assembly unavailable.");
            if (root.GetComponent(labType) == null)
                root.AddComponent(labType);
            Directory.CreateDirectory(Path.GetDirectoryName(LabScenePath));
            if (!EditorSceneManager.SaveScene(scene, LabScenePath, true))
                throw new InvalidOperationException(
                    "Unable to save isolated World Lab scene.");
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[WorldCreatorSceneBuilder] isolated XR browser Lab ready: " +
                LabScenePath);
        }

        /// <summary>
        /// Builds the standalone community OS scene. It reuses the hardware-
        /// validated XREAL rig and interaction stack, but disables every World
        /// Atelier entry point: no creator deck, map exchange or Memory runtime.
        /// </summary>
        [MenuItem("XReel OS/Build Community OS Scene")]
        public static void BuildCommunityOsScene()
        {
            BuildScene();
            var scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            GameObject root = GameObject.Find("MLOmega World Atelier");
            if (root == null)
                throw new InvalidOperationException(
                    "Validated XREAL root missing while creating XReel OS scene.");
            root.name = "XReel OS Runtime";

            var creator = root.GetComponent<WorldCreatorController>();
            if (creator == null)
                throw new InvalidOperationException(
                    "XREAL interaction controller missing from OS scene.");
            Assign(creator, "_osOnlyMode", true);
            Assign(creator, "_exchange", (UnityEngine.Object)null);

            var exchange = root.GetComponent<WorldMapDocumentExchange>();
            if (exchange != null)
                UnityEngine.Object.DestroyImmediate(exchange);

            foreach (MonoBehaviour behaviour in root.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null ||
                    behaviour.GetType().Name != "XrealSpatialProvider") continue;
                Assign(behaviour, "_creatorMode", false);
            }

            Type labType = Type.GetType(
                "MLOmega.XR.UI.WorldCreatorLabShell, MLOmega.XR.Lab",
                false);
            if (labType == null || !typeof(MonoBehaviour).IsAssignableFrom(labType))
                throw new InvalidOperationException(
                    "XReel OS shell assembly unavailable.");
            Component shell = root.GetComponent(labType) ?? root.AddComponent(labType);
            Assign(shell, "_osOnlyMode", true);

            Directory.CreateDirectory(Path.GetDirectoryName(OsScenePath));
            if (!EditorSceneManager.SaveScene(scene, OsScenePath, true))
                throw new InvalidOperationException(
                    "Unable to save XReel OS scene.");
            AssetDatabase.SaveAssets();
            Debug.Log("[XReel OS] standalone scene ready: " + OsScenePath);
        }

        /// <summary>
        /// Builds a separate XREAL scene used only to prove Android protected
        /// surface composition. The validated Atelier/Lab scenes are build inputs,
        /// never outputs, of this spike.
        /// </summary>
        public static void BuildSecureSurfaceSpikeScene()
        {
            BuildScene();
            var scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            GameObject root = GameObject.Find("MLOmega World Atelier");
            if (root == null)
                throw new InvalidOperationException(
                    "Validated Atelier root missing while creating secure-surface spike.");

            string[] disabledTypes =
            {
                "XrealSpatialProvider",
                "WorldMapDocumentExchange",
            };
            foreach (MonoBehaviour behaviour in
                     root.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null) continue;
                if (Array.IndexOf(disabledTypes, behaviour.GetType().Name) >= 0)
                    behaviour.enabled = false;
            }

            Type spikeType = Type.GetType(
                "MLOmega.XR.SecureSurfaceSpike.XrealSecureSurfaceSpike, " +
                "MLOmega.XR.SecureSurfaceSpike",
                false);
            if (spikeType == null || !typeof(MonoBehaviour).IsAssignableFrom(spikeType))
                throw new InvalidOperationException(
                    "Isolated secure-surface spike assembly unavailable.");
            root.AddComponent(spikeType);

            Directory.CreateDirectory(
                Path.GetDirectoryName(SecureSurfaceScenePath));
            if (!EditorSceneManager.SaveScene(
                    scene,
                    SecureSurfaceScenePath,
                    true))
                throw new InvalidOperationException(
                    "Unable to save isolated secure-surface scene.");
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[WorldCreatorSceneBuilder] isolated secure-surface spike ready: " +
                SecureSurfaceScenePath);
        }

        private static Shader RequiredShader(string path)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
                throw new FileNotFoundException("Required shader missing: " + path);
            return shader;
        }

        private static void Assign(
            UnityEngine.Object target,
            string field,
            UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, field);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Assign(
            UnityEngine.Object target,
            string field,
            bool value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, field);
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Assign(
            UnityEngine.Object target,
            string field,
            string value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, field);
            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Assign(
            UnityEngine.Object target,
            string field,
            int value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, field);
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Assign(
            UnityEngine.Object target,
            string field,
            float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, field);
            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
