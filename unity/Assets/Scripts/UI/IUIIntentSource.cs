// MLOmega V19 — E25
// A source of UIIntents feeding the UIIntentBroker. The broker arbitrates ALL
// sources through one interface, so BrainLive (via transport), UltraLive skills
// (future, local) and tests all present identically.
using System;
using MLOmega.Contracts.V19;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Anything that produces <see cref="UIIntent"/>s. Implementations raise
    /// <see cref="IntentProduced"/> on the Unity main thread (the transport source
    /// marshals off the DataChannel thread before raising it).
    /// </summary>
    public interface IUIIntentSource
    {
        /// <summary>Stable, human-readable id for logs and metrics (e.g. "transport", "local").</summary>
        string SourceName { get; }

        /// <summary>Raised when a new intent is available for arbitration.</summary>
        event Action<UIIntent> IntentProduced;
    }
}
