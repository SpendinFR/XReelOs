// MLOmega V19 — E25
// Registers an IUIIntentSource MonoBehaviour with the UIIntentBroker at start.
// RegisterSource is a runtime call (it subscribes to the source's event), so the
// scene builder cannot wire it as a serialized reference; this tiny bootstrap does
// it in Awake. Used by the E25 demo scene and reusable by the real app to attach
// its LocalIntentSource / TransportIntentSource to the broker.
using UnityEngine;

namespace MLOmega.XR.UI
{
    public sealed class E25SourceBootstrap : MonoBehaviour
    {
        [SerializeField] private UIIntentBroker _broker;
        [SerializeField] private MonoBehaviour _source; // must implement IUIIntentSource

        private void Awake()
        {
            if (_broker == null) _broker = FindAnyObjectByType<UIIntentBroker>();
            if (_broker != null && _source is IUIIntentSource src)
            {
                _broker.RegisterSource(src);
            }
        }
    }
}
