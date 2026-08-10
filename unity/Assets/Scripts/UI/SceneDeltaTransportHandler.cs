using System;
using MLOmega.Contracts.V19;
using MLOmega.XR.Scene;
using MLOmega.XR.Transport;
using Newtonsoft.Json;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>Applies raw PC scene_delta messages to entity and local-track caches.</summary>
    public sealed class SceneDeltaTransportHandler : MonoBehaviour
    {
        [SerializeField] private LiveTransportBridge _transport;
        [SerializeField] private SceneCache _sceneCache;
        [SerializeField] private LocalTrackStore _tracks;

        private void Awake()
        {
            if (_transport == null) _transport = FindAnyObjectByType<LiveTransportBridge>();
            if (_sceneCache == null) _sceneCache = FindAnyObjectByType<SceneCache>();
            if (_tracks == null) _tracks = FindAnyObjectByType<LocalTrackStore>();
        }

        private void OnEnable()
        {
            if (_transport != null) _transport.MessageReceived += OnMessage;
        }

        private void OnDisable()
        {
            if (_transport != null) _transport.MessageReceived -= OnMessage;
        }

        private void OnMessage(string json)
        {
            if (string.IsNullOrEmpty(json) || json.IndexOf("\"scene_delta\"", StringComparison.Ordinal) < 0)
                return;
            try
            {
                var delta = ContractJson.Deserialize<SceneDelta>(json);
                if (delta == null) return;
                _sceneCache?.SubmitSceneDelta(delta);
                _tracks?.SubmitSceneDelta(delta);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SceneDeltaTransport] bad json: " + ex.Message);
            }
        }
    }
}
