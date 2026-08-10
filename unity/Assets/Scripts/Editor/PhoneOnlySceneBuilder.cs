using System;
using System.Collections.Generic;
using System.IO;
using MLOmega.XR.Core;
using MLOmega.XR.Reflex;
using MLOmega.XR.Reflex.Skills;
using MLOmega.XR.Scene;
using MLOmega.XR.Transport;
using MLOmega.XR.UI;
using MLOmega.XR.UI.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

namespace MLOmega.XR.Editor
{
    public static class PhoneOnlySceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/PhoneOnly.unity";
        private const string ConfigPath = "Assets/Config/MLOmegaPhoneOnly.asset";
        public const string XrealScenePath = "Assets/Scenes/XrealProduct.unity";
        public const string XrealConfigPath = "Assets/Config/MLOmegaXreal.asset";
        public const string XrealYuvShaderPath = "Assets/Shaders/YUV420ToRGB.shader";
        public const string XrealDepthOcclusionShaderPath =
            "Assets/Shaders/XrealDepthOcclusion.shader";
        public const string XrealFreeGuyMeshShaderPath =
            "Assets/Shaders/XrealFreeGuyMesh.shader";
        private const string CacheConfigPath = "Assets/Settings/PhoneOnlySceneCacheConfig.asset";
        private const string ThemePath = "Assets/Settings/PhoneOnlyUITheme.asset";

        [MenuItem("MLOmega/Build PhoneOnly Scene")]
        public static void BuildScene() =>
            BuildProductScene(ScenePath, ConfigPath, XrAdapterKind.PhoneOnly, phonePreview: true);

        [MenuItem("MLOmega/XREAL/Build Product Scene")]
        public static void BuildXrealScene() =>
            BuildProductScene(XrealScenePath, XrealConfigPath, XrAdapterKind.Xreal, phonePreview: false);

        private static void BuildProductScene(string scenePath, string configPath,
            XrAdapterKind adapterKind, bool phonePreview)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var config = LoadOrCreateConfig(configPath, adapterKind);

