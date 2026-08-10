// MLOmega V19 â€” E23
// HTTP client for the PC SessionHub (services/live-pc/sessionhub.py).
//
// sessionhub.py is a plain in-process class today (SessionHub); the live-pc HTTP
// server that fronts it is wired in E24. This client speaks the natural HTTP
// mapping of its methods, so the E24 server only has to expose them 1:1:
//
//   POST {base}/session/create      {device_id}
//        -> {session_id, token, created_at_utc}                 (SessionHub.create_session)
//   POST {base}/session/clock-sync  {session_id, token, client_send_ns}
//        -> {server_recv_ns, server_send_ns}                    (begin/complete_clock_sync)
//   POST {base}/session/renew       {session_id, token}
//        -> {token, created_at_utc}                             (re-issue ephemeral token)
//
// The offset/RTT arithmetic stays on the client (ClockSync.ComputeSample) and is
// byte-identical to SessionHub.complete_clock_sync, so no server round-trip is
// needed to obtain the offset â€” only the two server monotonic stamps.
using System;
using System.Collections;
using System.Text;
using MLOmega.Contracts.V19;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MLOmega.XR.Core
{
    /// <summary>Result of a create/renew call.</summary>
    public readonly struct SessionCredentials
    {
        public readonly string SessionId;
        public readonly string Token;
        public readonly string CreatedAtUtc;

        public SessionCredentials(string sessionId, string token, string createdAtUtc)
        {
            SessionId = sessionId;
            Token = token;
            CreatedAtUtc = createdAtUtc;
        }
    }

    /// <summary>
    /// Thin UnityWebRequest wrapper. All methods are coroutines and never throw;
    /// they report success/failure through callbacks so callers can drive their own
    /// retry / state machine.
    /// </summary>
    public sealed class SessionHubClient
    {
        private readonly string _baseUrl;
        private readonly float _timeoutSeconds;

        public SessionHubClient(string baseUrl, float timeoutSeconds)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
            _timeoutSeconds = Mathf.Max(1f, timeoutSeconds);
        }

        public IEnumerator CreateSession(string deviceId,
            Action<SessionCredentials> onOk, Action<string> onError)
        {
            string body = ContractJson.Serialize(new { device_id = deviceId });
            yield return Post("/session/create", body,
                json =>
                {
                    var r = ContractJson.Deserialize<CredentialsDto>(json);
                    if (r == null || string.IsNullOrEmpty(r.session_id) || string.IsNullOrEmpty(r.token))
                    {
                        onError?.Invoke("malformed create response");
                        return;
                    }
                    onOk?.Invoke(new SessionCredentials(r.session_id, r.token, r.created_at_utc));
                },
                onError);
        }

        public IEnumerator RenewToken(string sessionId, string token,
            Action<SessionCredentials> onOk, Action<string> onError)
        {
            string body = ContractJson.Serialize(new { session_id = sessionId, token });
            yield return Post("/session/renew", body,
                json =>
                {
                    var r = ContractJson.Deserialize<CredentialsDto>(json);
                    if (r == null || string.IsNullOrEmpty(r.token))
                    {
                        onError?.Invoke("malformed renew response");
                        return;
                    }
                    // renew keeps the same session id
                    onOk?.Invoke(new SessionCredentials(sessionId, r.token, r.created_at_utc));
                },
                onError);
        }

        public IEnumerator ClockSync(string sessionId, string token, long clientSendNs,
            Action<ClockExchangeReply> onOk, Action<string> onError)
        {
            string body = ContractJson.Serialize(new
            {
                session_id = sessionId,
                token,
                client_send_ns = clientSendNs
            });
            yield return Post("/session/clock-sync", body,
                json =>
                {
                    var r = ContractJson.Deserialize<ClockDto>(json);
                    if (r == null)
                    {
                        onError?.Invoke("malformed clock-sync response");
                        return;
                    }
                    // Server may collapse recv/send into one stamp (as sessionhub.py
                    // does when server_send_ns is None); default send := recv.
                    long serverSend = r.server_send_ns != 0 ? r.server_send_ns : r.server_recv_ns;
                    onOk?.Invoke(new ClockExchangeReply(r.server_recv_ns, serverSend));
                },
                onError);
        }

        private IEnumerator Post(string path, string jsonBody,
            Action<string> onOk, Action<string> onError)
        {
            string url = _baseUrl + path;
            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            byte[] payload = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(payload);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = Mathf.CeilToInt(_timeoutSeconds);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"{path}: {req.result} ({req.responseCode}) {req.error}");
                yield break;
            }

            string text = req.downloadHandler.text;
            try
            {
                onOk?.Invoke(text);
            }
            catch (Exception ex)
            {
                onError?.Invoke($"{path}: parse error {ex.Message}");
            }
        }

        // DTOs use snake_case fields to match the SessionHub JSON directly.
        [Serializable]
        private sealed class CredentialsDto
        {
            public string session_id;
            public string token;
            public string created_at_utc;
        }

        [Serializable]
        private sealed class ClockDto
        {
            public long server_recv_ns;
            public long server_send_ns;
        }
    }

    /// <summary>
    /// Adapts <see cref="SessionHubClient"/> to <see cref="IClockSyncTransport"/>
    /// so <see cref="ClockSync"/> can drive real HTTP round-trips.
    /// </summary>
    public sealed class HttpClockSyncTransport : IClockSyncTransport
    {
        private readonly SessionHubClient _client;

        public HttpClockSyncTransport(SessionHubClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public IEnumerator Exchange(string sessionId, string token, long clientSendNs,
            Action<ClockExchangeReply> onReply, Action<string> onError)
        {
            return _client.ClockSync(sessionId, token, clientSendNs, onReply, onError);
        }
    }
}
