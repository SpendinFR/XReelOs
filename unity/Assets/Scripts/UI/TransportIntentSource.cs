// MLOmega V19 — E25
// IUIIntentSource backed by the E24 LiveTransportBridge: every UIIntent that
// arrives down the reliable DataChannel (BrainLive via delivery_adapter, VisionRT
// SceneDelta-driven results) is re-emitted to the broker. The bridge already
// marshals native callbacks onto the Unity main thread, so IntentProduced is
// raised there and the broker can consume it directly.
using System;
using MLOmega.Contracts.V19;
using MLOmega.XR.Transport;
using UnityEngine;

namespace MLOmega.XR.UI
{
    public sealed class TransportIntentSource : MonoBehaviour, IUIIntentSource
    {
        [SerializeField] private LiveTransportBridge _bridge;

        public string SourceName => "transport";
        public event Action<UIIntent> IntentProduced;

        private void Awake()
        {
            if (_bridge == null) _bridge = FindAnyObjectByType<LiveTransportBridge>();
        }

        private void OnEnable()
        {
            if (_bridge != null) _bridge.UiIntentReceived += Forward;
        }

        private void OnDisable()
        {
            if (_bridge != null) _bridge.UiIntentReceived -= Forward;
        }

        private void Forward(UIIntent intent) => IntentProduced?.Invoke(intent);
    }
}
