// MLOmega V19 — E48-A
// First-launch copy of the APK-embedded small device models
// (StreamingAssets/models -> getExternalFilesDir()/models). The KWS + the two
// MediaPipe .task bundles are shipped inside the APK (AndroidBuild.cs) so wake
// word + gestures work out-of-the-box; this component lands them in the app's
// models dir before the ModelProvisioner computes what is still missing (only the
// two streaming ASR models, which are downloaded).
//
// On Android StreamingAssets lives inside the APK jar and cannot be
// directory-listed at runtime, so files are read via UnityWebRequest and the set
// of files comes from the build-time index.txt. Idempotent: a file already
// present in files/models is skipped (never overwrites a downloaded/updated one).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace MLOmega.XR.Core
{
    /// <summary>
    /// Copies APK-embedded models into the app models dir once at first launch.
    /// <see cref="Done"/> flips true when finished (also true immediately in the
    /// editor / when nothing is embedded). <see cref="ModelProvisioningBridge"/>
    /// waits on this before downloading, so the two paths never race on the same file.
    /// </summary>
    public sealed class StreamingAssetsModelInstaller : MonoBehaviour
    {
        /// <summary>True once the embedded models have been installed (or there were none).</summary>
        public bool Done { get; private set; }

        /// <summary>Raised on the main thread when installation completes.</summary>
        public event Action Completed;

        private void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(Install());
#else
            // Editor/desktop: models are pushed to files/models directly for dev.
            Complete();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator Install()
        {
            string modelsRoot = ExternalFilesModelsDir();
            Directory.CreateDirectory(modelsRoot);

            // On Android streamingAssetsPath is a jar: URL; build child URLs with '/'.
            string srcBase = Application.streamingAssetsPath + "/models";
            string indexUrl = srcBase + "/index.txt";

            string indexText = null;
            yield return ReadText(indexUrl, t => indexText = t);
            if (string.IsNullOrEmpty(indexText))
            {
                // No index → nothing embedded (or a fetch-less build). Not an error.
                Debug.Log("[ModelInstaller] no embedded model index; nothing to install.");
                Complete();
                yield break;
            }

            int copied = 0, skipped = 0;
            foreach (string line in indexText.Split('\n'))
            {
                string rel = line.Trim();
                if (rel.Length == 0) continue;
                string dest = Path.Combine(modelsRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(dest) && new FileInfo(dest).Length > 0)
                {
                    skipped++;
                    continue; // already installed or downloaded — never clobber.
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                bool ok = false;
                yield return ReadBytes(srcBase + "/" + rel, bytes =>
                {
                    if (bytes != null && bytes.Length > 0)
                    {
                        // Atomic-ish: write a temp then move into place.
                        string tmp = dest + ".part";
                        File.WriteAllBytes(tmp, bytes);
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(tmp, dest);
                        ok = true;
                    }
                });
                if (ok) copied++;
                else Debug.LogWarning($"[ModelInstaller] could not read embedded {rel}");
            }
            Debug.Log($"[ModelInstaller] embedded models installed: {copied} copied, {skipped} present.");
            Complete();
        }

        private static string ExternalFilesModelsDir()
        {
            using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity");
            using var ctx = activity.Call<AndroidJavaObject>("getApplicationContext");
            using var extDir = ctx.Call<AndroidJavaObject>("getExternalFilesDir", (object)null);
            string baseDir = extDir != null
                ? extDir.Call<string>("getAbsolutePath")
                : ctx.Call<AndroidJavaObject>("getFilesDir").Call<string>("getAbsolutePath");
            return Path.Combine(baseDir, "models");
        }

        private static IEnumerator ReadText(string url, Action<string> onDone)
        {
            using var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();
#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
            onDone(ok ? req.downloadHandler.text : null);
        }

        private static IEnumerator ReadBytes(string url, Action<byte[]> onDone)
        {
            using var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();
#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
            onDone(ok ? req.downloadHandler.data : null);
        }
#endif

        private void Complete()
        {
            Done = true;
            Completed?.Invoke();
        }
    }
}
