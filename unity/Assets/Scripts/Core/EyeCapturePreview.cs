// MLOmega V19 — E22 / Gate G1
// Pulls the latest Eye frame from the adapter, shows it on a quad, and measures
// display fps + carries frame_id and a monotonic clock (nanoseconds).
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>
    /// Renders the RGB Eye texture onto a world-space quad and exposes live
    /// counters (frame_id, monotonic ns, measured fps) for the status overlay.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public sealed class EyeCapturePreview : MonoBehaviour
    {
        [SerializeField] private XrSessionController _session;

        [Tooltip("Window (in frames) over which display fps is averaged.")]
        [SerializeField] private int _fpsWindow = 30;

        public long LastFrameId { get; private set; }
        public long LastCaptureMonotonicNs { get; private set; }
        public float MeasuredFps { get; private set; }
        public bool HasFrame { get; private set; }

        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;
        private float _accumTime;
        private int _accumFrames;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
            if (_session == null)
            {
                _session = FindAnyObjectByType<XrSessionController>();
            }
        }

        private void Update()
        {
            if (_session == null || _session.Adapter == null)
            {
                return;
            }

            EyeFrame? frame = _session.Adapter.TryGetLatestFrame();
            if (frame.HasValue && frame.Value.Texture != null)
            {
                EyeFrame f = frame.Value;
                LastFrameId = f.FrameId;
                LastCaptureMonotonicNs = f.CaptureMonotonicNs;
                HasFrame = true;

                _renderer.GetPropertyBlock(_mpb);
                _mpb.SetTexture("_BaseMap", f.Texture); // URP Unlit
                _mpb.SetTexture("_MainTex", f.Texture); // built-in / fallback
                _renderer.SetPropertyBlock(_mpb);

                // Preserve aspect ratio on the quad.
                if (f.Texture.height > 0)
                {
                    float aspect = (float)f.Texture.width / f.Texture.height;
                    Vector3 s = transform.localScale;
                    transform.localScale = new Vector3(Mathf.Abs(s.y) * aspect, s.y, s.z);
                }

                _accumFrames++;
            }

            // fps measured on the display cadence, averaged over the window.
            _accumTime += Time.unscaledDeltaTime;
            if (_accumFrames >= _fpsWindow || _accumTime >= 1.0f)
            {
                if (_accumTime > 0f && _accumFrames > 0)
                {
                    MeasuredFps = _accumFrames / _accumTime;
                }
                _accumTime = 0f;
                _accumFrames = 0;
            }
        }
    }
}
