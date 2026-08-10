using System;
using UnityEngine;

namespace MLOmega.XR.UI
{
    internal static class XrealPrivateLensControl
    {
        private const string Bridge =
            "com.mlomega.xr.lens.XrealLensControlBridge";

        public static string Probe() => Call("probe");
        public static string ValidateCurrent() => Call("validateCurrent");
        public static string StepBrightness(int direction) =>
            Call("stepBrightness", direction);
        public static string StepEc(int direction) => Call("stepEc", direction);

        public static bool IsSuccess(string result) =>
            !string.IsNullOrEmpty(result) &&
            (result.StartsWith("OK|", StringComparison.Ordinal) ||
             result.StartsWith("VALID|", StringComparison.Ordinal));

        public static string HumanStatus(string result)
        {
            if (!IsSuccess(result))
                return result != null && result.Contains("not_initialized")
                    ? "LENTILLES // INITIALISATION REQUISE"
                    : "LENTILLES // CONTROLE INDISPONIBLE";
            int brightness = Read(result, "b");
            int brightnessCount = Read(result, "bc");
            int ec = Read(result, "ec");
            int ecCount = Read(result, "ecc");
            return "LENTILLES // LUM " + (brightness + 1) + "/" +
                   brightnessCount + " • EC " + (ec + 1) + "/" + ecCount;
        }

        private static int Read(string result, string key)
        {
            string prefix = key + "=";
            foreach (string part in result.Split('|'))
                if (part.StartsWith(prefix, StringComparison.Ordinal) &&
                    int.TryParse(part.Substring(prefix.Length), out int value))
                    return value;
            return -1;
        }

        private static string Call(string method, params object[] args)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var bridge = new AndroidJavaClass(Bridge);
                return bridge.CallStatic<string>(method, args) ?? "ERR|null";
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[XrealLensControl] " + method +
                    " unavailable: " + exception.Message);
                return "ERR|bridge=" + exception.GetType().Name;
            }
#else
            return "ERR|platform=unsupported";
#endif
        }
    }
}