            var cameraGo = new GameObject("Phone Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraGo.AddComponent<AudioListener>();
            if (adapterKind == XrAdapterKind.Xreal)
            {
                BuildXrealRig(cameraGo, camera);
            }

            var root = new GameObject(adapterKind == XrAdapterKind.Xreal ? "XREAL Product Session" : "PhoneOnly Session");
            if (adapterKind == XrAdapterKind.Xreal)
            {
                Shader yuv = AssetDatabase.LoadAssetAtPath<Shader>(XrealYuvShaderPath);
                if (yuv == null)
                    throw new FileNotFoundException(
                        $"XREAL YUV shader missing: {XrealYuvShaderPath}");
                var runtimeAssets = root.AddComponent<XrealRuntimeAssets>();
                Assign(runtimeAssets, "_yuv420ToRgb", yuv);
            }
            var permissions = root.AddComponent<PermissionGate>();
            var session = root.AddComponent<XrSessionController>();
            var pairing = root.AddComponent<SessionPairing>();
            var pose = root.AddComponent<PosePublisher>();
            var capture = root.AddComponent<EyeCaptureSource>();
            var orientation = root.AddComponent<OrientationGuard>();
            var transport = root.AddComponent<LiveTransportBridge>();
            // E48-A: install APK-embedded small models at first launch, then
            // download any still-missing device models in the background.
            var modelInstaller = root.AddComponent<StreamingAssetsModelInstaller>();
            var provisioning = root.AddComponent<ModelProvisioningBridge>();
            var coordinator = root.AddComponent<PhoneOnlySessionCoordinator>();
            var preview = phonePreview ? cameraGo.AddComponent<PhoneCameraPreview>() : null;

            // Real phone UI path: consume PC UIIntent/SceneDelta messages and
            // render the same component registry as the glasses, without the E25
            // demo driver or any simulated source.
            var cacheConfig = LoadOrCreate<SceneCacheConfig>(CacheConfigPath);
            var theme = LoadOrCreate<UITheme>(ThemePath);
            var cache = root.AddComponent<SceneCache>();
            var tracks = root.AddComponent<LocalTrackStore>();
            var broker = root.AddComponent<UIIntentBroker>();
            var intentSource = root.AddComponent<TransportIntentSource>();
            var sourceBootstrap = root.AddComponent<E25SourceBootstrap>();
            var receiptSink = root.AddComponent<UIReceiptTransportSink>();
            var uiRuntime = root.AddComponent<UIRuntime>();
            var statusBar = root.AddComponent<StatusBar>();
            var entityHot = root.AddComponent<EntityHotUpdateHandler>();
            var sceneDelta = root.AddComponent<SceneDeltaTransportHandler>();
            var appLauncher = root.AddComponent<AppLauncherBridge>();
            var commands = root.AddComponent<DeviceCommandHandler>();
            var ttsPlayer = root.AddComponent<TtsAudioPlayer>();
            var augmentedReality = root.AddComponent<AugmentedRealityFeatureRegistry>();
            Component xrealSpatial = null;
            Component xrealHandPointer = null;
            XrealSpatialGestureController xrealSpatialGestures = null;
            if (adapterKind == XrAdapterKind.Xreal)
            {
                // Product-only XREAL provider. PhoneOnly receives neither this
                // component nor AR Foundation/Depth managers.
                Type spatialType = Type.GetType(
                    "MLOmega.XR.UI.XrealSpatialProvider, " +
                    "MLOmega.XR.XrealSpatial",
                    false);
                if (spatialType == null ||
                    !typeof(MonoBehaviour).IsAssignableFrom(spatialType))
                {
                    throw new Exception(
                        "XREAL spatial assembly is unavailable. Run " +
                        "AndroidBuildXreal.PrepareDefines first.");
                }
                xrealSpatial = root.AddComponent(spatialType);
                Type pointerType = Type.GetType(
                    "MLOmega.XR.UI.XrealNativeHandPointer, " +
                    "MLOmega.XR.XrealSpatial",
                    false);
                if (
                    pointerType == null ||
                    !typeof(MonoBehaviour).IsAssignableFrom(pointerType))
                    throw new Exception(
                        "XREAL native hand pointer assembly is unavailable.");
                xrealHandPointer = root.AddComponent(pointerType);
                xrealSpatialGestures =
                    root.AddComponent<XrealSpatialGestureController>();
            }

            // E48-A: the Ultra-Live reflex layer (E26/E47). GAP FIX — these components
            // were never added to the PhoneOnly scene, so the E47 device gates (wake
            // word, gestures, offline subtitles) had nothing to run them. The skills
            // emit through their own LocalIntentSource (priority-2 UL seam), registered
            // with the broker by a second E25SourceBootstrap.
            var localIntents = root.AddComponent<LocalIntentSource>();
            var localBootstrap = root.AddComponent<E25SourceBootstrap>();
            var asrBridge = root.AddComponent<AsrBridge>();
            var gestureBridge = root.AddComponent<GestureBridge>();
            var instantImageLabels = root.AddComponent<InstantImageLabelBridge>();
            var semanticSound = root.AddComponent<SemanticSoundBridge>();
            var pulseAura = root.AddComponent<PulseAuraBridge>();
            var wakeGate = root.AddComponent<WakeWordGate>();
            var stableTrack = root.AddComponent<StableTrackSkill>();
            var lensWindow = root.AddComponent<LensWindowSkill>();
            var motionProximity = root.AddComponent<MotionProximitySkill>();
            var focusSearch = root.AddComponent<FocusSearchSkill>();
            var subtitle = root.AddComponent<SubtitleSkill>();
            var translate = root.AddComponent<TranslateBridge>();
            // E59: hand window-management (grab/resize/close/minimise of manipulable panels).
            var panelManipulator = root.AddComponent<PanelManipulator>();
            var reflex = root.AddComponent<ReflexScheduler>();
            var reflexSignals = root.AddComponent<PhoneOnlyReflexSignalSource>();

            // MenuPanel disables its own GameObject while closed, so it must live on
            // a child rather than disabling the entire PhoneOnly session root.
            var menuGo = new GameObject("PhoneOnly Menu");
            menuGo.transform.SetParent(root.transform, false);
            var menu = menuGo.AddComponent<MenuPanel>();
            var menuGestures = root.AddComponent<MenuGestureController>();

            Assign(session, "_config", config);
            Assign(session, "_permissions", permissions);
            Assign(pairing, "_config", config);
            Assign(capture, "_session", session);
            Assign(capture, "_pairing", pairing);
            Assign(capture, "_pose", pose);
            Assign(orientation, "_capture", capture);
            Assign(orientation, "_pose", pose);
            Assign(orientation, "_session", session);
            Assign(transport, "_pairing", pairing);
            Assign(transport, "_capture", capture);
            Assign(coordinator, "_pairing", pairing);
            Assign(coordinator, "_transport", transport);
            Assign(coordinator, "_session", session);
            if (preview != null)
            {
                Assign(preview, "_session", session);
                Assign(preview, "_camera", camera);
            }
            Assign(cache, "_config", cacheConfig);
            Assign(tracks, "_sceneCache", cache);
            Assign(broker, "_sceneCache", cache);
            Assign(broker, "_config", cacheConfig);
            Assign(intentSource, "_bridge", transport);
            Assign(sourceBootstrap, "_broker", broker);
            Assign(sourceBootstrap, "_source", intentSource);
            Assign(receiptSink, "_bridge", transport);
            Assign(uiRuntime, "_broker", broker);
            Assign(uiRuntime, "_sceneCache", cache);
            Assign(uiRuntime, "_theme", theme);
            Assign(uiRuntime, "_camera", camera);
            Assign(uiRuntime, "_pairing", pairing);
            Assign(uiRuntime, "_receiptSinkBehaviour", receiptSink);
            Assign(statusBar, "_theme", theme);
            Assign(statusBar, "_camera", camera);
            Assign(statusBar, "_transport", transport);
            Assign(statusBar, "_session", session);
            Assign(statusBar, "_provisioning", provisioning);
            Assign(orientation, "_statusBar", statusBar);
            // E48-A provisioning wiring.
            Assign(provisioning, "_pairing", pairing);
            Assign(provisioning, "_installer", modelInstaller);
            Assign(entityHot, "_sceneCache", cache);
            Assign(entityHot, "_transport", transport);
            Assign(sceneDelta, "_transport", transport);
            Assign(sceneDelta, "_sceneCache", cache);
            Assign(sceneDelta, "_tracks", tracks);
            Assign(commands, "_broker", broker);
            Assign(commands, "_statusBar", statusBar);
            Assign(commands, "_appLauncher", appLauncher);
            Assign(commands, "_transport", transport);
            Assign(commands, "_session", session);
            Assign(commands, "_augmentedReality", augmentedReality);
            Assign(commands, "_localIntents", localIntents);
            if (xrealSpatial != null)
                Assign(commands, "_xrealSpatial", xrealSpatial);
            Assign(ttsPlayer, "_transport", transport);
            Assign(augmentedReality, "_transport", transport);
            Assign(augmentedReality, "_statusBar", statusBar);
            if (xrealSpatial != null)
            {
                Assign(xrealSpatial, "_features", augmentedReality);
                Assign(xrealSpatial, "_transport", transport);
                Assign(xrealSpatial, "_intents", localIntents);
                Assign(xrealSpatial, "_pose", pose);
                Assign(xrealSpatial, "_camera", camera);
                Assign(
                    xrealSpatial,
                    "_depthOcclusionShader",
                    LoadRequiredShader(XrealDepthOcclusionShaderPath));
                Assign(
                    xrealSpatial,
                    "_freeGuyMeshShader",
                    LoadRequiredShader(XrealFreeGuyMeshShaderPath));
            }
            if (xrealHandPointer != null)
                Assign(xrealHandPointer, "_camera", camera);
            if (xrealSpatialGestures != null)
            {
                Assign(xrealSpatialGestures, "_gestures", gestureBridge);
                Assign(xrealSpatialGestures, "_spatial", xrealSpatial);
                Assign(xrealSpatialGestures, "_features", augmentedReality);
                Assign(xrealSpatialGestures, "_transport", transport);
            }
            // E48-A reflex wiring (the rest self-finds in Awake at scene load).
            Assign(translate, "_commands", commands);
            Assign(translate, "_statusBar", statusBar);
            Assign(localBootstrap, "_broker", broker);
            Assign(localBootstrap, "_source", localIntents);
            Assign(reflex, "_gestureBridge", gestureBridge);
            Assign(reflex, "_asrBridge", asrBridge);
            Assign(reflex, "_stableTrack", stableTrack);
            Assign(reflex, "_lensWindow", lensWindow);
            Assign(reflex, "_motionProximity", motionProximity);
            Assign(reflex, "_focusSearch", focusSearch);
            Assign(reflex, "_subtitle", subtitle);
            Assign(reflex, "_commands", commands);
            // E59: the manipulator runs BEFORE the lens on the pinch stream (claim → no zoom).
            Assign(reflex, "_panelManipulator", panelManipulator);
            Assign(reflexSignals, "_scheduler", reflex);
            Assign(reflexSignals, "_session", session);
            Assign(instantImageLabels, "_capture", capture);
            Assign(instantImageLabels, "_transport", transport);
            Assign(instantImageLabels, "_features", augmentedReality);
            Assign(semanticSound, "_transport", transport);
            Assign(semanticSound, "_features", augmentedReality);
            Assign(semanticSound, "_intentSource", localIntents);
            Assign(pulseAura, "_capture", capture);
            Assign(pulseAura, "_transport", transport);
            Assign(pulseAura, "_features", augmentedReality);
            Assign(pulseAura, "_intents", localIntents);
            Assign(panelManipulator, "_camera", camera);
            Assign(wakeGate, "_asr", asrBridge);
            Assign(wakeGate, "_intentSource", localIntents);
            Assign(wakeGate, "_statusBar", statusBar);
            Assign(translate, "_asrBridge", asrBridge);
            Assign(translate, "_subtitle", subtitle);
            Assign(translate, "_config", config);
            Assign(menu, "_commandHandler", commands);
            Assign(menu, "_theme", theme);
            Assign(menu, "_camera", camera);
            Assign(menu, "_augmentedReality", augmentedReality);
            Assign(menuGestures, "_gestures", gestureBridge);
            Assign(menuGestures, "_menu", menu);
            Assign(menuGestures, "_commandHandler", commands);

            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));

            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new System.InvalidOperationException($"Unable to save product scene: {scenePath}");
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == scenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[PhoneOnlySceneBuilder] {adapterKind} product scene ready: {scenePath}");
        }

        private static void BuildXrealRig(GameObject cameraGo, Camera camera)
        {
            var originGo = new GameObject("XR Origin (XREAL)");
            var cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(originGo.transform, false);
            cameraGo.transform.SetParent(cameraOffset.transform, false);

            var origin = originGo.AddComponent<XROrigin>();
            origin.Origin = originGo;
            origin.Camera = camera;
            origin.CameraFloorOffsetObject = cameraOffset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;

            // Embedded actions are enabled automatically by TrackedPoseDriver;
            // no external Starter Assets/InputActionManager is required.
            var trackedPose = cameraGo.AddComponent<TrackedPoseDriver>();
            trackedPose.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            trackedPose.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            trackedPose.ignoreTrackingState = false;
            trackedPose.positionInput = new InputActionProperty(new InputAction(
                "XREAL Head Position", InputActionType.Value,
                "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            trackedPose.rotationInput = new InputActionProperty(new InputAction(
                "XREAL Head Rotation", InputActionType.Value,
                "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            trackedPose.trackingStateInput = new InputActionProperty(new InputAction(
                "XREAL Head Tracking State", InputActionType.Value,
                "<XRHMD>/trackingState", expectedControlType: "Integer"));

            // XREAL's documented AR camera comfort defaults.
            camera.fieldOfView = 25f;
            camera.nearClipPlane = 0.1f;
        }

        private static MLOmegaConfig LoadOrCreateConfig(string configPath, XrAdapterKind adapterKind)
        {
            var config = AssetDatabase.LoadAssetAtPath<MLOmegaConfig>(configPath);
            bool created = config == null;
            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                config = ScriptableObject.CreateInstance<MLOmegaConfig>();
            }
            // The glasses are another presentation/capture surface for the same
            // product, not a second deployment profile. Refresh every generated
            // XREAL config from the authoritative PhoneOnly asset so LAN/Tailscale
            // endpoints, wake word, language and timing knobs cannot silently fall
            // back to MLOmegaConfig's development defaults.
            if (adapterKind == XrAdapterKind.Xreal)
            {
                var phoneConfig = AssetDatabase.LoadAssetAtPath<MLOmegaConfig>(ConfigPath);
                if (phoneConfig == null)
                    throw new FileNotFoundException(
                        $"Authoritative PhoneOnly config missing: {ConfigPath}");
                EditorUtility.CopySerialized(phoneConfig, config);
                config.name = Path.GetFileNameWithoutExtension(configPath);
            }
            var so = new SerializedObject(config);
            so.FindProperty("_adapter").enumValueIndex = (int)adapterKind;
            so.FindProperty("_deviceId").stringValue = adapterKind == XrAdapterKind.Xreal
                ? "xreal-primary" : "phone-only-primary";
            so.ApplyModifiedPropertiesWithoutUndo();
            if (created) AssetDatabase.CreateAsset(config, configPath);
            else EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
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

        private static Shader LoadRequiredShader(string path)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
                throw new FileNotFoundException(
                    $"Required XREAL shader missing: {path}");
            return shader;
        }

        private static void Assign(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
