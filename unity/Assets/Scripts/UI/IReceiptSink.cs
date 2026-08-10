// MLOmega V19 — E25
// Where UI components send their UIReceipts (displayed/seen/acted/dismissed/
// corrected, §13.3). The transport impl forwards them over the DataChannel (E24),
// where delivery_adapter (E6) reboucle them into v18_8_live_policy for the
// BrainLive voie. Tests use a capturing impl.
using MLOmega.Contracts.V19;

namespace MLOmega.XR.UI
{
    /// <summary>Sink for outbound <see cref="UIReceipt"/>s produced by UI components.</summary>
    public interface IReceiptSink
    {
        /// <summary>Send a receipt. Implementations must not throw on transport failure.</summary>
        void Send(UIReceipt receipt);
    }
}
