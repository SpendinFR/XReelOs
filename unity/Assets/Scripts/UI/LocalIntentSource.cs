// MLOmega V19 — E25
// IUIIntentSource for locally-produced intents: the future UltraLive UL0 skills
// (StableTrack, LensWindow, Subtitle, MotionProximity...) that run on-device with
// no PC round-trip, and the demo/scene-builder + EditMode tests. Skills call
// Emit(...) directly; nothing here talks to the network.
using System;
using MLOmega.Contracts.V19;
using UnityEngine;

namespace MLOmega.XR.UI
{
    public sealed class LocalIntentSource : MonoBehaviour, IUIIntentSource
    {
        public string SourceName => "local";
        public event Action<UIIntent> IntentProduced;

        /// <summary>Emit a fully-formed local intent to the broker.</summary>
        public void Emit(UIIntent intent)
        {
            if (intent != null) IntentProduced?.Invoke(intent);
        }
    }
}
