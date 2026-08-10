using MLOmega.XR.UI;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    /// <summary>
    /// Small-frame, inference-only low-light enhancement. It never touches the
    /// texture shown to the glasses. Light mode performs bounded percentile
    /// stretch and gamma lift while preserving colour. Strong mode adapts its
    /// lift to the measured frame luminance and uses only a one-frame causal
    /// stabilizer while retaining the skin-colour cues used by MediaPipe. At the dedicated
    /// Eye pipeline's 256 px input this remains cheap enough for 25 fps.
    /// </summary>
    internal sealed class HandLowLightEnhancer
    {
        private readonly int[] _histogram = new int[256];
        private readonly byte[] _lookup = new byte[256];
        private byte[] _temporal;
        private int _width;
        private int _height;
        private bool _hasTemporal;

        public void Reset()
        {
            _hasTemporal = false;
            _width = 0;
            _height = 0;
            _temporal = null;
        }

        public void Process(
            Color32[] pixels,
            int width,
            int height,
            HandLowLightMode mode)
        {
            if (pixels == null || pixels.Length == 0 || mode == HandLowLightMode.Off)
            {
                _hasTemporal = false;
                return;
            }

            System.Array.Clear(_histogram, 0, _histogram.Length);
            long luminanceSum = 0L;
            for (int i = 0; i < pixels.Length; i++)
            {
                int luminance = Luma(pixels[i]);
                _histogram[luminance]++;
                luminanceSum += luminance;
            }

            float meanLuminance = luminanceSum / (float)pixels.Length;
            // Strong can stay selected between rooms. Under normal light no
            // enhancement is useful, so skip the two costly full-frame passes.
            if (mode == HandLowLightMode.Strong && meanLuminance >= 92f)
            {
                _hasTemporal = false;
                return;
            }
            // Strong must never damage a normally lit hand. Its extra lift fades
            // continuously above the penumbra range instead of being imposed on
            // every frame selected through the manual setting.
            float darkness = mode == HandLowLightMode.Strong
                ? Mathf.Clamp01((82f - meanLuminance) / 58f)
                : 0f;

            int low = Percentile(
                pixels.Length,
                Mathf.Lerp(.02f, .01f, darkness));
            int high = Percentile(
                pixels.Length,
                Mathf.Lerp(.98f, .995f, darkness));
            // A flat/no-signal frame must not be amplified into random landmarks.
            if (high - low < 12)
            {
                _hasTemporal = false;
                return;
            }

            float gamma = Mathf.Lerp(.78f, .60f, darkness);
            float floor = Mathf.Lerp(3f, 7f, darkness);
            float ceiling = Mathf.Lerp(224f, 245f, darkness);
            float span = Mathf.Max(1f, high - low);
            for (int value = 0; value < 256; value++)
            {
                float normalized = Mathf.Clamp01((value - low) / span);
                _lookup[value] = (byte)Mathf.Clamp(
                    Mathf.RoundToInt(floor + Mathf.Pow(normalized, gamma) * (ceiling - floor)),
                    0,
                    255);
            }

            if (mode == HandLowLightMode.Strong)
            {
                EnsureTemporal(width, height, pixels.Length);
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 colour = pixels[i];
                    int luminance = Mathf.Max(4, Luma(colour));
                    byte lifted = _lookup[luminance];
                    // No queued median and no CLAHE: both cost latency. At the
                    // darkest setting retain at least seven eighths of the live
                    // frame, fading the causal stabilizer out as light returns.
                    int previousWeight = _hasTemporal
                        ? Mathf.RoundToInt(darkness)
                        : 0;
                    byte stable = previousWeight > 0
                        ? (byte)((lifted * 7 + _temporal[i] + 4) / 8)
                        : lifted;
                    _temporal[i] = stable;
                    float scale = Mathf.Clamp(
                        stable / (float)luminance,
                        .65f,
                        3.4f);
                    pixels[i] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(colour.r * scale), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(colour.g * scale), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(colour.b * scale), 0, 255),
                        255);
                }
                _hasTemporal = true;
                return;
            }

            _hasTemporal = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 colour = pixels[i];
                int luminance = Mathf.Max(4, Luma(colour));
                float scale = Mathf.Clamp(_lookup[luminance] / (float)luminance, .65f, 3.2f);
                pixels[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(colour.r * scale), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(colour.g * scale), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(colour.b * scale), 0, 255),
                    255);
            }
        }

        private void EnsureTemporal(int width, int height, int count)
        {
            if (_temporal != null && _temporal.Length == count &&
                _width == width && _height == height)
                return;
            _temporal = new byte[count];
            _width = width;
            _height = height;
            _hasTemporal = false;
        }

        private int Percentile(int count, float fraction)
        {
            int wanted = Mathf.Clamp(Mathf.RoundToInt(count * fraction), 0, count - 1);
            int seen = 0;
            for (int value = 0; value < _histogram.Length; value++)
            {
                seen += _histogram[value];
                if (seen > wanted) return value;
            }
            return 255;
        }

        private static int Luma(Color32 colour) =>
            (colour.r * 54 + colour.g * 183 + colour.b * 19) >> 8;
    }
}
