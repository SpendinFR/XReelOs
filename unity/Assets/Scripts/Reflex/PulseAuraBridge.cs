using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Core;
using MLOmega.XR.Transport;
using MLOmega.XR.UI;
using Newtonsoft.Json;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    /// <summary>
    /// Opt-in, non-medical rPPG experiment. The PC supplies the actual SFace ROI
    /// only for a person whose studio consent includes physiology. The S24 samples
    /// a tiny 32x32 crop; no image or signal is persisted or uploaded.
    /// </summary>
    public sealed class PulseAuraBridge : MonoBehaviour
    {
        [SerializeField] private EyeCaptureSource _capture;
        [SerializeField] private LiveTransportBridge _transport;
        [SerializeField] private AugmentedRealityFeatureRegistry _features;
        [SerializeField] private LocalIntentSource _intents;
        [Range(5f, 12f)] [SerializeField] private float _sampleFps = 8f;

        private readonly PulseSignalEstimator _estimator =
            new PulseSignalEstimator(18.0);
        private Texture2D _readback;
        private Rect _sourceRoi;
        private Rect _uprightRoi;
        private string _trackId;
        private string _displayName;
        private string _consentId;
        private float _roiExpiresAt;
        private float _nextSampleAt;
        private float _nextIntentAt;
        private float _motionPenalty;

        public bool HasConsentedRoi =>
            !string.IsNullOrEmpty(_trackId) &&
            Time.unscaledTime <= _roiExpiresAt;

        private void Awake()
        {
            if (_capture == null) _capture = FindAnyObjectByType<EyeCaptureSource>();
            if (_transport == null)
                _transport = FindAnyObjectByType<LiveTransportBridge>();
            if (_features == null)
                _features = FindAnyObjectByType<AugmentedRealityFeatureRegistry>();
            if (_intents == null) _intents = FindAnyObjectByType<LocalIntentSource>();
        }

        private void OnEnable()
        {
            if (_capture != null) _capture.OnFrame += OnFrame;
            if (_transport != null) _transport.MessageReceived += OnMessage;
            if (_features != null) _features.FeatureChanged += OnFeatureChanged;
        }

        private void OnDisable()
        {
            if (_capture != null) _capture.OnFrame -= OnFrame;
            if (_transport != null) _transport.MessageReceived -= OnMessage;
            if (_features != null) _features.FeatureChanged -= OnFeatureChanged;
            ResetSignal();
            if (_readback != null)
            {
                Destroy(_readback);
                _readback = null;
            }
        }

        private void OnFeatureChanged(string feature, bool enabled)
        {
            if (
                feature == AugmentedRealityFeatureRegistry.PulseAura &&
                !enabled)
                ResetSignal();
        }

        private void OnMessage(string json)
        {
            if (
                string.IsNullOrEmpty(json) ||
                json.IndexOf("\"bio_roi\"", StringComparison.Ordinal) < 0)
                return;
            try
            {
                BioRoiMessage message = JsonConvert.DeserializeObject<BioRoiMessage>(json);
                if (
                    message == null ||
                    message.Persist ||
                    message.Signal != "rppg_experimental" ||
                    string.IsNullOrWhiteSpace(message.ConsentId) ||
                    message.FaceBbox == null)
                    return;
                Rect upright = message.FaceBbox.ToRect();
                if (upright.width <= 0f || upright.height <= 0f) return;
                Vector2 oldCenter = _uprightRoi.center;
                bool sameTrack = string.Equals(
                    _trackId,
                    message.TargetTrackId,
                    StringComparison.Ordinal);
                _trackId = message.TargetTrackId;
                _displayName = message.DisplayName;
                _consentId = message.ConsentId;
                _uprightRoi = upright;
                _sourceRoi = SourceRectFromUpright(
                    upright,
                    message.Rotation,
                    message.Mirrored);
                _roiExpiresAt =
                    Time.unscaledTime + Mathf.Clamp(message.TtlMs / 1000f, 1f, 8f);
                if (!sameTrack)
                {
                    _estimator.Clear();
                    _motionPenalty = 0f;
                }
                else
                {
                    _motionPenalty = Mathf.Clamp01(
                        Vector2.Distance(oldCenter, upright.center) * 8f);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PulseAura] invalid ROI: {ex.Message}");
            }
        }

        private void OnFrame(Texture texture, FrameEnvelope envelope)
        {
            if (
                texture == null ||
                _features == null ||
                !_features.IsActive(AugmentedRealityFeatureRegistry.PulseAura) ||
                !HasConsentedRoi ||
                Time.unscaledTime < _nextSampleAt)
                return;
            _nextSampleAt = Time.unscaledTime + 1f / Mathf.Max(5f, _sampleFps);
            if (!TryAverageRoi(texture, _sourceRoi, out Color mean)) return;
            double timestamp = envelope != null
                ? envelope.CaptureMonotonicNs / 1_000_000_000.0
                : Time.unscaledTimeAsDouble;
            _estimator.Push(
                timestamp,
                mean.r,
                mean.g,
                mean.b,
                _motionPenalty);
            _motionPenalty *= 0.92f;
            if (
                Time.unscaledTime < _nextIntentAt ||
                !_estimator.TryEstimate(out float bpm, out float quality))
                return;
            _nextIntentAt = Time.unscaledTime + 1f;
            _intents?.Emit(new UIIntent
            {
                Type = "ui_intent",
                ContractsVersion = ContractDefaults.Version,
                UiIntentId = "pulse-aura:" + _trackId,
                Producer = "ultralive",
                SourceFrameId = envelope?.FrameId,
                TargetTrackId = _trackId,
                Component = "pulse_aura",
                Anchor = new Dictionary<string, object>
                {
                    { "type", "screen_track" },
                    { "bbox", Box(_uprightRoi) },
                },
                Content = new Dictionary<string, object>
                {
                    { "kind", "rppg_aura" },
                    { "label", _displayName ?? "Personne consentante" },
                    { "bpm", bpm },
                    { "signal_quality", quality },
                    { "experimental", true },
                    { "persisted", false },
                    { "medical", false },
                    { "emotion", "not_inferred" },
                    { "consent_id", _consentId },
                },
                TruthLevel = "probable",
                Confidence = quality,
                Priority = 0.36,
                TtlMs = 1800,
                EvidenceRefs = new List<string>
                {
                    "consent:" + _consentId,
                    "device-rppg:" + (envelope?.FrameId ?? "live"),
                },
            });
        }

        private bool TryAverageRoi(Texture source, Rect roi, out Color mean)
        {
            mean = Color.black;
            if (roi.width <= 0f || roi.height <= 0f) return false;
            const int Size = 32;
            RenderTexture rt = RenderTexture.GetTemporary(
                Size,
                Size,
                0,
                RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(
                    source,
                    rt,
                    new Vector2(roi.width, roi.height),
                    new Vector2(roi.x, 1f - roi.y - roi.height));
                RenderTexture.active = rt;
                if (_readback == null)
                    _readback = new Texture2D(
                        Size,
                        Size,
                        TextureFormat.RGBA32,
                        false);
                _readback.ReadPixels(
                    new Rect(0, 0, Size, Size),
                    0,
                    0,
                    false);
                _readback.Apply(false, false);
                var pixels = _readback.GetRawTextureData<Color32>();
                double r = 0d, g = 0d, b = 0d;
                int accepted = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    int luminance =
                        (pixel.r * 54 + pixel.g * 183 + pixel.b * 19) >> 8;
                    if (luminance < 35 || luminance > 235) continue;
                    r += pixel.r;
                    g += pixel.g;
                    b += pixel.b;
                    accepted++;
                }
                if (accepted < pixels.Length / 4) return false;
                mean = new Color(
                    (float)(r / accepted / 255d),
                    (float)(g / accepted / 255d),
                    (float)(b / accepted / 255d));
                return true;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private void ResetSignal()
        {
            _trackId = null;
            _displayName = null;
            _consentId = null;
            _roiExpiresAt = 0f;
            _estimator.Clear();
        }

        public static Rect SourceRectFromUpright(
            Rect upright,
            int rotation,
            bool mirrored)
        {
            int normalized = ((rotation % 360) + 360) % 360;
            Rect source = normalized switch
            {
                90 => new Rect(
                    1f - upright.yMax,
                    upright.x,
                    upright.height,
                    upright.width),
                180 => new Rect(
                    1f - upright.xMax,
                    1f - upright.yMax,
                    upright.width,
                    upright.height),
                270 => new Rect(
                    upright.y,
                    1f - upright.xMax,
                    upright.height,
                    upright.width),
                _ => upright,
            };
            // VisionRT sees the same mirrored pixels as the source texture; the
            // flag is carried for audit and does not require a second flip here.
            _ = mirrored;
            source.x = Mathf.Clamp01(source.x);
            source.y = Mathf.Clamp01(source.y);
            source.width = Mathf.Clamp(source.width, 0f, 1f - source.x);
            source.height = Mathf.Clamp(source.height, 0f, 1f - source.y);
            return source;
        }

        private static Dictionary<string, object> Box(Rect value) =>
            new Dictionary<string, object>
            {
                { "x", value.x },
                { "y", value.y },
                { "w", value.width },
                { "h", value.height },
            };

        [Serializable]
        private sealed class BioRoiMessage
        {
            [JsonProperty("type")] public string Type;
            [JsonProperty("target_track_id")] public string TargetTrackId;
            [JsonProperty("display_name")] public string DisplayName;
            [JsonProperty("face_bbox")] public Bbox FaceBbox;
            [JsonProperty("rotation")] public int Rotation;
            [JsonProperty("mirrored")] public bool Mirrored;
            [JsonProperty("consent_id")] public string ConsentId;
            [JsonProperty("signal")] public string Signal;
            [JsonProperty("persist")] public bool Persist;
            [JsonProperty("ttl_ms")] public int TtlMs;
        }

        [Serializable]
        private sealed class Bbox
        {
            [JsonProperty("x")] public float X;
            [JsonProperty("y")] public float Y;
            [JsonProperty("w")] public float W;
            [JsonProperty("h")] public float H;

            public Rect ToRect() =>
                new Rect(
                    Mathf.Clamp01(X),
                    Mathf.Clamp01(Y),
                    Mathf.Clamp(W, 0f, 1f - Mathf.Clamp01(X)),
                    Mathf.Clamp(H, 0f, 1f - Mathf.Clamp01(Y)));
        }
    }

    /// <summary>Allocation-bounded autocorrelation estimator for green-channel rPPG.</summary>
    public sealed class PulseSignalEstimator
    {
        private readonly List<Sample> _samples = new List<Sample>(192);
        private readonly double _windowSeconds;

        public PulseSignalEstimator(double windowSeconds)
        {
            _windowSeconds = Math.Max(10.0, Math.Min(30.0, windowSeconds));
        }

        public int SampleCount => _samples.Count;
        public void Clear() => _samples.Clear();

        public void Push(
            double atSeconds,
            double red,
            double green,
            double blue,
            double motion)
        {
            double total = Math.Max(1e-6, red + green + blue);
            double chroma = (green - 0.5 * red - 0.5 * blue) / total;
            _samples.Add(new Sample
            {
                At = atSeconds,
                Value = chroma,
                Motion = Math.Max(0.0, Math.Min(1.0, motion)),
            });
            double cutoff = atSeconds - _windowSeconds;
            int remove = 0;
            while (remove < _samples.Count && _samples[remove].At < cutoff)
                remove++;
            if (remove > 0) _samples.RemoveRange(0, remove);
        }

        public bool TryEstimate(out float bpm, out float quality)
        {
            bpm = quality = 0f;
            int count = _samples.Count;
            if (count < 64) return false;
            double duration = _samples[count - 1].At - _samples[0].At;
            if (duration < 8.0) return false;
            double mean = 0d, motion = 0d;
            for (int i = 0; i < count; i++)
            {
                mean += _samples[i].Value;
                motion += _samples[i].Motion;
            }
            mean /= count;
            motion /= count;
            double variance = 0d;
            for (int i = 0; i < count; i++)
            {
                double value = _samples[i].Value - mean;
                variance += value * value;
            }
            variance /= count;
            if (variance < 1e-8) return false;
            double dt = duration / Math.Max(1, count - 1);
            if (dt <= 0.0 || dt > 0.25) return false;

            double best = -1d;
            int bestBpm = 0;
            for (int candidate = 45; candidate <= 180; candidate++)
            {
                double lag = (60.0 / candidate) / dt;
                int start = Math.Max(1, (int)Math.Ceiling(lag));
                if (start >= count / 2) continue;
                double covariance = 0d, left = 0d, right = 0d;
                for (int i = start; i < count; i++)
                {
                    double a = _samples[i].Value - mean;
                    double delayed = i - lag;
                    int lower = Math.Max(0, (int)Math.Floor(delayed));
                    int upper = Math.Min(count - 1, lower + 1);
                    double fraction = delayed - lower;
                    double b = (
                        _samples[lower].Value * (1.0 - fraction) +
                        _samples[upper].Value * fraction
                    ) - mean;
                    covariance += a * b;
                    left += a * a;
                    right += b * b;
                }
                double correlation =
                    covariance / Math.Sqrt(Math.Max(1e-12, left * right));
                if (correlation > best)
                {
                    best = correlation;
                    bestBpm = candidate;
                }
            }
            double motionFactor = Math.Max(0.0, 1.0 - motion * 1.8);
            double correlationQuality = Math.Max(
                0.0,
                Math.Min(1.0, (best - 0.28) / 0.52));
            quality = (float)(correlationQuality * motionFactor);
            if (quality < 0.45f || bestBpm == 0) return false;
            bpm = bestBpm;
            return true;
        }

        private struct Sample
        {
            public double At;
            public double Value;
            public double Motion;
        }
    }
}
