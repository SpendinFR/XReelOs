// MLOmega V19 — E33
// AppLauncherBridge: Unity-side bridge to the native Android app launcher
// (com.mlomega.xr.reflexvision.AppLauncher). Turns open_app device commands into
// real Android Intents:
//   * Maps navigation  -> google.navigation:/geo: (Google Maps or any maps app);
//   * YouTube          -> vnd.youtube: / ACTION_VIEW on a youtube search URL;
//   * arbitrary app    -> getLaunchIntentForPackage(package).
//
// Editor / Windows dev has no Android plugin, so the calls log and return false
// (same DIRECT_ANDROID / editor split as LiveTransportBridge). This lets the whole
// device-command chain be developed and unit-tested without a device.
using UnityEngine;

namespace MLOmega.XR.Transport
{
    public sealed class AppLauncherBridge : MonoBehaviour
    {
        /// <summary>Open Google Maps navigation to a free-text destination (or Maps home if empty).</summary>
        public bool OpenMaps(string destination)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return CallLauncher("openMaps", destination ?? string.Empty);
#else
            Debug.Log($"[AppLauncher] (editor) openMaps: {destination}");
            return false;
#endif
        }

        /// <summary>Open YouTube on a search query (or the app home if empty).</summary>
        public bool OpenYouTube(string query)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return CallLauncher("openYouTube", query ?? string.Empty);
#else
            Debug.Log($"[AppLauncher] (editor) openYouTube: {query}");
            return false;
#endif
        }

        /// <summary>Launch an arbitrary installed app by package name.</summary>
        public bool OpenPackage(string package)
        {
            if (string.IsNullOrEmpty(package)) return false;
#if UNITY_ANDROID && !UNITY_EDITOR
            return CallLauncher("openPackage", package);
#else
            Debug.Log($"[AppLauncher] (editor) openPackage: {package}");
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private bool CallLauncher(string method, string arg)
        {
            try
            {
                using var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                using var launcher = new AndroidJavaClass("com.mlomega.xr.reflexvision.AppLauncher");
                return launcher.CallStatic<bool>(method, activity, arg);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AppLauncher] {method} failed: {ex.Message}");
                return false;
            }
        }
#endif
    }
}
