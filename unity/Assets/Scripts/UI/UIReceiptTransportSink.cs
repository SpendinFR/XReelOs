// MLOmega V19 — E25
// IReceiptSink that forwards UIReceipts over the E24 reliable DataChannel via
// LiveTransportBridge. Receipts feed delivery_adapter (E6) back into the BrainLive
// voie, so losing them silently would corrupt the feedback loop. Therefore this
// sink keeps a BOUNDED local FIFO: when the transport is down (Disconnected /
// Reconnecting) or a send fails, receipts are queued and flushed once the bridge
// reports Connected. The queue is capped (oldest dropped past the cap) so a long
// outage can never grow memory without bound — matching the reference's "borné,
// drop du plus ancien" rule for the whole live stack (ADR §E25).
//
// The buffer/flush logic lives in a pure ReceiptOutbox so it is unit-testable
// without a live transport; the MonoBehaviour just supplies the "try send now"
// delegate (the real DataChannel) and the reconnect trigger.
using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using MLOmega.XR.Transport;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Bounded FIFO of receipts with a flush-on-reconnect policy. Pure C# so tests
    /// drive it directly: <see cref="TrySend"/> is injected, letting a test simulate
    /// the transport being down (returns false → buffered) then up (returns true →
    /// flushed in order).
    /// </summary>
    public sealed class ReceiptOutbox
    {
        private readonly Queue<UIReceipt> _pending = new Queue<UIReceipt>();
        private readonly int _maxPending;

        /// <summary>Delivers a receipt now; returns true on success, false if the transport is down.</summary>
        public Func<UIReceipt, bool> TrySend { get; set; }

        public int PendingCount => _pending.Count;
        public int DroppedCount { get; private set; }

        public ReceiptOutbox(int maxPending, Func<UIReceipt, bool> trySend)
        {
            _maxPending = Mathf.Max(1, maxPending);
            TrySend = trySend;
        }

        /// <summary>Send or buffer a receipt, preserving FIFO order.</summary>
        public void Send(UIReceipt receipt)
        {
            if (receipt == null) return;

            // Preserve ordering: if anything is already queued, this goes behind it.
            if (_pending.Count > 0)
            {
                Enqueue(receipt);
                Flush();
                return;
            }
            if (!SafeSend(receipt))
            {
                Enqueue(receipt);
            }
        }

        /// <summary>Try to deliver everything queued, in order. Stops at the first failure.</summary>
        public void Flush()
        {
            while (_pending.Count > 0)
            {
                if (!SafeSend(_pending.Peek()))
                {
                    break; // still down; keep the remainder
                }
                _pending.Dequeue();
            }
        }

        private bool SafeSend(UIReceipt receipt)
        {
            if (TrySend == null) return false;
            try { return TrySend(receipt); }
            catch { return false; } // never throw on transport failure (IReceiptSink contract)
        }

        private void Enqueue(UIReceipt receipt)
        {
            _pending.Enqueue(receipt);
            while (_pending.Count > _maxPending)
            {
                _pending.Dequeue(); // drop the oldest — bounded buffer
                DroppedCount++;
            }
        }
    }

    public sealed class UIReceiptTransportSink : MonoBehaviour, IReceiptSink
    {
        [SerializeField] private LiveTransportBridge _bridge;

        [Tooltip("Max receipts buffered while the transport is down. Oldest dropped past this.")]
        [Min(1)]
        [SerializeField] private int _maxPending = 128;

        private ReceiptOutbox _outbox;

        /// <summary>Number of receipts currently buffered (test/telemetry visibility).</summary>
        public int PendingCount => _outbox?.PendingCount ?? 0;

        /// <summary>Total receipts dropped because the buffer was full (telemetry).</summary>
        public int DroppedCount => _outbox?.DroppedCount ?? 0;

        private void Awake()
        {
            if (_bridge == null) _bridge = FindAnyObjectByType<LiveTransportBridge>();
            _outbox = new ReceiptOutbox(_maxPending, TrySendOverBridge);
        }

        private void OnEnable()
        {
            if (_bridge != null) _bridge.StateChanged += OnTransportState;
        }

        private void OnDisable()
        {
            if (_bridge != null) _bridge.StateChanged -= OnTransportState;
        }

        /// <summary>Send a receipt. Never throws; buffers if the transport is down.</summary>
        public void Send(UIReceipt receipt)
        {
            EnsureOutbox();
            _outbox.Send(receipt);
        }

        /// <summary>Force a flush attempt (also called automatically on reconnect).</summary>
        public void Flush()
        {
            EnsureOutbox();
            _outbox.Flush();
        }

        private void OnTransportState(LiveTransportState state, string detail)
        {
            if (state == LiveTransportState.Connected) Flush();
        }

        private bool TrySendOverBridge(UIReceipt receipt)
        {
            if (_bridge == null) return false;
            if (_bridge.State != LiveTransportState.Connected) return false;
            return _bridge.SendReceipt(receipt);
        }

        private void EnsureOutbox() =>
            _outbox ??= new ReceiptOutbox(_maxPending, TrySendOverBridge);
    }
}
