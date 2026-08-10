using System;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Lab-only Android runtime helper. Product and Atelier never call the
    /// Shizuku preflight because AndroidBuildXreal only injects it in Lab builds.
    /// </summary>
    internal static class XrLabAndroidRuntimeBridge
    {
        private const string Bridge =
            "com.mlomega.xr.securesurface.SecureWidevinePlayer";

        internal static void PrepareRuntime()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            WithActivity((java, activity) =>
                java.CallStatic("prepareRuntime", activity));
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void WithActivity(
            Action<AndroidJavaClass, AndroidJavaObject> action)
        {
            try
            {
                using var unity = new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unity.GetStatic<AndroidJavaObject>("currentActivity");
                using var java = new AndroidJavaClass(Bridge);
                action?.Invoke(java, activity);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[XR-RUNTIME-BRIDGE] Android action unavailable: " +
                    exception.Message);
            }
        }
#endif
    }
}
