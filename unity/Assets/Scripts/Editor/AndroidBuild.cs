// MLOmega V19 â€” E46-D
// Reproducible batchmode Android APK build for the PhoneOnly profile.
//
// Runs headless:
//   Unity.exe -batchmode -quit -projectPath <apps/xr-mobile> \
//     -executeMethod MLOmega.XR.Editor.AndroidBuild.BuildApk -logFile -
//
// It (1) points Unity at the external Android toolchain configured by
// scripts/BUILD_ANDROID_PLUGINS.ps1 (JDK 17 + system SDK + NDK r23b) so no
// Hub-embedded module is required, (2) forces the PhoneOnly player settings
// (IL2CPP, ARM64, min/target SDK, the PhoneOnly adapter/define), (3) ensures the
// PhoneOnly scene exists, and (4) writes the APK to build/android/.
//
// Overridable by environment variable:
//   MLOMEGA_ANDROID_SDK   â€” Android SDK root      (default %LOCALAPPDATA%\Android\Sdk)
//   MLOMEGA_ANDROID_NDK   â€” NDK root              (default <sdk>\ndk\23.1.7779620)
//   MLOMEGA_ANDROID_JDK   â€” JDK 17 home           (default Microsoft OpenJDK 17.0.19)
//   MLOMEGA_GRADLE_HOME   â€” Gradle 8.7 home       (default <repo>\.tools\gradle-8.7)
//   MLOMEGA_APK_OUT       â€” APK output path       (default build/android/mlomega-phoneonly.apk)
//   MLOMEGA_PC_HOST       â€” LAN/Tailscale host injected into MLOmegaConfig
//   MLOMEGA_PC_PORT       â€” SessionHub port
using System;
using System.IO;
using MLOmega.XR.Core;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MLOmega.XR.Editor
{
    public static class AndroidBuild
    {
        private const string ScenePath = "Assets/Scenes/PhoneOnly.unity";
        private const string ConfigPath = "Assets/Config/MLOmegaPhoneOnly.asset";
        private const string NdkVersion = "23.1.7779620";

        // E48-A: the small device models embedded in the APK (StreamingAssets) so
        // wake word + gestures work out-of-the-box. The two streaming ASR models
        // (~300-380 MB each) are NOT embedded — the app downloads them at first
        // launch (ModelProvisioner). These names must match the repo models/device/
        // layout (extracted KWS dir + the two MediaPipe .task files).
        private static readonly string[] EmbeddedModelNames =
        {
            "sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01",
            "hand_landmarker.task",
            "gesture_recognizer.task",
            // Silero VAD (~2 MB) gates the whole AsrKwsService stream — without it
            // neither the wake word nor offline subtitles start.
            "silero_vad.onnx",
            // YAMNet (~4 MB) powers the opt-in semantic sound overlay from the
            // existing WebRTC PCM fan-out.
            "yamnet.tflite",
        };

        [MenuItem("MLOmega/Build PhoneOnly APK")]
        public static void BuildApk()
        {
            ConfigureExternalTools();
            ConfigurePlayerSettings();
            EnsureScene();
            EmbedSmallDeviceModels();
            ApplyEndpointOverride();

            string outPath = Env("MLOMEGA_APK_OUT",
                Path.GetFullPath(Path.Combine("build", "android", "mlomega-phoneonly.apk")));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
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
                    $"[AndroidBuild] APK build failed: {summary.result} " +
                    $"({summary.totalErrors} errors) -> {outPath}");
            }
            Debug.Log($"[AndroidBuild] APK OK: {outPath} ({summary.totalSize} bytes)");
        }

        // --- toolchain -------------------------------------------------------
        private static void ConfigureExternalTools()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string sdk = Env("MLOMEGA_ANDROID_SDK", Path.Combine(localAppData, "Android", "Sdk"));
            string ndk = Env("MLOMEGA_ANDROID_NDK", Path.Combine(sdk, "ndk", NdkVersion));
            string jdk = Env("MLOMEGA_ANDROID_JDK",
                @"C:\Program Files\Microsoft\jdk-17.0.19.10-hotspot");
            string gradle = Env("MLOMEGA_GRADLE_HOME",
                Path.GetFullPath(Path.Combine("..", "..", ".tools", "gradle-8.7")));

            Require(sdk, "Android SDK");
            Require(ndk, "Android NDK r23b");
            Require(jdk, "JDK 17");

            // Tell Unity to use these external tools rather than the (absent)
            // Hub-embedded ones. Both the legacy EditorPrefs keys and the Unity 6
            // AndroidExternalToolsSettings API are set for robustness.
            EditorPrefs.SetBool("SdkUseEmbedded", false);
            EditorPrefs.SetBool("NdkUseEmbedded", false);
            EditorPrefs.SetBool("JdkUseEmbedded", false);
            EditorPrefs.SetBool("GradleUseEmbedded", Directory.Exists(gradle) ? false : true);
            EditorPrefs.SetString("AndroidSdkRoot", sdk);
            EditorPrefs.SetString("AndroidNdkRootR23", ndk);
            EditorPrefs.SetString("AndroidNdkRoot", ndk);
            EditorPrefs.SetString("JdkPath", jdk);
            if (Directory.Exists(gradle)) EditorPrefs.SetString("GradlePath", gradle);

#if UNITY_2022_2_OR_NEWER
            AndroidExternalToolsSettings.sdkRootPath = sdk;
            AndroidExternalToolsSettings.ndkRootPath = ndk;
            AndroidExternalToolsSettings.jdkRootPath = jdk;
            if (Directory.Exists(gradle)) AndroidExternalToolsSettings.gradlePath = gradle;
