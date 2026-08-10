// MLOmega V19 — XREAL product-only runtime assets.
// This component is added only to the generated XrealProduct scene. Its
// serialized references force hardware-only assets into the glasses APK without
// changing the PhoneOnly scene or relying on Shader.Find/string reachability.
using UnityEngine;

namespace MLOmega.XR.Core
{
    public sealed class XrealRuntimeAssets : MonoBehaviour
    {
        [SerializeField] private Shader _yuv420ToRgb;

        public Shader Yuv420ToRgb => _yuv420ToRgb;
    }
}
