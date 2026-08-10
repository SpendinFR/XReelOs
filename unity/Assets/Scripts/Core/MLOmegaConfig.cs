// MLOmega V19 — E23
// Runtime configuration for the XR client: which PC SessionHub to reach, which
// device adapter to run, and the clock-sync / capture cadences. Created as a
// ScriptableObject asset (menu "MLOmega/Config/Create MLOmega Config") so the
// same build can target a different PC or capability profile without recompiling.
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>
    /// Which <see cref="IXRDeviceAdapter"/> implementation to instantiate at
    /// runtime. Values map one-to-one to the <c>display</c>/<c>capture</c> pairs of
    /// <c>configs/user_profile.yaml</c> (handoff §3.5):
    ///   - <see cref="Xreal"/>     ⟵ display: xreal_one_pro   + capture: xreal_eye
    ///   - <see cref="PhoneOnly"/> ⟵ display: phone_only      + capture: phone_camera
    ///   - <see cref="Simulated"/> ⟵ editor / no-hardware dev (companion_web display)
    /// </summary>
    public enum XrAdapterKind
    {
        /// <summary>Pick automatically: editor -> Simulated, device -> Xreal.</summary>
        Auto = 0,
        Xreal = 1,
        Simulated = 2,
        PhoneOnly = 3
    }

    /// <summary>Language of the on-device streaming ASR (maps to a sherpa-onnx model).</summary>
    public enum ReflexAsrLanguage
    {
        En = 0,
        Fr = 1
    }

    /// <summary>
    /// E36 §1 — one ordered PC endpoint the client may reach the SessionHub at.
    /// Outside the home the phone is on 4G/5G and the PC is behind NAT; a single
    /// LAN IP no longer works. The client tries the endpoints IN ORDER (LAN first,
    /// then a VPN-tunnel address such as Tailscale 100.x) and uses the first whose
    /// <c>/health</c> answers. See <see cref="MLOmegaConfig.Endpoints"/>.
    /// </summary>
    [System.Serializable]
    public sealed class PcEndpoint
    {
        [Tooltip("Friendly name, e.g. 'lan' or 'tailscale'. First entry is preferred.")]
        [SerializeField] private string _name = "lan";

        [Tooltip("Hostname or IP of the PC running the SessionHub / gateway.")]
        [SerializeField] private string _host = "192.0.2.10";

        [Tooltip("SessionHub HTTP port. V19 uses the 87xx range (never 8766).")]
        [SerializeField] private int _port = 8710;

        [Tooltip("Use https for this endpoint's base URL.")]
        [SerializeField] private bool _useTls;

        public string Name => _name;
        public string Host => _host;
        public int Port => _port;
        public bool UseTls => _useTls;

        public string BaseUrl => $"{(_useTls ? "https" : "http")}://{_host}:{_port}";
        public string HealthUrl => $"{BaseUrl}/health";
        public string WebrtcOfferUrl => $"{BaseUrl}/webrtc/offer";
    }

    [CreateAssetMenu(
        fileName = "MLOmegaConfig",
        menuName = "MLOmega/Config/MLOmega Config",
        order = 0)]
    public sealed class MLOmegaConfig : ScriptableObject
    {
        [Header("PC SessionHub (services/live-pc)")]
        [Tooltip("Hostname or IP of the PC running the SessionHub / gateway.")]
        [SerializeField] private string _pcHost = "192.0.2.10";

        [Tooltip("SessionHub HTTP port. V19 uses the 87xx range (never 8766).")]
        [SerializeField] private int _sessionHubPort = 8710;

        [Tooltip("Use https for the SessionHub base URL.")]
        [SerializeField] private bool _useTls;

        [Header("Outside access — endpoint failover (E36)")]
        [Tooltip("Ordered PC endpoints tried in order (LAN first, then a VPN tunnel " +
                 "like Tailscale 100.x). The first whose /health answers is used; if " +
                 "it drops, the client fails over to the next. Left empty → the single " +
                 "_pcHost above is used (backward compatible).")]
        [SerializeField] private PcEndpoint[] _endpoints = new PcEndpoint[0];

        [Tooltip("This device's stable id, sent when creating a session.")]
        [SerializeField] private string _deviceId = "s25-primary";

        [Header("Adapter selection")]
        [Tooltip("Which device adapter to run. Auto -> Simulated in editor, Xreal on device.")]
        [SerializeField] private XrAdapterKind _adapter = XrAdapterKind.Auto;

        [Header("Clock sync")]
        [Tooltip("Seconds between periodic clock re-measurements.")]
        [Min(1f)]
        [SerializeField] private float _clockSyncIntervalSeconds = 30f;

        [Tooltip("Number of round-trips per clock-sync burst (best RTT wins).")]
        [Min(1)]
        [SerializeField] private int _clockSyncSamplesPerBurst = 5;

        [Tooltip("Max retries for a single failed clock-sync round-trip before giving up this burst.")]
        [Min(0)]
        [SerializeField] private int _clockSyncMaxRetries = 3;

        [Header("Session pairing")]
        [Tooltip("Refresh the ephemeral token this many seconds before it is assumed expired.")]
        [Min(5f)]
        [SerializeField] private float _tokenRenewLeadSeconds = 60f;

        [Tooltip("Assumed token lifetime in seconds (server authoritative; used only to schedule renewal).")]
        [Min(30f)]
        [SerializeField] private float _assumedTokenLifetimeSeconds = 600f;

        [Header("Capture cadence")]
        [Tooltip("Target frames-per-second published by EyeCaptureSource (0 = every device update).")]
        [Min(0f)]
        [SerializeField] private float _captureFps = 30f;

        [Tooltip("Pose publish rate on the standalone pose stream (Hz).")]
        [Min(1f)]
        [SerializeField] private float _posePublishHz = 60f;

        [Header("HTTP")]
        [Tooltip("Per-request timeout in seconds for SessionHub calls.")]
        [Min(1f)]
        [SerializeField] private float _httpTimeoutSeconds = 5f;

        [Header("Ultra-Live reflex (E26)")]
        [Tooltip("Spoken wake word that arms command listening. E58: detected in the " +
                 "on-device streaming ASR transcript (natural pronunciation in the ASR " +
                 "language), NOT the English KWS. Runtime-settable (PC push, no rebuild). " +
                 "Default 'viki'. Pick a RARE word to avoid false triggers.")]
        [SerializeField] private string _wakeWord = "viki";

        [Tooltip("Language of the on-device streaming ASR/subtitles (fr or en). " +
                 "Selects the sherpa-onnx model loaded by AsrKwsService. Also the " +
                 "language the wake word is heard in (E58).")]
        [SerializeField] private ReflexAsrLanguage _asrLanguage = ReflexAsrLanguage.Fr;

        [Tooltip("E47-A: how long the wake word keeps command routing armed. " +
                 "Final transcripts ending inside this window are flagged as " +
                 "commands (is_command). Capture never stops — this only gates " +
                 "routing, not the audio uplink.")]
        [Min(1f)]
        [SerializeField] private float _commandWindowSeconds = 6f;

        [Tooltip("E48-A: start with live translation ON. When on, FINAL ASR segments " +
                 "whose language differs from the subtitle language are translated " +
                 "on-device (offline OPUS-MT) and shown under the original subtitle. " +
                 "Toggled at runtime by the menu entry / the « traduis en direct » " +
                 "voice command. Off by default (opt-in).")]
        [SerializeField] private bool _translateLiveDefault;

        public string PcHost => _pcHost;
        public int SessionHubPort => _sessionHubPort;
        public bool UseTls => _useTls;
        public string DeviceId => _deviceId;
        public XrAdapterKind Adapter => _adapter;
        public float ClockSyncIntervalSeconds => _clockSyncIntervalSeconds;
        public int ClockSyncSamplesPerBurst => _clockSyncSamplesPerBurst;
        public int ClockSyncMaxRetries => _clockSyncMaxRetries;
        public float TokenRenewLeadSeconds => _tokenRenewLeadSeconds;
        public float AssumedTokenLifetimeSeconds => _assumedTokenLifetimeSeconds;
        public float CaptureFps => _captureFps;
        public float PosePublishHz => _posePublishHz;
        public float HttpTimeoutSeconds => _httpTimeoutSeconds;
        public string WakeWord => _wakeWord;
        public ReflexAsrLanguage AsrLanguage => _asrLanguage;

        /// <summary>E47-A: wake-word command window length in seconds.</summary>
        public float CommandWindowSeconds => _commandWindowSeconds;

        /// <summary>E48-A: whether live on-device translation starts ON.</summary>
        public bool TranslateLiveDefault => _translateLiveDefault;

        /// <summary>The configured endpoint list (may be empty).</summary>
        public PcEndpoint[] Endpoints => _endpoints;

        /// <summary>
        /// E36 §1 — the ordered endpoints the client should try, LAN/preferred first.
        /// When <see cref="Endpoints"/> is empty this yields a single implicit
        /// endpoint built from the legacy <c>_pcHost</c> so existing configs keep
        /// working. <see cref="SessionPairing"/> probes these in order and fails over.
        /// </summary>
        public PcEndpoint[] ResolvedEndpoints
        {
            get
            {
                if (_endpoints != null && _endpoints.Length > 0)
                {
                    return _endpoints;
                }
                var single = new PcEndpoint();
                // Mirror the legacy single-host fields onto the implicit endpoint via
                // JSON so the private serialized fields are populated without a ctor.
                UnityEngine.JsonUtility.FromJsonOverwrite(
                    $"{{\"_name\":\"lan\",\"_host\":\"{_pcHost}\",\"_port\":{_sessionHubPort}," +
                    $"\"_useTls\":{(_useTls ? "true" : "false")}}}", single);
                return new[] { single };
            }
        }

        /// <summary>
        /// Base URL for the SessionHub, e.g. <c>http://192.0.2.10:8710</c>.
        /// Uses the first resolved endpoint (LAN unless overridden by failover).
        /// </summary>
        public string SessionHubBaseUrl =>
            $"{(_useTls ? "https" : "http")}://{_pcHost}:{_sessionHubPort}";

        /// <summary>
        /// Unified WebRTC signaling endpoint (E24), served by the SessionHub HTTP
        /// app on the same host/port: <c>{base}/webrtc/offer</c>. The Android
        /// LiveTransportPlugin POSTs its SDP offer + session token here.
        /// </summary>
        public string WebrtcOfferUrl => $"{SessionHubBaseUrl}/webrtc/offer";
    }
}