#endif
            Debug.Log($"[AndroidBuild] SDK={sdk} NDK={ndk} JDK={jdk} Gradle={gradle}");
        }

        // --- player settings -------------------------------------------------
        private static void ConfigurePlayerSettings()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.runInBackground = true;
            // Never inherit the XREAL/G1 identifier from a previous build target.
            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android, "com.mlomega.xr.phoneonly");

            // PhoneOnly profile: no XREAL SDK. The proprietary tarball define stays
            // absent so the guarded adapters compile without it.
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android);
            defines = defines.Replace(";XREAL_SDK_PRESENT", "")
                .Replace("XREAL_SDK_PRESENT;", "")
                .Replace("XREAL_SDK_PRESENT", "")
                .Replace(";XR_HANDS", "")
                .Replace("XR_HANDS;", "")
                .Replace("XR_HANDS", "")
                .Trim(';');
            if (!defines.Contains("MLOMEGA_PHONE_ONLY"))
            {
                defines = string.IsNullOrEmpty(defines)
                    ? "MLOMEGA_PHONE_ONLY"
                    : defines + ";MLOMEGA_PHONE_ONLY";
            }
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, defines);
        }

        private static void EnsureScene()
        {
            // Always regenerate from the canonical builder. Merely checking that the
            // YAML exists produced stale APKs after runtime components were added.
            PhoneOnlySceneBuilder.BuildScene();
            if (!File.Exists(ScenePath))
                throw new Exception($"[AndroidBuild] PhoneOnly scene missing after build: {ScenePath}");
        }

        // --- embedded small device models (E48-A) ---------------------------
        // Copy KWS + the two MediaPipe .task files from the repo models/device/
        // into Assets/StreamingAssets/models/ so they ship inside the APK. At
        // first launch the app copies StreamingAssets/models -> files/models
        // before the ModelProvisioner computes what is still missing (see
        // StreamingAssetsModelInstaller). Absent source = skip + warn (never fail
        // the build — the background fetch tolerates their absence).
        internal static void EmbedSmallDeviceModels()
        {
            // Repo root is two levels above the Unity project (apps/xr-mobile).
            string repoRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", ".."));
            string src = Path.Combine(repoRoot, "models", "device");
            string dst = Path.Combine(Application.dataPath, "StreamingAssets", "models");
            Directory.CreateDirectory(dst);

            foreach (string name in EmbeddedModelNames)
            {
                string from = Path.Combine(src, name);
                string to = Path.Combine(dst, name);
                if (Directory.Exists(from))
                {
                    CopyDirectory(from, to);
                    Debug.Log($"[AndroidBuild] embedded model dir: {name}");
                }
                else if (File.Exists(from))
                {
                    File.Copy(from, to, overwrite: true);
                    Debug.Log($"[AndroidBuild] embedded model file: {name}");
                }
                else
                {
                    Debug.LogWarning(
                        $"[AndroidBuild] embedded model absent, skipped: {from} " +
                        "(run scripts/fetch_models_v19.py --device). It will be " +
                        "downloaded at first launch instead.");
                }
            }

            // Write an index of every embedded file (relative to StreamingAssets/models)
            // so the runtime installer can enumerate them: on Android StreamingAssets
            // lives inside the APK jar and cannot be directory-listed at runtime, so
            // it reads this index and UnityWebRequest-copies each listed file.
            WriteEmbeddedIndex(dst);
            AssetDatabase.Refresh();
        }

        private static void WriteEmbeddedIndex(string modelsDir)
        {
            var rel = new System.Collections.Generic.List<string>();
            if (Directory.Exists(modelsDir))
            {
                foreach (string file in Directory.GetFiles(modelsDir, "*", SearchOption.AllDirectories))
                {
                    string r = file.Substring(modelsDir.Length + 1).Replace('\\', '/');
                    if (r.Equals("index.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    rel.Add(r);
                }
            }
            rel.Sort(StringComparer.Ordinal);
            File.WriteAllText(Path.Combine(modelsDir, "index.txt"), string.Join("\n", rel));
            Debug.Log($"[AndroidBuild] embedded model index: {rel.Count} file(s)");
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                // Skip test wavs / readmes to keep the embedded footprint minimal;
                // sherpa only needs the onnx + tokens (+ keywords for KWS).
                string fname = Path.GetFileName(file);
                if (fname.Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(to, fname), overwrite: true);
            }
            foreach (string dir in Directory.GetDirectories(from))
            {
                string dname = Path.GetFileName(dir);
                if (dname.Equals("test_wavs", StringComparison.OrdinalIgnoreCase)) continue;
                CopyDirectory(dir, Path.Combine(to, dname));
            }
        }

        // --- endpoint injection ---------------------------------------------
        internal static void ApplyEndpointOverride(string configPath = ConfigPath)
        {
            string host = Environment.GetEnvironmentVariable("MLOMEGA_PC_HOST");
            string port = Environment.GetEnvironmentVariable("MLOMEGA_PC_PORT");
            if (string.IsNullOrEmpty(host) && string.IsNullOrEmpty(port)) return;

            var config = AssetDatabase.LoadAssetAtPath<MLOmegaConfig>(configPath);
            if (config == null) return;
            var so = new SerializedObject(config);
            if (!string.IsNullOrEmpty(host)) so.FindProperty("_pcHost").stringValue = host;
            if (!string.IsNullOrEmpty(port) && int.TryParse(port, out int p))
                so.FindProperty("_sessionHubPort").intValue = p;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AndroidBuild] Endpoint override host={host} port={port}");
        }

        // --- helpers ---------------------------------------------------------
        private static string Env(string key, string fallback)
        {
            string v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        private static void Require(string path, string what)
        {
            if (!Directory.Exists(path))
                throw new Exception($"[AndroidBuild] {what} not found at: {path}");
        }
    }
}
