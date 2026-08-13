// MLOmega V19 — E49 / Gate G1
// Reproducible batchmode Android APK build for the XREAL glasses profile.
//
// Runs headless:
//   Unity.exe -batchmode -quit -projectPath <apps/xr-mobile> \
//     -executeMethod MLOmega.XR.Editor.AndroidBuildXreal.BuildApk -logFile -
//
// Differences from the PhoneOnly build (AndroidBuild.cs):
//   * enables the XREAL_SDK_PRESENT define (activates the real XrealDeviceAdapter),
//     NOT MLOMEGA_PHONE_ONLY;
//   * injects the com.xreal.xr file: dependency into Packages/manifest.json at build
//     time (the proprietary tarball lives under Packages/xreal-sdk/, git-ignored — so
//     the committed manifest stays XREAL-free and a PhoneOnly clone without the SDK
//     keeps building);
//   * activates the XREAL XR loader for Android (XR Plug-in Management);
//   * builds the full product scene with XrealDeviceAdapter. G1Gate remains a
//     separate hardware diagnostic scene, never the shipped product APK.
//
// PrepareDefines is a separate entry point so a first pass can set the define + import
// the SDK before the compile that exercises the real adapter path.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager.UI;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MLOmega.XR.Editor
{
    public static class AndroidBuildXreal
    {
        private const string ScenePath = PhoneOnlySceneBuilder.XrealScenePath;
        private const string ManifestPath = "Packages/manifest.json";
        private const string TarballRel = "Packages/xreal-sdk/com.xreal.xr.tar.gz";
        private const string XReelIconPath = "Assets/Brand/XReelOsIcon.png";
        private const string XrealDep = "\"com.xreal.xr\": \"file:xreal-sdk/com.xreal.xr.tar.gz\"";
        private const string ArFoundationDep =
            "\"com.unity.xr.arfoundation\": \"6.0.6\"";
        // Keep these aligned byte-for-byte with XREAL's hardware-proven
        // SDKTemplate. The SDK 3.1 prefabs reference the 2.6.5/1.4.3 sample
        // GUIDs; importing XRI 3.0.9 leaves the nested camera rig unresolved.
        private const string XrHandsDep =
            "\"com.unity.xr.hands\": \"1.4.3\"";
        private const string XrInteractionDep =
            "\"com.unity.xr.interaction.toolkit\": \"2.6.5\"";
        private const string XrInteractionVersion = "2.6.5";
        private const string XriSamplesRoot =
            "Assets/Samples/XR Interaction Toolkit/" +
            XrInteractionVersion;
        private const string XrealLoader = "Unity.XR.XREAL.XREALXRLoader";
        private const string XrealSettingsType = "Unity.XR.XREAL.XREALSettings";
        private const string XrealSettingsKey = "com.unity.xr.management.xrealsettings";
        private const string XrealSettingsAssetPath = "Assets/XR/Settings/XREALSettings.asset";
        private const string TmpSettingsAssetPath =
            "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string TmpEssentialPackagePath =
            "Library/PackageCache/com.unity.ugui/Package Resources/" +
            "TMP Essential Resources.unitypackage";
        private const string NdkVersion = "23.1.7779620";
        private const string AndroidManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        internal const string XrealUrpAssetPath =
            "Assets/Settings/XREAL/MLOmegaXrealURP.asset";
        internal const string XrealVolumeProfilePath =
            "Assets/Settings/XREAL/MLOmegaXrealVolume.asset";
        private const string GraphicsSettingsAssetPath =
            "ProjectSettings/GraphicsSettings.asset";
        private static readonly string[] XrealRuntimeShaderAssetPaths =
        {
            "Packages/com.unity.render-pipelines.universal/Shaders/Unlit.shader",
            "Assets/Shaders/XrealRuntimeUnlit.shader",
            "Assets/Shaders/LiquidGlass.shader",
            "Assets/Shaders/GlassKawaseBlur.shader",
            "Assets/Shaders/XrealDepthOcclusion.shader",
            "Assets/Shaders/XrealFreeGuyMesh.shader",
            "Assets/Shaders/YUV420ToRGB.shader",
        };
        private static readonly string[] XrealRuntimeBuiltinShaderNames =
        {
            "Sprites/Default",
            "Unlit/Color",
            "Unlit/Texture",
            "Unlit/Transparent",
        };

        // Pass 1: ensure the SDK is referenced + the define is on, so the next compile
        // exercises the real XrealDeviceAdapter path. Safe to run repeatedly.
        [MenuItem("MLOmega/XREAL/1. Prepare (SDK + define)")]
        public static void PrepareDefines()
        {
            EnsureXrealPackage();
            // XREAL 3.1 implements its own planes/depth mesh/anchors through
            // AR Foundation. This package is injected only by the glasses build;
            // the committed manifest and PhoneOnly build remain dependency-free.
            EnsureArFoundationPackage();
            EnsurePackageDependency(XrHandsDep, "com.unity.xr.hands");
            EnsurePackageDependency(
                XrInteractionDep, "com.unity.xr.interaction.toolkit");
            SetDefine();
            AssetDatabase.Refresh();
            Debug.Log("[AndroidBuildXreal] Prepared: XREAL package referenced + XREAL_SDK_PRESENT set. " +
                      "Re-open/rebuild to compile the real adapter path.");
        }

        [MenuItem("MLOmega/XREAL/2. Build Glasses APK (G1)")]
        public static void BuildApk()
        {
            EnsureXrealPackage();
            EnsureArFoundationPackage();
            EnsurePackageDependency(XrHandsDep, "com.unity.xr.hands");
            EnsurePackageDependency(
                XrInteractionDep, "com.unity.xr.interaction.toolkit");
            SetDefine();
            ConfigureExternalTools();
            EnsureS24DisplayCompatibility();
            EnsureTmpEssentialResources();
            EnsureXrealRenderPipelineAssets();
            using (var xrealSettings = new XrealBuildSettingsScope())
            {
                ConfigurePlayerSettings();
                ConfigureXrealSdkSettings();
                EnableXrealLoader();
                ValidateArFoundationLoaded();
                EnsureScene();
                string buildScene = ScenePath;
                if (IsProviderGate())
                {
                    AugmentedRealityGateSceneBuilder.BuildXrealProviderGateScene();
                    buildScene = AugmentedRealityGateSceneBuilder.GateScenePath;
                }
                AndroidBuild.EmbedSmallDeviceModels();
                AndroidBuild.ApplyEndpointOverride(PhoneOnlySceneBuilder.XrealConfigPath);
                ValidateXrealBuildSettings();

                string defaultName = IsProviderGate()
                    ? "mlomega-xreal-provider-gate.apk"
                    : "mlomega-xreal.apk";
                string outPath = Env("MLOMEGA_APK_OUT",
                    Path.GetFullPath(Path.Combine("build", "android", defaultName)));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { buildScene },
                    locationPathName = outPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    throw new Exception(
                        $"[AndroidBuildXreal] Glasses APK build failed: {summary.result} " +
                        $"({summary.totalErrors} errors) -> {outPath}");
                }
                string profile = IsProviderGate()
                    ? "isolated AR provider gate"
                    : "Glasses PRODUCT";
                Debug.Log($"[AndroidBuildXreal] {profile} APK OK: {outPath} ({summary.totalSize} bytes)");
            }
        }

        [MenuItem("MLOmega/XREAL/3. Build World Atelier APK")]
        public static void BuildCreatorApk()
        {
            EnsureXrealPackage();
            EnsureArFoundationPackage();
            EnsurePackageDependency(XrHandsDep, "com.unity.xr.hands");
            EnsurePackageDependency(
                XrInteractionDep, "com.unity.xr.interaction.toolkit");
            SetDefine();
            ConfigureExternalTools();
            EnsureS24DisplayCompatibility();
            EnsureTmpEssentialResources();
            EnsureXrealRenderPipelineAssets();
            using (var xrealSettings = new XrealBuildSettingsScope(
                       useTemplateBuiltInPipeline: true))
            {
                ConfigurePlayerSettings();
                PlayerSettings.productName = "MLOmega World Atelier";
                PlayerSettings.SetApplicationIdentifier(
                    BuildTargetGroup.Android,
                    "com.mlomega.xr.worldatelier");
                ConfigureXrealSdkSettings();
                EnableXrealLoader();
                ValidateArFoundationLoaded();
                EnsureOfficialXriRigAssets();
                // Atelier hand pinch uses the same on-device MediaPipe model as
                // PhoneOnly, but its package has a separate app-private files dir.
                // Embed it here so first launch never depends on a download.
                AndroidBuild.EmbedSmallDeviceModels();
                WorldCreatorSceneBuilder.BuildScene();
                ValidateXrealBuildSettings(expectTemplateBuiltInPipeline: true);
                if (!File.Exists(WorldCreatorSceneBuilder.ScenePath))
                    throw new Exception(
                        "[AndroidBuildXreal] World Atelier scene missing.");

                string outPath = Env(
                    "MLOMEGA_CREATOR_APK_OUT",
                    Path.GetFullPath(Path.Combine(
                        "build",
                        "android",
                        "mlomega-xreal-world-atelier.apk")));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                BuildReport report = BuildPipeline.BuildPlayer(
                    new BuildPlayerOptions
                    {
                        scenes = new[] { WorldCreatorSceneBuilder.ScenePath },
                        locationPathName = outPath,
                        target = BuildTarget.Android,
                        targetGroup = BuildTargetGroup.Android,
                        options = BuildOptions.None,
                    });
                BuildSummary summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                    throw new Exception(
                        "[AndroidBuildXreal] World Atelier APK failed: " +
                        summary.result + " (" + summary.totalErrors +
                        " errors) -> " + outPath);
                Debug.Log(
                    "[AndroidBuildXreal] World Atelier APK OK: " +
                    outPath + " (" + summary.totalSize + " bytes)");
            }
        }

        /// <summary>
        /// Experimental browser/keyboard build. It has its own package, scene
        /// and artifact. The native WebView archive is enabled only during this
        /// build and restored to disabled afterwards, so Product and Atelier do
        /// not silently acquire a second Android rendering stack.
        /// </summary>
        [MenuItem("MLOmega/XREAL/4. Build Spatial Browser Lab APK")]
        public static void BuildCreatorLabApk()
        {
            const string webViewPlugin =
                "Assets/ThirdParty/TLabWebView/Plugins/Android/" +
                "libTLabWebView-release.aar";
            EnsureXrealPackage();
            EnsureArFoundationPackage();
            EnsurePackageDependency(XrHandsDep, "com.unity.xr.hands");
            EnsurePackageDependency(
                XrInteractionDep, "com.unity.xr.interaction.toolkit");
            SetDefine();
            ConfigureExternalTools();
            EnsureS24DisplayCompatibility();
            EnsureTmpEssentialResources();
            EnsureXrealRenderPipelineAssets();

            var importer = AssetImporter.GetAtPath(webViewPlugin) as PluginImporter;
            if (importer == null)
                throw new FileNotFoundException(
                    "XR Lab WebView plugin missing.", webViewPlugin);
            bool originalAndroid = importer.GetCompatibleWithPlatform(
                BuildTarget.Android);
            try
            {
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
                importer.SaveAndReimport();
                using (var xrealSettings = new XrealBuildSettingsScope(
                           useTemplateBuiltInPipeline: true))
                {
                    ConfigurePlayerSettings();
                    PlayerSettings.productName = Env(
                        "MLOMEGA_CREATOR_LAB_PRODUCT_NAME",
                        "MLOmega XR Browser Lab");
                    PlayerSettings.SetApplicationIdentifier(
                        BuildTargetGroup.Android,
                        Env(
                            "MLOMEGA_CREATOR_LAB_PACKAGE",
                            "com.mlomega.xr.worldatelierlab"));
                    ConfigureXrealSdkSettings();
                    EnableXrealLoader();
                    ValidateArFoundationLoaded();
                    EnsureOfficialXriRigAssets();
                    AndroidBuild.EmbedSmallDeviceModels();
                    WorldCreatorSceneBuilder.BuildLaboratoryScene();
                    ValidateXrealBuildSettings(
                        expectTemplateBuiltInPipeline: true);
                    if (!File.Exists(WorldCreatorSceneBuilder.LabScenePath))
                        throw new Exception(
                            "[AndroidBuildXreal] World Lab scene missing.");

                    string outPath = Env(
                        "MLOMEGA_CREATOR_LAB_APK_OUT",
                        Path.GetFullPath(Path.Combine(
                            "build",
                            "android",
                            "mlomega-xreal-world-lab.apk")));
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    BuildReport report = BuildPipeline.BuildPlayer(
                        new BuildPlayerOptions
                        {
                            scenes = new[] { WorldCreatorSceneBuilder.LabScenePath },
                            locationPathName = outPath,
                            target = BuildTarget.Android,
                            targetGroup = BuildTargetGroup.Android,
                            options = BuildOptions.None,
                        });
                    BuildSummary summary = report.summary;
                    if (summary.result != BuildResult.Succeeded)
                        throw new Exception(
                            "[AndroidBuildXreal] Spatial Browser Lab APK failed: " +
                            summary.result + " (" + summary.totalErrors +
                            " errors) -> " + outPath);
                    Debug.Log(
                        "[AndroidBuildXreal] Spatial Browser Lab APK OK: " +
                        outPath + " (" + summary.totalSize + " bytes)");
                }
            }
            finally
            {
                importer = AssetImporter.GetAtPath(webViewPlugin) as PluginImporter;
                if (importer != null)
                {
                    importer.SetCompatibleWithPlatform(
                        BuildTarget.Android,
                        originalAndroid);
                    importer.SaveAndReimport();
                }
            }
        }

        /// <summary>
        /// Standalone community OS build. This has its own package, scene and
        /// artifact and therefore cannot overwrite MLOmega Product or Atelier.
        /// Only the Eye/MediaPipe hand model is embedded; audio/KWS models are
        /// deliberately excluded.
        /// </summary>
        [MenuItem("XReel OS/Build Android APK")]
        public static void BuildXReelOsApk()
        {
            const string webViewPlugin =
                "Assets/ThirdParty/TLabWebView/Plugins/Android/" +
                "libTLabWebView-release.aar";
            EnsureXrealPackage();
            EnsureArFoundationPackage();
            EnsurePackageDependency(XrHandsDep, "com.unity.xr.hands");
            EnsurePackageDependency(
                XrInteractionDep, "com.unity.xr.interaction.toolkit");
            SetDefine();
            ConfigureExternalTools();
            EnsureS24DisplayCompatibility();
            EnsureTmpEssentialResources();
            EnsureXrealRenderPipelineAssets();

            var importer = AssetImporter.GetAtPath(webViewPlugin) as PluginImporter;
            if (importer == null)
                throw new FileNotFoundException(
                    "XReel OS WebView plugin missing.", webViewPlugin);
            bool originalAndroid = importer.GetCompatibleWithPlatform(
                BuildTarget.Android);
            try
            {
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
                importer.SaveAndReimport();
                using (var xrealSettings = new XrealBuildSettingsScope(
                           useTemplateBuiltInPipeline: true))
                {
                    ConfigurePlayerSettings();
                    PlayerSettings.productName = "XReel OS";
                    PlayerSettings.SetApplicationIdentifier(
                        BuildTargetGroup.Android,
                        "com.spendinfr.xreelos");
                    ConfigureXReelOsIcon();
                    ConfigureXrealSdkSettings();
                    EnableXrealLoader();
                    ValidateArFoundationLoaded();
                    EnsureOfficialXriRigAssets();
                    EmbedHandTrackingModelOnly();
                    WorldCreatorSceneBuilder.BuildCommunityOsScene();
                    ValidateXrealBuildSettings(
                        expectTemplateBuiltInPipeline: true);
                    if (!File.Exists(WorldCreatorSceneBuilder.OsScenePath))
                        throw new Exception("[XReel OS] scene missing.");

                    string outPath = Path.GetFullPath(Path.Combine(
                        "build", "android", "XReelOs.apk"));
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    // Unity's incremental Android writer can preserve a stale
                    // signing/padding tail when replacing an existing APK. The
                    // ZIP payload remains valid but the physical file becomes
                    // hundreds of MiB larger. A release artifact must start
                    // from a fresh file.
                    if (File.Exists(outPath)) File.Delete(outPath);
                    string idsig = outPath + ".idsig";
                    if (File.Exists(idsig)) File.Delete(idsig);
                    BuildReport report = BuildPipeline.BuildPlayer(
                        new BuildPlayerOptions
                        {
                            scenes = new[] { WorldCreatorSceneBuilder.OsScenePath },
                            locationPathName = outPath,
                            target = BuildTarget.Android,
                            targetGroup = BuildTargetGroup.Android,
                            options = BuildOptions.None,
                        });
                    BuildSummary summary = report.summary;
                    if (summary.result != BuildResult.Succeeded)
                        throw new Exception(
                            "[XReel OS] APK build failed: " + summary.result +
                            " (" + summary.totalErrors + " errors) -> " + outPath);
                    Debug.Log(
                        "[XReel OS] APK OK: " + outPath +
                        " (" + summary.totalSize + " bytes)");
                }
            }
            finally
            {
                importer = AssetImporter.GetAtPath(webViewPlugin) as PluginImporter;
                if (importer != null)
                {
                    importer.SetCompatibleWithPlatform(
                        BuildTarget.Android,
                        originalAndroid);
                    importer.SaveAndReimport();
                }
            }
        }

        private static void ConfigureXReelOsIcon()
        {
            AssetDatabase.ImportAsset(
                XReelIconPath,
                ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(XReelIconPath) as TextureImporter;
            if (importer == null)
                throw new FileNotFoundException(
                    "XReel OS launcher icon is missing.", XReelIconPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.SaveAndReimport();

            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                XReelIconPath);
            if (icon == null)
                throw new Exception("XReel OS launcher icon import failed.");
#pragma warning disable 618
            PlayerSettings.SetIconsForTargetGroup(
                BuildTargetGroup.Android,
                new[] { icon });
#pragma warning restore 618
        }

        private static void EmbedHandTrackingModelOnly()
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            string source = Path.Combine(
                projectRoot, "models", "hand_landmarker.task");
            string destinationDirectory = Path.Combine(
                Application.dataPath, "StreamingAssets", "models");
            string destination = Path.Combine(
                destinationDirectory, "hand_landmarker.task");
            if (!File.Exists(source))
                throw new FileNotFoundException(
                    "Download models/hand_landmarker.task before building XReel OS.",
                    source);
            Directory.CreateDirectory(destinationDirectory);
            File.Copy(source, destination, true);
            File.WriteAllText(
                Path.Combine(destinationDirectory, "index.txt"),
                "hand_landmarker.task");
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Builds an isolated protected-surface composition probe. It owns a
        /// unique package, scene and output file and therefore cannot overwrite
        /// Product, Atelier or Browser Lab.
        /// </summary>
        [MenuItem("MLOmega/XREAL/5. Build Secure Surface Spike APK")]
        public static void BuildSecureSurfaceSpikeApk()
        {
            EnsureXrealPackage();
            EnsureArFoundationPackage();
            EnsurePackageDependency(XrHandsDep, "com.unity.xr.hands");
            EnsurePackageDependency(
                XrInteractionDep, "com.unity.xr.interaction.toolkit");
            SetDefine();
            ConfigureExternalTools();
            EnsureS24DisplayCompatibility();
            EnsureTmpEssentialResources();
            EnsureXrealRenderPipelineAssets();

            using (var xrealSettings = new XrealBuildSettingsScope(
                       useTemplateBuiltInPipeline: true))
            {
                ConfigurePlayerSettings();
                PlayerSettings.productName = "MLOmega XREAL Secure Surface Spike";
                PlayerSettings.SetApplicationIdentifier(
                    BuildTargetGroup.Android,
                    "com.mlomega.xr.securesurfacespike");
                ConfigureXrealSdkSettings();
                EnableXrealLoader();
                ValidateArFoundationLoaded();
                EnsureOfficialXriRigAssets();
                AndroidBuild.EmbedSmallDeviceModels();
                WorldCreatorSceneBuilder.BuildSecureSurfaceSpikeScene();
                ValidateXrealBuildSettings(
                    expectTemplateBuiltInPipeline: true);
                if (!File.Exists(
                        WorldCreatorSceneBuilder.SecureSurfaceScenePath))
                    throw new Exception(
                        "[AndroidBuildXreal] Secure-surface scene missing.");

                string outPath = Path.GetFullPath(Path.Combine(
                    "build",
                    "android",
                    "mlomega-xreal-secure-surface-spike.apk"));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                BuildReport report = BuildPipeline.BuildPlayer(
                    new BuildPlayerOptions
                    {
                        scenes = new[]
                        {
                            WorldCreatorSceneBuilder.SecureSurfaceScenePath,
                        },
                        locationPathName = outPath,
                        target = BuildTarget.Android,
                        targetGroup = BuildTargetGroup.Android,
                        options = BuildOptions.None,
                    });
                BuildSummary summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                    throw new Exception(
                        "[AndroidBuildXreal] Secure Surface Spike failed: " +
                        summary.result + " (" + summary.totalErrors +
                        " errors) -> " + outPath);
                Debug.Log(
                    "[AndroidBuildXreal] Secure Surface Spike APK OK: " +
                    outPath + " (" + summary.totalSize + " bytes)");
            }
        }

        /// <summary>
        /// Hardware diagnostic built from XREAL SDK 3.1's unmodified HelloMR
        /// sample. It contains no MLOmega scene, renderer or interaction code,
        /// so it separates a host/SDK problem from a product regression.
        /// </summary>
        public static void BuildOfficialHelloMrDiagnosticApk()
        {
            const string scene =
                "Assets/Diagnostics/XREALOfficialHelloMR/HelloMR.unity";
            EnsureXrealPackage();
            EnsureArFoundationPackage();
            EnsurePackageDependency(XrHandsDep, "com.unity.xr.hands");
            EnsurePackageDependency(
                XrInteractionDep, "com.unity.xr.interaction.toolkit");
            SetDefine();
            ConfigureExternalTools();
            EnsureS24DisplayCompatibility();
            EnsureTmpEssentialResources();
            EnsureXrealRenderPipelineAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            using (var xrealSettings = new XrealBuildSettingsScope())
            {
                ConfigurePlayerSettings();
                PlayerSettings.productName = "XREAL Official HelloMR Diagnostic";
                PlayerSettings.SetApplicationIdentifier(
                    BuildTargetGroup.Android,
                    "com.mlomega.xr.officialdiagnostic");
                ConfigureXrealSdkSettings();
                EnableXrealLoader();
                ValidateArFoundationLoaded();
                ValidateXrealBuildSettings();
                if (!File.Exists(scene))
                    throw new Exception(
                        "[AndroidBuildXreal] Official HelloMR scene missing.");

                string outPath = Path.GetFullPath(Path.Combine(
                    "build",
                    "android",
                    "xreal-official-hellomr-diagnostic.apk"));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                BuildReport report = BuildPipeline.BuildPlayer(
                    new BuildPlayerOptions
                    {
                        scenes = new[] { scene },
                        locationPathName = outPath,
                        target = BuildTarget.Android,
                        targetGroup = BuildTargetGroup.Android,
                        options = BuildOptions.None,
                    });
                BuildSummary summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                    throw new Exception(
                        "[AndroidBuildXreal] Official HelloMR diagnostic failed: " +
                        summary.result + " (" + summary.totalErrors +
                        " errors) -> " + outPath);
                Debug.Log(
                    "[AndroidBuildXreal] Official HelloMR diagnostic APK OK: " +
                    outPath + " (" + summary.totalSize + " bytes)");
            }
        }

        /// <summary>
        /// Runtime-created XREAL canvases use TextMeshPro directly. Unity does
        /// not import TMP Essential Resources into a fresh project merely
        /// because com.unity.ugui is installed; without TMP Settings, the first
        /// label throws during Awake after the Atelier glass plate has already
        /// been created, leaving only a large purple/empty slab in the glasses.
        /// Keep this repair inside the XREAL build path so PhoneOnly remains
        /// unchanged.
        /// </summary>
        private static void EnsureTmpEssentialResources()
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    TmpSettingsAssetPath) != null)
                return;
            string packagePath = Path.GetFullPath(TmpEssentialPackagePath);
            if (!File.Exists(packagePath))
                throw new FileNotFoundException(
                    "[AndroidBuildXreal] TMP Essential Resources package missing.",
                    packagePath);
            AssetDatabase.ImportPackage(packagePath, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    TmpSettingsAssetPath) == null)
            {
                throw new Exception(
                    "[AndroidBuildXreal] TMP Essential Resources import did not " +
                    "produce TMP Settings; refusing an empty/purple XREAL UI.");
            }
            Debug.Log(
                "[AndroidBuildXreal] TMP Essential Resources imported and validated.");
        }

        /// <summary>
        /// The XREAL UI and hologram shaders are URP shaders. Merely installing
        /// the URP package does not activate the pipeline: Unity otherwise
        /// resolves those shaders but renders them magenta under Built-in.
        /// Create one mobile/XR URP profile and assign it only inside
        /// <see cref="XrealBuildSettingsScope"/>. PhoneOnly's pipeline remains
        /// exactly as it was after the build.
        /// </summary>
        private static void EnsureXrealRenderPipelineAssets()
        {
            Directory.CreateDirectory("Assets/Settings/XREAL");
            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                    XrealUrpAssetPath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create();
                pipeline.name = "MLOmega XREAL URP";
                AssetDatabase.CreateAsset(pipeline, XrealUrpAssetPath);
                if (
                    pipeline.rendererDataList.Length > 0 &&
                    pipeline.rendererDataList[0] != null &&
                    !AssetDatabase.Contains(pipeline.rendererDataList[0]))
                {
                    pipeline.rendererDataList[0].name =
                        "MLOmega XREAL Universal Renderer";
                    AssetDatabase.AddObjectToAsset(
                        pipeline.rendererDataList[0],
                        pipeline);
                }
            }

            pipeline.supportsHDR = true;
            // XREAL's optical eye surface is not multisampled. Requesting MSAA
            // makes URP resolve a non-AA render surface every frame.
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1f;
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.shadowDistance = 12f;
            var serializedPipeline = new SerializedObject(pipeline);
            SerializedProperty alpha =
                serializedPipeline.FindProperty(
                    "m_AllowPostProcessAlphaOutput");
            if (alpha == null)
                throw new MissingFieldException(
                    nameof(UniversalRenderPipelineAsset),
                    "m_AllowPostProcessAlphaOutput");
            alpha.boolValue = true;
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();

            VolumeProfile profile =
                AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                    XrealVolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "MLOmega XREAL Hologram Volume";
                AssetDatabase.CreateAsset(profile, XrealVolumeProfilePath);
            }
            if (!profile.TryGet(out Bloom bloom))
                bloom = profile.Add<Bloom>(true);
            bloom.active = true;
            bloom.threshold.Override(.72f);
            bloom.intensity.Override(.55f);
            bloom.scatter.Override(.62f);
            bloom.clamp.Override(12f);

            EditorUtility.SetDirty(pipeline);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[AndroidBuildXreal] XREAL-only URP + alpha-preserving bloom ready.");
        }

        internal static VolumeProfile LoadXrealVolumeProfile()
        {
            VolumeProfile profile =
                AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                    XrealVolumeProfilePath);
            if (profile == null)
                throw new FileNotFoundException(
                    "XREAL volume profile missing.",
                    XrealVolumeProfilePath);
            return profile;
        }

        // --- SDK package injection (keeps the committed manifest XREAL-free) -------
        private static void EnsureXrealPackage()
        {
            if (!File.Exists(TarballRel))
            {
                throw new Exception(
                    $"[AndroidBuildXreal] XREAL SDK tarball missing: {TarballRel}. " +
                    "Download SDK 3.1.0 from your XREAL developer account and place it there.");
            }
            string manifest = File.ReadAllText(ManifestPath);
            if (manifest.Contains("com.xreal.xr"))
            {
                return;
            }
            // Insert the dependency as the last entry of the "dependencies" object.
            int deps = manifest.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            int brace = manifest.IndexOf('{', deps);
            // Find the matching closing brace of the dependencies object.
            int depth = 0, close = -1;
            for (int i = brace; i < manifest.Length; i++)
            {
                if (manifest[i] == '{') depth++;
                else if (manifest[i] == '}') { depth--; if (depth == 0) { close = i; break; } }
            }
            if (close < 0) throw new Exception("[AndroidBuildXreal] manifest.json: dependencies block not found.");
            // last existing entry gets a trailing comma; insert before the close brace.
            string head = manifest.Substring(0, close).TrimEnd();
            string tail = manifest.Substring(close);
            string sep = head.EndsWith(",") ? "" : ",";
            manifest = head + sep + "\n    " + XrealDep + "\n  " + tail;
            File.WriteAllText(ManifestPath, manifest);
            Debug.Log("[AndroidBuildXreal] Injected com.xreal.xr into manifest.json (local build only).");
        }

        private static void EnsureArFoundationPackage()
            => EnsurePackageDependency(ArFoundationDep, "com.unity.xr.arfoundation");

        /// <summary>
        /// XREAL SDK 3.1's GlassesDisplayPlugEvent 2.4.2 only accepts an
        /// external display when Display.getName() contains "HDMI". Current
        /// Samsung firmware exposes the EDID name ("One Pro"), despite the
        /// native SDK and MCU initializing successfully. Patch only the local
        /// proprietary package cache so PhoneOnly remains untouched.
        /// </summary>
        private static void EnsureS24DisplayCompatibility()
        {
            string script = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "..",
                "scripts",
                "PATCH_XREAL_S24_DISPLAY.ps1"));
            if (!File.Exists(script))
                throw new Exception(
                    "[AndroidBuildXreal] S24 display compatibility script missing: " +
                    script);

            string project = Path.GetFullPath(".");
            string sdk = EditorPrefs.GetString(
                "AndroidSdkRoot",
                Environment.GetEnvironmentVariable("MLOMEGA_ANDROID_SDK") ??
                string.Empty);
            string jdk = EditorPrefs.GetString(
                "JdkPath",
                Environment.GetEnvironmentVariable("MLOMEGA_ANDROID_JDK") ??
                string.Empty);
            string Quote(string value) =>
                "\"" + value.Replace("\"", "\\\"") + "\"";
            string arguments =
                "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script) +
                " -ProjectPath " + Quote(project) +
                " -AndroidSdk " + Quote(sdk) +
                " -JavaHome " + Quote(jdk);

            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var process = System.Diagnostics.Process.Start(info))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new Exception(
                        "[AndroidBuildXreal] S24 display compatibility patch failed " +
                        $"(exit={process.ExitCode}).\n{stdout}\n{stderr}");
                Debug.Log(stdout.Trim());
            }
        }

        private static void EnsurePackageDependency(
            string dependency,
            string packageName)
        {
            string manifest = File.ReadAllText(ManifestPath);
            if (manifest.Contains("\"" + packageName + "\""))
            {
                string exactPattern =
                    "\"" + Regex.Escape(packageName) +
                    "\"\\s*:\\s*\"[^\"]+\"";
                string updated = Regex.Replace(
                    manifest,
                    exactPattern,
                    dependency,
                    RegexOptions.CultureInvariant);
                if (!string.Equals(updated, manifest, StringComparison.Ordinal))
                {
                    File.WriteAllText(ManifestPath, updated);
                    Debug.Log(
                        "[AndroidBuildXreal] Aligned package dependency: " +
                        dependency);
                }
                return;
            }
            int deps = manifest.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            int brace = manifest.IndexOf('{', deps);
            int depth = 0, close = -1;
            for (int i = brace; i < manifest.Length; i++)
            {
                if (manifest[i] == '{') depth++;
                else if (manifest[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        close = i;
                        break;
                    }
                }
            }
            if (close < 0)
                throw new Exception(
                    "[AndroidBuildXreal] manifest.json dependencies block not found.");
            string head = manifest.Substring(0, close).TrimEnd();
            string tail = manifest.Substring(close);
            string separator = head.EndsWith(",") ? string.Empty : ",";
            File.WriteAllText(
                ManifestPath,
                head + separator + "\n    " + dependency + "\n  " + tail);
            Debug.Log(
                "[AndroidBuildXreal] XREAL-only dependency injected: " +
                packageName);
        }

        private static void ValidateArFoundationLoaded()
        {
            bool arFoundation = false;
            bool xrHands = false;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetType(
                        "UnityEngine.XR.ARFoundation.ARSession",
                        false) != null)
                    arFoundation = true;
                if (assembly.GetType(
                        "UnityEngine.XR.Hands.XRHandSubsystem",
                        false) != null)
                    xrHands = true;
            }
            if (!arFoundation || !xrHands)
            {
                throw new Exception(
                    "[AndroidBuildXreal] XREAL spatial product dependencies are " +
                    $"not loaded (ARFoundation={arFoundation}, XRHands={xrHands}). " +
                    "Run PrepareDefines as a separate first pass.");
            }
        }

        /// <summary>
        /// XREAL's hardware-proven "XR Interaction Hands Setup" prefab is a
        /// wrapper around the XRI Starter Assets and Hands Interaction Demo.
        /// Import both official samples so every nested prefab reference is
        /// resolved exactly as in XREAL's HelloMR scene.
        /// </summary>
        private static void EnsureOfficialXriRigAssets()
        {
            string starter = Path.Combine(
                XriSamplesRoot,
                "Starter Assets").Replace('\\', '/');
            string hands = Path.Combine(
                XriSamplesRoot,
                "Hands Interaction Demo").Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(starter) &&
                AssetDatabase.IsValidFolder(hands))
                return;

            var samples = Sample.FindByPackage(
                "com.unity.xr.interaction.toolkit",
                XrInteractionVersion);
            bool importedStarter = AssetDatabase.IsValidFolder(starter);
            bool importedHands = AssetDatabase.IsValidFolder(hands);
            foreach (Sample sample in samples)
            {
                if (!importedStarter &&
                    string.Equals(
                        sample.displayName,
                        "Starter Assets",
                        StringComparison.Ordinal))
                {
                    importedStarter = sample.Import(
                        Sample.ImportOptions.OverridePreviousImports |
                        Sample.ImportOptions.HideImportWindow);
                }
                if (!importedHands &&
                    string.Equals(
                        sample.displayName,
                        "Hands Interaction Demo",
                        StringComparison.Ordinal))
                {
                    importedHands = sample.Import(
                        Sample.ImportOptions.OverridePreviousImports |
                        Sample.ImportOptions.HideImportWindow);
                }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!importedStarter || !importedHands ||
                !AssetDatabase.IsValidFolder(starter) ||
                !AssetDatabase.IsValidFolder(hands))
            {
                throw new Exception(
                    "[AndroidBuildXreal] Official XRI Starter/Hands samples " +
                    "could not be imported.");
            }
            Debug.Log(
                "[AndroidBuildXreal] Imported official XRI Starter Assets and " +
                "Hands Interaction Demo for the XREAL HelloMR rig.");
        }

        private static bool IsProviderGate() =>
            string.Equals(
                Environment.GetEnvironmentVariable("MLOMEGA_XREAL_PROVIDER_GATE"),
                "1",
                StringComparison.Ordinal);

        private static void SetDefine()
        {
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                string d = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
                foreach (string define in new[] { "XREAL_SDK_PRESENT", "XR_HANDS" })
                {
                    if (!d.Contains(define))
                        d = string.IsNullOrEmpty(d) ? define : d + ";" + define;
                }
                // The glasses build is NOT PhoneOnly — drop that define if present.
                d = d.Replace(";MLOMEGA_PHONE_ONLY", "").Replace("MLOMEGA_PHONE_ONLY;", "").Replace("MLOMEGA_PHONE_ONLY", "");
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, d);
            }
        }

        // --- XR Plug-in Management: enable the XREAL loader for Android ------------
        private static void EnableXrealLoader()
        {
            try
            {
                var settings = UnityEngine.XR.Management.XRGeneralSettings.Instance;
                var buildSettings = GetOrCreateAndroidBuildSettings();
                if (buildSettings == null)
                {
                    Debug.LogWarning("[AndroidBuildXreal] XR settings for Android not available; " +
                        "enable XREAL in Edit > Project Settings > XR Plug-in Management (Android) once, then rebuild.");
                    return;
                }
                var manager = buildSettings.Manager;
                bool ok = UnityEditor.XR.Management.Metadata.XRPackageMetadataStore.AssignLoader(
                    manager, XrealLoader, BuildTargetGroup.Android);
                Debug.Log(ok
                    ? "[AndroidBuildXreal] XREAL XR loader assigned for Android."
                    : "[AndroidBuildXreal] XREAL loader assignment returned false — enable it once via the XR Plug-in Management GUI.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AndroidBuildXreal] Could not enable the XREAL loader programmatically " +
                    $"({ex.Message}). Enable XREAL in Edit > Project Settings > XR Plug-in Management (Android) once, then rebuild.");
            }
        }

        /// <summary>
        /// XRPackageMetadataStore may create the XREAL settings asset without
        /// registering it as an EditorBuildSettings config object in batchmode.
        /// The SDK's own build processor then dereferences null but Unity still
        /// emits a superficially successful APK. Configure and register the exact
        /// SDK 3.1 settings explicitly so its official build/manifest callbacks run.
        /// Reflection keeps a clean PhoneOnly checkout compilable without the
        /// proprietary XREAL package installed.
        /// </summary>
        private static void ConfigureXrealSdkSettings()
        {
            Type settingsType = FindLoadedType(XrealSettingsType);
            if (settingsType == null || !typeof(ScriptableObject).IsAssignableFrom(settingsType))
            {
                throw new Exception(
                    $"[AndroidBuildXreal] SDK type '{XrealSettingsType}' is unavailable. " +
                    "Run PrepareDefines, let Unity import com.xreal.xr 3.1.0, then run the build pass.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(XrealSettingsAssetPath));
            var settings = AssetDatabase.LoadAssetAtPath(
                XrealSettingsAssetPath, settingsType) as ScriptableObject;
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance(settingsType);
                settings.name = "XREALSettings";
                AssetDatabase.CreateAsset(settings, XrealSettingsAssetPath);
            }

            string stereoRendering = Env(
                "MLOMEGA_XREAL_STEREO_RENDERING",
                "SinglePassInstanced");
            SetEnumField(settings, "StereoRendering", stereoRendering);
            string trackingType = Env("MLOMEGA_XREAL_TRACKING_TYPE", "MODE_6DOF");
            // XREAL's official HelloMR sample on One Pro + Eye/S24 initializes
            // the handset controller path.  The native Hands source currently
            // reports invalid hand payloads on this hardware and must not be
            // the production default.  Eye RGB/MediaPipe can still provide a
            // later hand-gesture path without changing the XR input source.
            string inputSource = Env("MLOMEGA_XREAL_INPUT_SOURCE", "Controller");
            SetEnumField(settings, "InitialTrackingType", trackingType);
            // Product spatial tools retain the phone controller/touch surface
            // fallback.  If XR Hands becomes available on a future supported
            // host it can still be explicitly selected by build environment.
            SetEnumField(settings, "InitialInputSource", inputSource);
            // The official XREAL template keeps this build capability enabled so
            // nractivitylife_6-release.aar contributes NRXRActivity. The temporary
            // XREAL manifest below also sets com.xreal.debug.noMultiResume=true:
            // this keeps the official bootstrap while deterministically avoiding
            // Samsung Android 16's rejected secondary-activity path.
            SetBoolField(settings, "SupportMultiResume", true);
            SetBoolField(settings, "EnableNativeSessionManager", false);
            SetBoolField(
                settings,
                "EnableAutoLogcat",
                !string.Equals(
                    Env("MLOMEGA_XREAL_AUTO_LOGCAT", "0"),
                    "0",
                    StringComparison.OrdinalIgnoreCase));
            SetEnumListField(
                settings,
                "SupportDevices",
                "XREAL_DEVICE_CATEGORY_REALITY",
                "XREAL_DEVICE_CATEGORY_VISION");
            AssignXrealVirtualController(settings);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            EditorBuildSettings.AddConfigObject(XrealSettingsKey, settings, true);
            if (!EditorBuildSettings.TryGetConfigObject(
                    XrealSettingsKey, out ScriptableObject registered) ||
                registered == null)
            {
                throw new Exception(
                    "[AndroidBuildXreal] XREALSettings registration did not persist.");
            }
            Debug.Log(
                "[AndroidBuildXreal] XREAL SDK settings registered: " +
                $"{stereoRendering}, {trackingType}, {inputSource}, " +
                "SupportMultiResume=true (official NRXRActivity bootstrap), " +
                $"AutoLogcat={GetFieldValue(settings, "EnableAutoLogcat")}.");
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static FieldInfo RequireField(ScriptableObject target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                throw new Exception(
                    $"[AndroidBuildXreal] XREAL SDK field missing: {fieldName}.");
            return field;
        }

        private static object GetFieldValue(ScriptableObject target, string fieldName) =>
            RequireField(target, fieldName).GetValue(target);

        private static void SetBoolField(
            ScriptableObject target,
            string fieldName,
            bool value)
        {
            FieldInfo field = RequireField(target, fieldName);
            if (field.FieldType != typeof(bool))
                throw new Exception(
                    $"[AndroidBuildXreal] XREAL field {fieldName} is not Boolean.");
            field.SetValue(target, value);
        }

        private static void SetEnumField(
            ScriptableObject target,
            string fieldName,
            string valueName)
        {
            FieldInfo field = RequireField(target, fieldName);
            if (!field.FieldType.IsEnum)
                throw new Exception(
                    $"[AndroidBuildXreal] XREAL field {fieldName} is not an enum.");
            field.SetValue(target, Enum.Parse(field.FieldType, valueName));
        }

        private static void SetEnumListField(
            ScriptableObject target,
            string fieldName,
            params string[] valueNames)
        {
            FieldInfo field = RequireField(target, fieldName);
            if (!(field.GetValue(target) is IList list) ||
                !field.FieldType.IsGenericType)
            {
                throw new Exception(
                    $"[AndroidBuildXreal] XREAL field {fieldName} is not an enum list.");
            }
            Type elementType = field.FieldType.GetGenericArguments()[0];
            if (!elementType.IsEnum)
                throw new Exception(
                    $"[AndroidBuildXreal] XREAL field {fieldName} element is not enum.");
            list.Clear();
            foreach (string valueName in valueNames)
                list.Add(Enum.Parse(elementType, valueName));
        }

        private static void AssignXrealVirtualController(ScriptableObject settings)
        {
            FieldInfo field = RequireField(settings, "VirtualController");
            if (field.GetValue(settings) != null) return;
            string[] guids = AssetDatabase.FindAssets(
                "XREALVirtualController t:Prefab",
                new[] { "Packages/com.xreal.xr" });
            if (guids.Length == 0)
                throw new Exception(
                    "[AndroidBuildXreal] XREALVirtualController prefab missing from SDK.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
            if (prefab == null)
                throw new Exception(
                    "[AndroidBuildXreal] XREALVirtualController prefab could not be loaded.");
            field.SetValue(settings, prefab);
        }

        private static UnityEngine.XR.Management.XRGeneralSettings GetOrCreateAndroidBuildSettings()
        {
            UnityEditor.EditorBuildSettings.TryGetConfigObject(
                UnityEngine.XR.Management.XRGeneralSettings.k_SettingsKey,
                out UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget perBuildTarget);
            if (perBuildTarget == null)
            {
                perBuildTarget = ScriptableObject.CreateInstance<UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget>();
                const string dir = "Assets/XR";
                Directory.CreateDirectory(dir);
                AssetDatabase.CreateAsset(perBuildTarget, dir + "/XRGeneralSettingsPerBuildTarget.asset");
                UnityEditor.EditorBuildSettings.AddConfigObject(
                    UnityEngine.XR.Management.XRGeneralSettings.k_SettingsKey, perBuildTarget, true);
            }
            if (!perBuildTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            }
            return perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        }

        // --- toolchain (mirrors AndroidBuild) -------------------------------------
        private static void ConfigureExternalTools()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string editorDirectory = Path.GetDirectoryName(
                EditorApplication.applicationPath);
            string androidPlayer = Path.Combine(
                editorDirectory,
                "Data",
                "PlaybackEngines",
                "AndroidPlayer");
            string installedSdk = Path.Combine(localAppData, "Android", "Sdk");
            string embeddedSdk = Path.Combine(androidPlayer, "SDK");
            string sdk = Env(
                "MLOMEGA_ANDROID_SDK",
                Directory.Exists(installedSdk) ? installedSdk : embeddedSdk);
            string installedNdk = Path.Combine(sdk, "ndk", NdkVersion);
            string embeddedNdk = Path.Combine(androidPlayer, "NDK");
            string ndk = Env(
                "MLOMEGA_ANDROID_NDK",
                Directory.Exists(installedNdk) ? installedNdk : embeddedNdk);
            const string installedJdk =
                @"C:\Program Files\Microsoft\jdk-17.0.19.10-hotspot";
            string embeddedJdk = Path.Combine(androidPlayer, "OpenJDK");
            string jdk = Env(
                "MLOMEGA_ANDROID_JDK",
                Directory.Exists(installedJdk) ? installedJdk : embeddedJdk);
            string gradle = Environment.GetEnvironmentVariable(
                "MLOMEGA_GRADLE_HOME") ?? string.Empty;
            bool useEmbeddedGradle =
                string.IsNullOrWhiteSpace(gradle) || !Directory.Exists(gradle);
            EditorPrefs.SetBool("SdkUseEmbedded", false);
            EditorPrefs.SetBool("NdkUseEmbedded", false);
            EditorPrefs.SetBool("JdkUseEmbedded", false);
            EditorPrefs.SetBool("GradleUseEmbedded", useEmbeddedGradle);
            EditorPrefs.SetString("AndroidSdkRoot", sdk);
            EditorPrefs.SetString("AndroidNdkRootR23", ndk);
            EditorPrefs.SetString("AndroidNdkRoot", ndk);
            EditorPrefs.SetString("JdkPath", jdk);
            if (!useEmbeddedGradle)
                EditorPrefs.SetString("GradlePath", gradle);
#if UNITY_2022_2_OR_NEWER
            AndroidExternalToolsSettings.sdkRootPath = sdk;
            AndroidExternalToolsSettings.ndkRootPath = ndk;
            AndroidExternalToolsSettings.jdkRootPath = jdk;
            if (!useEmbeddedGradle)
                AndroidExternalToolsSettings.gradlePath = gradle;
#endif
            Debug.Log(
                $"[AndroidBuildXreal] SDK={sdk} NDK={ndk} JDK={jdk} " +
                $"Gradle={(useEmbeddedGradle ? "Unity embedded" : gradle)}");
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            // Match the official XREAL SDK 3.1 template. On the S24/Android 16
            // test device this resolves to API 36; pinning API 34 leaves the
            // XREAL activity on the handset compatibility path.
            PlayerSettings.Android.targetSdkVersion =
                AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.runInBackground = true;
            PlayerSettings.productName = "MLOmega XREAL";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.mlomega.xr.glasses");
        }

        private static void ValidateXrealBuildSettings(
            bool expectTemplateBuiltInPipeline = false)
        {
            GraphicsDeviceType[] graphics =
                PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) ||
                graphics == null ||
                graphics.Length != 1 ||
                graphics[0] != GraphicsDeviceType.OpenGLES3)
            {
                throw new Exception(
                    "[AndroidBuildXreal] XREAL build requires OpenGLES3 only.");
            }
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation)
                throw new Exception(
                    "[AndroidBuildXreal] XREAL build requires the official " +
                    "AutoRotation orientation.");
            if (QualitySettings.vSyncCount != 0)
                throw new Exception(
                    "[AndroidBuildXreal] XREAL build requires VSync Don't Sync.");
            if (QualitySettings.antiAliasing != 0)
                throw new Exception(
                    "[AndroidBuildXreal] XREAL optical surface requires MSAA disabled.");
            UniversalRenderPipelineAsset expectedPipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                    XrealUrpAssetPath);
            bool pipelineValid = expectTemplateBuiltInPipeline
                ? GraphicsSettings.defaultRenderPipeline == null &&
                  QualitySettings.renderPipeline == null
                : expectedPipeline != null &&
                  GraphicsSettings.defaultRenderPipeline == expectedPipeline &&
                  QualitySettings.renderPipeline == expectedPipeline &&
                  expectedPipeline.allowPostProcessAlphaOutput;
            if (!pipelineValid)
            {
                throw new Exception(
                    expectTemplateBuiltInPipeline
                        ? "[AndroidBuildXreal] World Atelier must match the " +
                          "official XREAL template's Built-in render pipeline."
                        : "[AndroidBuildXreal] XREAL build requires the dedicated " +
                          "alpha-preserving URP asset in Graphics and Quality.");
            }
            if (!File.ReadAllText(AndroidManifestPath)
                    .Contains("android:screenOrientation=\"fullSensor\""))
            {
                throw new Exception(
                    "[AndroidBuildXreal] XREAL manifest orientation was not " +
                    "isolated to the official full-sensor policy.");
            }
            string xrealManifest = File.ReadAllText(AndroidManifestPath);
            if (!xrealManifest.Contains(
                    "com.xreal.debug.noMultiResume",
                    StringComparison.Ordinal))
            {
                throw new Exception(
                    "[AndroidBuildXreal] Deterministic XREAL noMultiResume metadata missing.");
            }
            if (xrealManifest
                    .Contains("com.mlomega.xrg1gate.EyeCaptureService"))
            {
                throw new Exception(
                    "[AndroidBuildXreal] Stale EyeCaptureService leaked into XREAL manifest.");
            }
            if (AssetDatabase.LoadAssetAtPath<Shader>(
                    PhoneOnlySceneBuilder.XrealYuvShaderPath) == null)
            {
                throw new Exception("[AndroidBuildXreal] XREAL YUV shader asset missing.");
            }
            if (AssetDatabase.LoadAssetAtPath<Shader>(
                    PhoneOnlySceneBuilder.XrealDepthOcclusionShaderPath) == null ||
                AssetDatabase.LoadAssetAtPath<Shader>(
                    PhoneOnlySceneBuilder.XrealFreeGuyMeshShaderPath) == null)
            {
                throw new Exception(
                    "[AndroidBuildXreal] XREAL depth occlusion/FreeGuy shader " +
                    "assets are missing.");
            }
            ValidateXrealRuntimeShadersIncluded();
            if (!EditorBuildSettings.TryGetConfigObject(
                    XrealSettingsKey, out ScriptableObject xrealSettings) ||
                xrealSettings == null)
            {
                throw new Exception(
                    "[AndroidBuildXreal] XREALSettings is not registered; " +
                    "the SDK manifest/build callbacks would silently fail.");
            }
            if (!string.Equals(
                    GetFieldValue(xrealSettings, "StereoRendering").ToString(),
                    Env(
                        "MLOMEGA_XREAL_STEREO_RENDERING",
                        "SinglePassInstanced"),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    GetFieldValue(xrealSettings, "InitialTrackingType").ToString(),
                    Env("MLOMEGA_XREAL_TRACKING_TYPE", "MODE_6DOF"),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    GetFieldValue(xrealSettings, "InitialInputSource").ToString(),
                    Env("MLOMEGA_XREAL_INPUT_SOURCE", "Controller"),
                    StringComparison.Ordinal) ||
                !Equals(GetFieldValue(xrealSettings, "SupportMultiResume"), true))
            {
                throw new Exception(
                    "[AndroidBuildXreal] XREAL settings do not match the requested " +
                    "official stereo + tracking + input profile " +
                    "(SupportMultiResume=true for the official NRXRActivity bootstrap).");
            }
        }

        private static Shader[] ResolveXrealRuntimeShaders()
        {
            var shaders = new List<Shader>();
            foreach (string path in XrealRuntimeShaderAssetPaths)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                    throw new FileNotFoundException(
                        $"[AndroidBuildXreal] Required runtime shader asset missing: {path}",
                        path);
                if (!shaders.Contains(shader))
                    shaders.Add(shader);
            }
            foreach (string name in XrealRuntimeBuiltinShaderNames)
            {
                Shader shader = Shader.Find(name);
                if (shader == null)
                    throw new Exception(
                        $"[AndroidBuildXreal] Required built-in runtime shader missing: {name}");
                if (!shaders.Contains(shader))
                    shaders.Add(shader);
            }
            return shaders.ToArray();
        }

        private static void InstallXrealRuntimeShaders()
        {
            var merged = new List<Shader>();
            Shader[] current = GetAlwaysIncludedShaders();
            if (current != null)
            {
                foreach (Shader shader in current)
                {
                    if (shader != null && !merged.Contains(shader))
                        merged.Add(shader);
                }
            }
            foreach (Shader shader in ResolveXrealRuntimeShaders())
            {
                if (!merged.Contains(shader))
                    merged.Add(shader);
            }
            SetAlwaysIncludedShaders(merged.ToArray());
            Debug.Log(
                $"[AndroidBuildXreal] Forced {ResolveXrealRuntimeShaders().Length} " +
                "runtime shaders into the XREAL player.");
        }

        private static void ValidateXrealRuntimeShadersIncluded()
        {
            Shader[] included = GetAlwaysIncludedShaders();
            foreach (Shader required in ResolveXrealRuntimeShaders())
            {
                if (Array.IndexOf(included, required) < 0)
                    throw new Exception(
                        $"[AndroidBuildXreal] Runtime shader was not forced into " +
                        $"the XREAL player: {required.name}");
            }
        }

        private static Shader[] GetAlwaysIncludedShaders()
        {
            UnityEngine.Object[] settingsAssets =
                AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsAssetPath);
            if (settingsAssets == null || settingsAssets.Length == 0)
                throw new FileNotFoundException(
                    "[AndroidBuildXreal] GraphicsSettings asset is unavailable.",
                    GraphicsSettingsAssetPath);
            var serialized = new SerializedObject(settingsAssets[0]);
            SerializedProperty included =
                serialized.FindProperty("m_AlwaysIncludedShaders");
            if (included == null || !included.isArray)
                throw new MissingFieldException(
                    "GraphicsSettings", "m_AlwaysIncludedShaders");
            var shaders = new List<Shader>(included.arraySize);
            for (int i = 0; i < included.arraySize; i++)
            {
                Shader shader = included.GetArrayElementAtIndex(i)
                    .objectReferenceValue as Shader;
                if (shader != null)
                    shaders.Add(shader);
            }
            return shaders.ToArray();
        }

        private static void SetAlwaysIncludedShaders(Shader[] shaders)
        {
            UnityEngine.Object[] settingsAssets =
                AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsAssetPath);
            if (settingsAssets == null || settingsAssets.Length == 0)
                throw new FileNotFoundException(
                    "[AndroidBuildXreal] GraphicsSettings asset is unavailable.",
                    GraphicsSettingsAssetPath);
            var serialized = new SerializedObject(settingsAssets[0]);
            SerializedProperty included =
                serialized.FindProperty("m_AlwaysIncludedShaders");
            if (included == null || !included.isArray)
                throw new MissingFieldException(
                    "GraphicsSettings", "m_AlwaysIncludedShaders");
            Shader[] safe = shaders ?? Array.Empty<Shader>();
            included.arraySize = safe.Length;
            for (int i = 0; i < safe.Length; i++)
            {
                included.GetArrayElementAtIndex(i).objectReferenceValue =
                    safe[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureScene()
        {
            PhoneOnlySceneBuilder.BuildXrealScene();
            if (!File.Exists(ScenePath))
                throw new Exception($"[AndroidBuildXreal] XREAL product scene missing after build: {ScenePath}");
        }

        /// <summary>
        /// Applies XREAL's documented Android graphics/orientation/VSync settings
        /// only while the glasses player is built. Dispose restores the exact
        /// PhoneOnly project state, including on build failure.
        /// </summary>
        private sealed class XrealBuildSettingsScope : IDisposable
        {
            private readonly bool _automaticGraphics;
            private readonly GraphicsDeviceType[] _graphics;
            private readonly UIOrientation _orientation;
            private readonly bool _autorotatePortrait;
            private readonly bool _autorotatePortraitUpsideDown;
            private readonly bool _autorotateLandscapeLeft;
            private readonly bool _autorotateLandscapeRight;
            private readonly string _productName;
            private readonly string _applicationIdentifier;
            private readonly ScriptingImplementation _scriptingBackend;
            private readonly AndroidArchitecture _targetArchitectures;
            private readonly AndroidSdkVersions _minSdkVersion;
            private readonly AndroidSdkVersions _targetSdkVersion;
            private readonly bool _runInBackground;
            private readonly int _activeQuality;
            private readonly int[] _vSync;
            private readonly int[] _antiAliasing;
            private readonly RenderPipelineAsset _graphicsPipeline;
            private readonly RenderPipelineAsset[] _qualityPipelines;
            private readonly RenderPipelineGlobalSettings _urpGlobalSettings;
            private readonly Shader[] _alwaysIncludedShaders;
            private readonly string _manifest;
            private readonly bool _hadXrealSettingsConfig;
            private readonly ScriptableObject _previousXrealSettingsConfig;
            private bool _disposed;

            public XrealBuildSettingsScope(
                bool useTemplateBuiltInPipeline = false)
            {
                _automaticGraphics =
                    PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android);
                _graphics = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
                _orientation = PlayerSettings.defaultInterfaceOrientation;
                _autorotatePortrait =
                    PlayerSettings.allowedAutorotateToPortrait;
                _autorotatePortraitUpsideDown =
                    PlayerSettings.allowedAutorotateToPortraitUpsideDown;
                _autorotateLandscapeLeft =
                    PlayerSettings.allowedAutorotateToLandscapeLeft;
                _autorotateLandscapeRight =
                    PlayerSettings.allowedAutorotateToLandscapeRight;
                _productName = PlayerSettings.productName;
                _applicationIdentifier =
                    PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
                _scriptingBackend =
                    PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android);
                _targetArchitectures = PlayerSettings.Android.targetArchitectures;
                _minSdkVersion = PlayerSettings.Android.minSdkVersion;
                _targetSdkVersion = PlayerSettings.Android.targetSdkVersion;
                _runInBackground = PlayerSettings.runInBackground;
                _hadXrealSettingsConfig =
                    EditorBuildSettings.TryGetConfigObject(
                        XrealSettingsKey,
                        out _previousXrealSettingsConfig);
                _activeQuality = QualitySettings.GetQualityLevel();
                _vSync = new int[QualitySettings.names.Length];
                _antiAliasing = new int[QualitySettings.names.Length];
                _qualityPipelines =
                    new RenderPipelineAsset[QualitySettings.names.Length];
                _graphicsPipeline = GraphicsSettings.defaultRenderPipeline;
                _urpGlobalSettings =
                    EditorGraphicsSettings
                        .GetRenderPipelineGlobalSettingsAsset<
                            UniversalRenderPipeline>();
                _alwaysIncludedShaders = GetAlwaysIncludedShaders();
                UniversalRenderPipelineAsset xrealPipeline = null;
                if (!useTemplateBuiltInPipeline)
                {
                    xrealPipeline =
                        AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                            XrealUrpAssetPath);
                    if (xrealPipeline == null)
                        throw new FileNotFoundException(
                            "XREAL URP asset missing.",
                            XrealUrpAssetPath);
                }
                // The official XREAL 3.1 template which is proven on the One
                // Pro uses Unity's Built-in renderer. Keep that exact contract
                // for the isolated Atelier; the main glasses build remains URP.
                GraphicsSettings.defaultRenderPipeline = xrealPipeline;
                if (useTemplateBuiltInPipeline)
                {
                    // The official XREAL template has no URP global-settings
                    // registration. Leaving ours registered made Unity enumerate
                    // billions of Lit/SimpleLit variants even though Built-in
                    // stripped every one of them.
                    EditorGraphicsSettings
                        .SetRenderPipelineGlobalSettingsAsset<
                            UniversalRenderPipeline>(null);
                }
                InstallXrealRuntimeShaders();
                for (int i = 0; i < _vSync.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    _vSync[i] = QualitySettings.vSyncCount;
                    _antiAliasing[i] = QualitySettings.antiAliasing;
                    _qualityPipelines[i] = QualitySettings.renderPipeline;
                    QualitySettings.vSyncCount = 0;
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.renderPipeline = xrealPipeline;
                }
                QualitySettings.SetQualityLevel(_activeQuality, false);

                _manifest = File.ReadAllText(AndroidManifestPath);
                string xrealManifest = _manifest.Replace(
                    "android:screenOrientation=\"landscape\"",
                    "android:screenOrientation=\"fullSensor\"");
                if (xrealManifest == _manifest &&
                    !_manifest.Contains(
                        "android:screenOrientation=\"fullSensor\"",
                        StringComparison.Ordinal))
                    throw new Exception(
                        "[AndroidBuildXreal] Expected landscape orientation marker missing.");
                const string applicationOpen =
                    "<application android:allowBackup=\"false\" android:usesCleartextTraffic=\"true\">";
                const string noMultiResumeMeta =
                    "        <meta-data android:name=\"com.xreal.debug.noMultiResume\" " +
                    "android:value=\"true\" />";
                if (!xrealManifest.Contains(applicationOpen, StringComparison.Ordinal))
                    throw new Exception(
                        "[AndroidBuildXreal] Expected application marker missing.");
                if (!xrealManifest.Contains(
                        "com.xreal.debug.noMultiResume",
                        StringComparison.Ordinal))
                {
                    xrealManifest = xrealManifest.Replace(
                        applicationOpen,
                        applicationOpen + Environment.NewLine + noMultiResumeMeta);
                }
                xrealManifest = Regex.Replace(
                    xrealManifest,
                    @"\s*<!-- Foreground service used by the Eye capture path \(media projection class\)\. -->\s*" +
                    @"<service\s+android:name=""com\.mlomega\.xrg1gate\.EyeCaptureService""[\s\S]*?/>\s*",
                    Environment.NewLine,
                    RegexOptions.CultureInvariant);
                const string networkPermission =
                    "<uses-permission android:name=\"android.permission.ACCESS_NETWORK_STATE\" />";
                if (!xrealManifest.Contains(
                        "android.permission.ACCESS_WIFI_STATE",
                        StringComparison.Ordinal))
                {
                    xrealManifest = xrealManifest.Replace(
                        networkPermission,
                        networkPermission + Environment.NewLine +
                        "    <uses-permission android:name=\"android.permission.ACCESS_WIFI_STATE\" />" +
                        Environment.NewLine +
                        "    <uses-permission android:name=\"android.permission.ACCESS_FINE_LOCATION\" />" +
                        Environment.NewLine +
                        "    <uses-permission android:name=\"android.permission.NEARBY_WIFI_DEVICES\" " +
                        "android:usesPermissionFlags=\"neverForLocation\" />");
                }
                File.WriteAllText(AndroidManifestPath, xrealManifest);

                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(
                    BuildTarget.Android,
                    new[] { GraphicsDeviceType.OpenGLES3 });
                // XREAL's SDK 3.1 reference project uses AutoRotation with all
                // four autorotation directions enabled. Forcing Portrait made
                // Unity allocate a 1080x2340 surface while the XREAL
                // presentation was 1600x900, squeezing the stereo texture into
                // the lower-left of Samsung's external display.
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
                PlayerSettings.allowedAutorotateToPortrait = true;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
                PlayerSettings.allowedAutorotateToLandscapeLeft = true;
                PlayerSettings.allowedAutorotateToLandscapeRight = true;
                AssetDatabase.SaveAssets();
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    PlayerSettings.SetUseDefaultGraphicsAPIs(
                        BuildTarget.Android, _automaticGraphics);
                    if (_graphics != null && _graphics.Length > 0)
                        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, _graphics);
                    PlayerSettings.defaultInterfaceOrientation = _orientation;
                    PlayerSettings.allowedAutorotateToPortrait =
                        _autorotatePortrait;
                    PlayerSettings.allowedAutorotateToPortraitUpsideDown =
                        _autorotatePortraitUpsideDown;
                    PlayerSettings.allowedAutorotateToLandscapeLeft =
                        _autorotateLandscapeLeft;
                    PlayerSettings.allowedAutorotateToLandscapeRight =
                        _autorotateLandscapeRight;
                    PlayerSettings.productName = _productName;
                    PlayerSettings.SetApplicationIdentifier(
                        BuildTargetGroup.Android, _applicationIdentifier);
                    PlayerSettings.SetScriptingBackend(
                        BuildTargetGroup.Android, _scriptingBackend);
                    PlayerSettings.Android.targetArchitectures = _targetArchitectures;
                    PlayerSettings.Android.minSdkVersion = _minSdkVersion;
                    PlayerSettings.Android.targetSdkVersion = _targetSdkVersion;
                    PlayerSettings.runInBackground = _runInBackground;
                    GraphicsSettings.defaultRenderPipeline =
                        _graphicsPipeline;
                    EditorGraphicsSettings
                        .SetRenderPipelineGlobalSettingsAsset<
                            UniversalRenderPipeline>(_urpGlobalSettings);
                    SetAlwaysIncludedShaders(_alwaysIncludedShaders);
                    if (_hadXrealSettingsConfig && _previousXrealSettingsConfig != null)
                    {
                        EditorBuildSettings.AddConfigObject(
                            XrealSettingsKey, _previousXrealSettingsConfig, true);
                    }
                    else
                    {
                        EditorBuildSettings.RemoveConfigObject(XrealSettingsKey);
                    }
                    for (int i = 0; i < _vSync.Length; i++)
                    {
                        QualitySettings.SetQualityLevel(i, false);
                        QualitySettings.vSyncCount = _vSync[i];
                        QualitySettings.antiAliasing = _antiAliasing[i];
                        QualitySettings.renderPipeline =
                            _qualityPipelines[i];
                    }
                    QualitySettings.SetQualityLevel(_activeQuality, false);
                    File.WriteAllText(AndroidManifestPath, _manifest);
                    AssetDatabase.SaveAssets();
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[AndroidBuildXreal] Failed to restore PhoneOnly settings: {ex}");
                    throw;
                }
            }
        }

        private static string Env(string key, string fallback)
        {
            string v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }
    }

    /// <summary>
    /// Android 11+ hides installed application metadata unless a package is in
    /// the manifest queries list. Inject the narrow list only into the generated
    /// Lab Gradle project: Product and validated Atelier manifests remain byte-for-
    /// byte unchanged.
    /// </summary>
    internal sealed class XrLabManifestPostprocessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string identifier = PlayerSettings.GetApplicationIdentifier(
                BuildTargetGroup.Android);
            if (string.Equals(
                    identifier,
                    "com.mlomega.xr.securesurfacespike",
                    StringComparison.Ordinal))
            {
                InjectSecureSurfaceWidevineProbe(path);
                return;
            }
            bool isIsolatedLab = identifier.StartsWith(
                "com.mlomega.xr.worldatelierlab",
                StringComparison.Ordinal);
            bool isXReelOs = string.Equals(
                identifier,
                "com.spendinfr.xreelos",
                StringComparison.Ordinal);
            if (!isIsolatedLab && !isXReelOs)
                return;

            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest))
            {
                string[] candidates = Directory.GetFiles(
                    path,
                    "AndroidManifest.xml",
                    SearchOption.AllDirectories);
                if (candidates.Length == 0)
                    throw new FileNotFoundException(
                        "Generated XR Lab AndroidManifest.xml missing.", path);
                Array.Sort(candidates, (left, right) =>
                    left.Length.CompareTo(right.Length));
                manifest = candidates[0];
            }

            string xml = File.ReadAllText(manifest);
            int application = xml.IndexOf("<application", StringComparison.Ordinal);
            if (application < 0)
                throw new InvalidDataException(
                    "Generated XR Lab manifest has no application element: " + manifest);
            const string queries =
                "    <queries>\n" +
                "        <package android:name=\"com.android.settings\" />\n" +
                "        <package android:name=\"com.android.chrome\" />\n" +
                "        <package android:name=\"com.google.android.googlequicksearchbox\" />\n" +
                "        <package android:name=\"com.google.android.youtube\" />\n" +
                "        <package android:name=\"com.netflix.mediaclient\" />\n" +
                "        <package android:name=\"com.spotify.music\" />\n" +
                "        <package android:name=\"com.reddit.frontpage\" />\n" +
                "        <package android:name=\"com.amazon.avod.thirdpartyclient\" />\n" +
                "        <package android:name=\"com.limelight\" />\n" +
                "        <package android:name=\"moe.shizuku.privileged.api\" />\n" +
                "        <package android:name=\"com.google.android.inputmethod.latin\" />\n" +
                "        <package android:name=\"com.samsung.android.honeyboard\" />\n" +
                "        <intent>\n" +
                "            <action android:name=\"android.speech.RecognitionService\" />\n" +
                "        </intent>\n" +
                "    </queries>\n";
            File.WriteAllText(manifest, xml.Insert(application, queries));
            Debug.Log("[AndroidBuildXreal] XR Lab package/icon queries injected: " + manifest);
            // The Lab reuses the hardware-validated v34 cinema transport. The
            // postprocessor remains package-gated, so Product/Atelier/PhoneOnly
            // never acquire Shizuku, Media3 or protected-display code.
            InjectSecureSurfaceWidevineProbe(path);
        }

        private static void InjectSecureSurfaceWidevineProbe(string unityLibraryPath)
        {
            string applicationIdentifier =
                PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
            // Every isolated Lab version uses the same WebVR/Media3 bridge.
            // Restricting injection to the historical v15 package left v14
            // builds compiling against a stale Java file in Library/Bee: Unity
            // rendered VR, but new playback controls failed at runtime with
            // NoSuchMethod/AndroidJavaException.
            bool isolatedWebVr = applicationIdentifier.StartsWith(
                "com.mlomega.xr.worldatelierlabv",
                StringComparison.Ordinal) ||
                string.Equals(
                    applicationIdentifier,
                    "com.mlomega.xr.worldatelierlab",
                    StringComparison.Ordinal) ||
                string.Equals(
                    applicationIdentifier,
                    "com.spendinfr.xreelos",
                    StringComparison.Ordinal);
            string template = Path.Combine(
                Application.dataPath,
                "Scripts",
                "Editor",
                "SecureSurfaceSpike",
                "SecureWidevinePlayer.java.txt");
            if (!File.Exists(template))
                throw new FileNotFoundException(
                    "Secure Widevine Java bridge template missing.", template);

            string java = Path.Combine(
                unityLibraryPath,
                "src",
                "main",
                "java",
                "com",
                "mlomega",
                "xr",
                "securesurface",
                "SecureWidevinePlayer.java");
            Directory.CreateDirectory(Path.GetDirectoryName(java));
            File.Copy(template, java, true);

            string webVrJava = null;
            if (isolatedWebVr)
            {
                string webVrTemplate = Path.Combine(
                    Application.dataPath,
                    "Scripts",
                    "Editor",
                    "XrWebVr",
                    "XrWebVrBridge.java.txt");
                if (!File.Exists(webVrTemplate))
                    throw new FileNotFoundException(
                        "Isolated Web VR Java bridge template missing.",
                        webVrTemplate);
                webVrJava = Path.Combine(
                    unityLibraryPath,
                    "src",
                    "main",
                    "java",
                    "com",
                    "mlomega",
                    "xr",
                    "webvr",
                    "XrWebVrBridge.java");
                Directory.CreateDirectory(Path.GetDirectoryName(webVrJava));
                File.Copy(webVrTemplate, webVrJava, true);
            }

            string trustedServiceTemplate = Path.Combine(
                Application.dataPath,
                "Scripts",
                "Editor",
                "SecureSurfaceSpike",
                "TrustedDisplayUserService.java.txt");
            if (!File.Exists(trustedServiceTemplate))
                throw new FileNotFoundException(
                    "Trusted-display Shizuku UserService template missing.",
                    trustedServiceTemplate);
            string trustedServiceJava = Path.Combine(
                Path.GetDirectoryName(java),
                "TrustedDisplayUserService.java");
            File.Copy(trustedServiceTemplate, trustedServiceJava, true);

            for (int slot = 1; slot <= 3; slot++)
            {
                string slotFile = "TrustedDisplayUserServiceSlot" + slot + ".java";
                string slotTemplate = Path.Combine(
                    Application.dataPath,
                    "Scripts",
                    "Editor",
                    "SecureSurfaceSpike",
                    slotFile + ".txt");
                if (!File.Exists(slotTemplate))
                    throw new FileNotFoundException(
                        "Dedicated multi-app UserService template missing.",
                        slotTemplate);
                File.Copy(
                    slotTemplate,
                    Path.Combine(Path.GetDirectoryName(java), slotFile),
                    true);
            }

            string multiAppTemplate = Path.Combine(
                Application.dataPath,
                "Scripts",
                "Editor",
                "SecureSurfaceSpike",
                "MultiAppDisplayBridge.java.txt");
            if (!File.Exists(multiAppTemplate))
                throw new FileNotFoundException(
                    "Multi-app display bridge template missing.",
                    multiAppTemplate);
            string multiAppJava = Path.Combine(
                Path.GetDirectoryName(java),
                "MultiAppDisplayBridge.java");
            File.Copy(multiAppTemplate, multiAppJava, true);

            string taskProbeTemplate = Path.Combine(
                Application.dataPath,
                "Scripts",
                "Editor",
                "SecureSurfaceSpike",
                "SecureTaskSurfaceProbe.java.txt");
            if (!File.Exists(taskProbeTemplate))
                throw new FileNotFoundException(
                    "Secure task-surface probe template missing.",
                    taskProbeTemplate);
            string taskProbeJava = Path.Combine(
                Path.GetDirectoryName(java),
                "SecureTaskSurfaceProbe.java");
            File.Copy(taskProbeTemplate, taskProbeJava, true);

            string physicalStereoProbeTemplate = Path.Combine(
                Application.dataPath,
                "Scripts",
                "Editor",
                "SecureSurfaceSpike",
                "SecurePhysicalStereoProbe.java.txt");
            if (!File.Exists(physicalStereoProbeTemplate))
                throw new FileNotFoundException(
                    "Secure physical stereo probe template missing.",
                    physicalStereoProbeTemplate);
            string physicalStereoProbeJava = Path.Combine(
                Path.GetDirectoryName(java),
                "SecurePhysicalStereoProbe.java");
            File.Copy(physicalStereoProbeTemplate, physicalStereoProbeJava, true);

            string nativeProbe = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "..",
                "scripts",
                "xreal-compat",
                "native",
                "arm64-v8a",
                "libmlomega_secure_task_probe.so"));
            if (!File.Exists(nativeProbe))
                throw new FileNotFoundException(
                    "Native task-surface probe missing. Run " +
                    "scripts\\BUILD_XREAL_SECURE_TASK_PROBE.ps1 first.",
                    nativeProbe);
            string nativeProbeDestination = Path.Combine(
                unityLibraryPath,
                "src",
                "main",
                "jniLibs",
                "arm64-v8a",
                Path.GetFileName(nativeProbe));
            Directory.CreateDirectory(Path.GetDirectoryName(nativeProbeDestination));
            File.Copy(nativeProbe, nativeProbeDestination, true);

            string taskOrganizerStubs = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "..",
                "scripts",
                "xreal-compat",
                "taskorganizer-stubs.jar"));
            if (!File.Exists(taskOrganizerStubs))
                throw new FileNotFoundException(
                    "Compile-only TaskOrganizer stubs missing. Run " +
                    "scripts\\BUILD_XREAL_TASKORGANIZER_STUBS.ps1 first.",
                    taskOrganizerStubs);
            string compileStubs = Path.Combine(
                unityLibraryPath,
                "compile-stubs",
                Path.GetFileName(taskOrganizerStubs));
            Directory.CreateDirectory(Path.GetDirectoryName(compileStubs));
            File.Copy(taskOrganizerStubs, compileStubs, true);

            string manifest = Path.Combine(
                unityLibraryPath,
                "src",
                "main",
                "AndroidManifest.xml");
            if (File.Exists(manifest))
            {
                string xml = File.ReadAllText(manifest);
                const string youtubePackage =
                    "        <package android:name=\"com.google.android.youtube\" />\n";
                const string netflixPackage =
                    "        <package android:name=\"com.netflix.mediaclient\" />\n";
                const string shizukuPackage =
                    "        <package android:name=\"moe.shizuku.privileged.api\" />\n";
                string missingQueries = string.Empty;
                if (!xml.Contains("com.google.android.youtube"))
                    missingQueries += youtubePackage;
                if (!xml.Contains("com.netflix.mediaclient"))
                    missingQueries += netflixPackage;
                if (!xml.Contains("moe.shizuku.privileged.api"))
                    missingQueries += shizukuPackage;
                if (!string.IsNullOrEmpty(missingQueries))
                {
                    int queryEnd = xml.IndexOf("</queries>", StringComparison.Ordinal);
                    if (queryEnd >= 0)
                    {
                        xml = xml.Insert(queryEnd, missingQueries);
                    }
                    else
                    {
                        int application = xml.IndexOf(
                            "<application",
                            StringComparison.Ordinal);
                        if (application < 0)
                            throw new InvalidDataException(
                                "Generated secure-surface manifest has no application: " +
                                manifest);
                        xml = xml.Insert(
                            application,
                            "    <queries>\n" + missingQueries + "    </queries>\n");
                    }
                }

                if (!xml.Contains("moe.shizuku.manager.permission.API_V23"))
                {
                    int application = xml.IndexOf(
                        "<application",
                        StringComparison.Ordinal);
                    xml = xml.Insert(
                        application,
                        "    <uses-permission android:name=\"moe.shizuku.manager.permission.API_V23\" />\n");
                }

                if (!xml.Contains("rikka.shizuku.ShizukuProvider"))
                {
                    int applicationEnd = xml.IndexOf(
                        "</application>",
                        StringComparison.Ordinal);
                    if (applicationEnd < 0)
                        throw new InvalidDataException(
                            "Generated secure-surface manifest has no application end: " +
                            manifest);
                    const string provider =
                        "        <provider\n" +
                        "            android:name=\"rikka.shizuku.ShizukuProvider\"\n" +
                        "            android:authorities=\"${applicationId}.shizuku\"\n" +
                        "            android:enabled=\"true\"\n" +
                        "            android:exported=\"true\"\n" +
                        "            android:multiprocess=\"false\"\n" +
                        "            android:permission=\"android.permission.INTERACT_ACROSS_USERS_FULL\" />\n";
                    xml = xml.Insert(applicationEnd, provider);
                }
                File.WriteAllText(manifest, xml);
            }

            string gradle = Path.Combine(unityLibraryPath, "build.gradle");
            if (!File.Exists(gradle))
                throw new FileNotFoundException(
                    "Generated unityLibrary build.gradle missing.", gradle);
            string text = File.ReadAllText(gradle);
            const string marker = "// MLOMEGA_SECURE_SURFACE_MEDIA3";
            const string plainShizuku =
                "    implementation('dev.rikka.shizuku:api:13.1.5')\n" +
                "    implementation('dev.rikka.shizuku:provider:13.1.5')\n";
            const string isolatedShizuku =
                "    implementation('dev.rikka.shizuku:api:13.1.5') {\n" +
                "        exclude group: 'androidx.annotation'\n" +
                "    }\n" +
                "    implementation('dev.rikka.shizuku:provider:13.1.5') {\n" +
                "        exclude group: 'androidx.annotation'\n" +
                "    }\n";
            if (text.Contains(plainShizuku))
                text = text.Replace(plainShizuku, isolatedShizuku);
            if (!text.Contains(marker))
            {
                int dependencies = text.IndexOf(
                    "dependencies {",
                    StringComparison.Ordinal);
                if (dependencies < 0)
                    throw new InvalidDataException(
                        "Generated unityLibrary build.gradle has no dependencies block: " +
                        gradle);
                dependencies += "dependencies {".Length;
                string media3 =
                    "\n    // MLOMEGA_SECURE_SURFACE_MEDIA3\n" +
                    "    compileOnly files('compile-stubs/taskorganizer-stubs.jar')\n" +
                    isolatedShizuku +
                    "    implementation('androidx.media3:media3-exoplayer:1.5.1') {\n" +
                    "        exclude group: 'androidx.core'\n" +
                    "        exclude group: 'androidx.annotation'\n" +
                    "        exclude group: 'androidx.collection'\n" +
                    "        exclude group: 'androidx.exifinterface'\n" +
                    "        exclude group: 'org.jetbrains.kotlin', module: 'kotlin-stdlib'\n" +
                    "        exclude group: 'org.jetbrains', module: 'annotations'\n" +
                    "    }\n" +
                    "    implementation('androidx.media3:media3-exoplayer-dash:1.5.1') {\n" +
                    "        exclude group: 'androidx.core'\n" +
                    "        exclude group: 'androidx.annotation'\n" +
                    "        exclude group: 'androidx.collection'\n" +
                    "        exclude group: 'androidx.exifinterface'\n" +
                    "        exclude group: 'org.jetbrains.kotlin', module: 'kotlin-stdlib'\n" +
                    "        exclude group: 'org.jetbrains', module: 'annotations'\n" +
                    "    }\n" +
                    (isolatedWebVr
                        ? "    implementation('androidx.media3:media3-exoplayer-hls:1.5.1') {\n" +
                          "        exclude group: 'androidx.core'\n" +
                          "        exclude group: 'androidx.annotation'\n" +
                          "        exclude group: 'androidx.collection'\n" +
                          "        exclude group: 'androidx.exifinterface'\n" +
                          "        exclude group: 'org.jetbrains.kotlin', module: 'kotlin-stdlib'\n" +
                          "        exclude group: 'org.jetbrains', module: 'annotations'\n" +
                          "    }\n"
                        : string.Empty);
                text = text.Insert(dependencies, media3);
                const string allJars =
                    "implementation fileTree(dir: 'libs', include: ['*.jar'])";
                const string spikeJars =
                    "implementation fileTree(dir: 'libs', include: ['*.jar'], " +
                    "exclude: ['guava-27.0.1-android.jar', " +
                    "'failureaccess-1.0.1.jar', " +
                    "'listenablefuture-9999.0-empty-to-avoid-conflict-with-guava.jar'])";
                if (!text.Contains(allJars))
                    throw new InvalidDataException(
                        "Generated unityLibrary JAR fileTree declaration changed: " +
                        gradle);
                text = text.Replace(allJars, spikeJars);
            }
            File.WriteAllText(gradle, text);

            Debug.Log(
                "[AndroidBuildXreal] Isolated Media3/Shizuku display probe injected: " +
                java + " + " + trustedServiceJava + " + " + taskProbeJava +
                " + " + physicalStereoProbeJava + " + " + nativeProbeDestination +
                " + " + compileStubs +
                (webVrJava != null ? " + " + webVrJava : string.Empty));
        }
    }
}
