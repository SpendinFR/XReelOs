using System;
using System.Collections;
using System.Text;
using MLOmega.XR.Core;
using MLOmega.Contracts.V19;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace MLOmega.XR.Transport
{
    /// <summary>Coordinates the real Android phone-only capture/transport lifecycle.</summary>
    public sealed class PhoneOnlySessionCoordinator : MonoBehaviour
    {
        [SerializeField] private SessionPairing _pairing;
        [SerializeField] private LiveTransportBridge _transport;
        [SerializeField] private XrSessionController _session;
        [Tooltip("Show a small maintenance button. Disable when another menu calls EndSessionAndCloseDay().")]
        [SerializeField] private bool _showMaintenanceButton = true;

        public bool EndRequested { get; private set; }
        public string EndStatus { get; private set; }
        public bool ConnectedConfirmed { get; private set; }
        private int _previousSleepTimeout;
        private bool _sleepLockHeld;

        private void Awake()
        {
            if (_pairing == null) _pairing = FindAnyObjectByType<SessionPairing>();
            if (_transport == null) _transport = FindAnyObjectByType<LiveTransportBridge>();
            if (_session == null) _session = FindAnyObjectByType<XrSessionController>();
        }

        private void OnEnable()
        {
            if (_pairing != null) _pairing.StateChanged += OnPairingStateChanged;
            if (_session != null) _session.SessionStateChanged += OnSessionStateChanged;
            if (_transport != null) _transport.StateChanged += OnTransportStateChanged;
            Application.runInBackground = true;
            UpdateSleepPolicy(_session != null ? _session.State : XrSessionState.Idle);
            TryStartTransport();
        }

        private void OnDisable()
        {
            if (_pairing != null) _pairing.StateChanged -= OnPairingStateChanged;
            if (_session != null) _session.SessionStateChanged -= OnSessionStateChanged;
            if (_transport != null) _transport.StateChanged -= OnTransportStateChanged;
            ReleaseSleepLock();
            // Lifecycle loss is not an explicit end: never call /session/end here.
        }

        private void OnPairingStateChanged(PairingState state)
        {
            if (state == PairingState.Paired) TryStartTransport();
        }

        private void OnSessionStateChanged(XrSessionState state)
        {
            UpdateSleepPolicy(state);
            if (state == XrSessionState.Running) TryStartTransport();
        }

        private void OnTransportStateChanged(LiveTransportState state, string detail)
        {
            ConnectedConfirmed = state == LiveTransportState.Connected || state == LiveTransportState.Degraded;
            EndStatus = EndRequested ? EndStatus : $"transport:{state} {detail}";
        }

        private void TryStartTransport()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!EndRequested && _pairing != null && _pairing.State == PairingState.Paired &&
                _session != null && _session.Adapter != null)
            {
                if (_session.State == XrSessionState.Running && _session.Adapter.IsEyeActive)
                    _transport?.StartTransport();
            }
#endif
        }

        public void EndSessionAndCloseDay()
        {
            if (EndRequested || _pairing == null || !_pairing.TryGetActiveSession(out var sid, out var token))
                return;
            string baseUrl = _pairing.ActiveBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                EndStatus = "PC unreachable: reconnect before ending the session";
                Debug.LogWarning("[PhoneOnly] " + EndStatus);
                return;
            }
            EndRequested = true;
            StartCoroutine(EndExplicitly(baseUrl, sid, token));
        }

        private IEnumerator EndExplicitly(string baseUrl, string sessionId, string token)
        {
            EndStatus = "requesting";
            string url = baseUrl.TrimEnd('/') + "/session/end";
            byte[] body = Encoding.UTF8.GetBytes(ContractJson.Serialize(new { session_id = sessionId, token }));
            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            var operation = req.SendWebRequest();
            // The authenticated request is now in flight; stop capture/audio so
            // the PC's bounded drain can finish before end_session + CloseDay.
            yield return null;
            _transport?.StopTransport();
            _session?.StopSession();
            yield return operation;
            if (req.result == UnityWebRequest.Result.Success)
            {
                EndStatus = req.downloadHandler.text;
                yield return PollCloseDay(baseUrl, sessionId);
            }
            else
            {
                EndStatus = $"error {req.responseCode}: {req.error}";
                EndRequested = false; // authenticated request can be retried explicitly
                Debug.LogError("[PhoneOnly] " + EndStatus);
            }
        }

        private IEnumerator PollCloseDay(string baseUrl, string sessionId)
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(2f);
                if (_pairing == null || string.IsNullOrEmpty(_pairing.Token)) yield break;
                string url = baseUrl.TrimEnd('/') + "/session/status";
                byte[] body = Encoding.UTF8.GetBytes(ContractJson.Serialize(new
                {
                    session_id = sessionId,
                    token = _pairing.Token
                }));
                using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    EndStatus = $"status retry: {req.responseCode} {req.error}";
                    continue;
                }
                EndStatus = req.downloadHandler.text;
                var status = ContractJson.ParseObject(EndStatus).Value<string>("close_day");
                if (status == "completed")
                {
                    _pairing.ClearPersistedSession();
                    Debug.Log("[PhoneOnly] CloseDay completed: " + EndStatus);
                    yield break;
                }
                if (status == "error" || status == "blocked")
                {
                    EndRequested = false; // expose the button for an explicit retry
                    Debug.LogError("[PhoneOnly] CloseDay requires retry: " + EndStatus);
                    yield break;
                }
            }
        }

        private void UpdateSleepPolicy(XrSessionState state)
        {
            bool active = state == XrSessionState.Running || state == XrSessionState.Suspended;
            if (active && !_sleepLockHeld)
            {
                _previousSleepTimeout = Screen.sleepTimeout;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                _sleepLockHeld = true;
            }
            else if (!active)
            {
                ReleaseSleepLock();
            }
        }

        private void ReleaseSleepLock()
        {
            if (!_sleepLockHeld) return;
            Screen.sleepTimeout = _previousSleepTimeout;
            _sleepLockHeld = false;
        }

        private void OnGUI()
        {
            if (!_showMaintenanceButton || EndRequested) return;
            if (GUI.Button(new Rect(16, Screen.height - 64, 300, 48), "Terminer la session et lancer CloseDay"))
                EndSessionAndCloseDay();
        }
    }
}
