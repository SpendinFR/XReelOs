// MLOmega V19 — E23
// Client half of the SessionHub clock-sync protocol (services/live-pc/sessionhub.py).
//
// Protocol (mirrors SessionHub.begin_clock_sync / complete_clock_sync):
//   1. client reads t0 = client monotonic ns                       (client_send_ns)
//   2. client asks the hub to stamp the exchange; the hub returns
//        server_recv_ns  (SessionHub.begin_clock_sync, time.monotonic_ns on the PC)
//        server_send_ns  (same instant unless the hub separates them)
//   3. client reads t3 = client monotonic ns on reply              (client_recv_ns)
//   4. rtt/offset are computed with the EXACT formulas the server uses:
//        rtt    = (client_recv - client_send) - (server_send - server_recv)
//        offset = ((server_recv - client_send) + (server_send - client_recv)) / 2   (floor div)
//   The best (lowest-RTT) sample of a burst wins, identically to
//   SessionHub.current_offset_ns. offset is "server_monotonic - client_monotonic":
//   add it to a client monotonic timestamp to express it on the PC clock.
//
// Transport is abstracted (IClockSyncTransport). HttpClockSyncTransport is the
// concrete mapping onto the SessionHub HTTP surface the E24 server exposes; tests
// inject a deterministic transport to prove the math matches test_sessionhub.py.
using System;
using System.Collections;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>One completed clock-sync round-trip, with derived offset/rtt.</summary>
    public readonly struct ClockSample
    {
        public readonly long ClientSendNs;
        public readonly long ServerRecvNs;
        public readonly long ServerSendNs;
        public readonly long ClientRecvNs;
        public readonly long OffsetNs;
        public readonly long RttNs;

        public ClockSample(long clientSendNs, long serverRecvNs, long serverSendNs,
            long clientRecvNs, long offsetNs, long rttNs)
        {
            ClientSendNs = clientSendNs;
            ServerRecvNs = serverRecvNs;
            ServerSendNs = serverSendNs;
            ClientRecvNs = clientRecvNs;
            OffsetNs = offsetNs;
            RttNs = rttNs;
        }
    }

    /// <summary>The two server monotonic stamps returned for one exchange.</summary>
    public readonly struct ClockExchangeReply
    {
        public readonly long ServerRecvNs;
        public readonly long ServerSendNs;

        public ClockExchangeReply(long serverRecvNs, long serverSendNs)
        {
            ServerRecvNs = serverRecvNs;
            ServerSendNs = serverSendNs;
        }
    }

    /// <summary>
    /// Performs one clock-sync exchange. <paramref name="clientSendNs"/> is the
    /// client monotonic stamp to relay; the transport must invoke exactly one of
    /// the callbacks. Implementations must not throw; report failure via onError.
    /// </summary>
    public interface IClockSyncTransport
    {
        IEnumerator Exchange(
            string sessionId,
            string token,
            long clientSendNs,
            Action<ClockExchangeReply> onReply,
            Action<string> onError);
    }

    /// <summary>
    /// Coroutine-driven clock-sync client. Runs bursts of round-trips, keeps the
    /// current best offset, re-measures periodically, and exposes a bounded-retry
    /// "not synchronised" state on repeated network failure.
    /// </summary>
    public sealed class ClockSync
    {
        public enum SyncState
        {
            NeverSynced = 0,
            Synced = 1,
            Unsynced = 2 // was synced or attempted, last burst failed / stale
        }

        private readonly IClockSyncTransport _transport;
        private readonly IMonotonicClock _clock;
        private readonly int _samplesPerBurst;
        private readonly int _maxRetries;

        private bool _hasOffset;
        private long _currentOffsetNs;
        private long _currentRttNs = long.MaxValue;
        private ClockSample _lastSample;

        public SyncState State { get; private set; } = SyncState.NeverSynced;
        public string LastError { get; private set; }

        /// <summary>True once at least one successful burst has produced an offset.</summary>
        public bool HasOffset => _hasOffset;

        /// <summary>Best offset in ns (server_monotonic - client_monotonic).</summary>
        public long CurrentOffsetNs => _currentOffsetNs;

        /// <summary>RTT of the sample the current offset came from.</summary>
        public long CurrentRttNs => _currentRttNs;

        public ClockSample LastSample => _lastSample;

        public ClockSync(IClockSyncTransport transport, IMonotonicClock clock,
            int samplesPerBurst, int maxRetries)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _samplesPerBurst = Mathf.Max(1, samplesPerBurst);
            _maxRetries = Mathf.Max(0, maxRetries);
        }

        /// <summary>
        /// Pure offset/rtt computation — identical to SessionHub.complete_clock_sync.
        /// Exposed static so EditMode tests can assert numeric symmetry with
        /// tests/v19/test_sessionhub.py without any transport.
        /// </summary>
        public static ClockSample ComputeSample(
            long clientSendNs, long serverRecvNs, long serverSendNs, long clientRecvNs)
        {
            long rtt = (clientRecvNs - clientSendNs) - (serverSendNs - serverRecvNs);
            long offset = FloorDiv2(
                (serverRecvNs - clientSendNs) + (serverSendNs - clientRecvNs));
            return new ClockSample(clientSendNs, serverRecvNs, serverSendNs,
                clientRecvNs, offset, rtt);
        }

        /// <summary>
        /// Floor division by 2, matching Python's <c>// 2</c> (rounds toward
        /// negative infinity), so negative offsets agree bit-for-bit with the server.
        /// </summary>
        private static long FloorDiv2(long value)
        {
            long q = value / 2;
            if ((value % 2 != 0) && ((value < 0) != (2 < 0)))
            {
                q -= 1;
            }
            return q;
        }

        /// <summary>
        /// Run one burst of up to <c>samplesPerBurst</c> exchanges and adopt the
        /// lowest-RTT sample's offset. Bounded retries per exchange; if the whole
        /// burst yields nothing the state becomes <see cref="SyncState.Unsynced"/>.
        /// </summary>
        public IEnumerator RunBurst(string sessionId, string token)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                State = SyncState.Unsynced;
                LastError = "no session id";
                yield break;
            }

            ClockSample best = default;
            bool gotAny = false;

            for (int i = 0; i < _samplesPerBurst; i++)
            {
                bool exchangeDone = false;
                bool exchangeOk = false;
                ClockExchangeReply reply = default;
                long clientSendNs = 0;

                for (int attempt = 0; attempt <= _maxRetries && !exchangeOk; attempt++)
                {
                    exchangeDone = false;
                    clientSendNs = _clock.NowNs();
                    yield return _transport.Exchange(
                        sessionId, token, clientSendNs,
                        r => { reply = r; exchangeOk = true; exchangeDone = true; },
                        err => { LastError = err; exchangeDone = true; });

                    // Guard against a transport that forgets to call back.
                    if (!exchangeDone)
                    {
                        LastError = "transport did not complete";
                    }
                }

                if (!exchangeOk)
                {
                    continue;
                }

                long clientRecvNs = _clock.NowNs();
                ClockSample sample = ComputeSample(
                    clientSendNs, reply.ServerRecvNs, reply.ServerSendNs, clientRecvNs);
                _lastSample = sample;

                if (!gotAny || sample.RttNs < best.RttNs)
                {
                    best = sample;
                    gotAny = true;
                }
            }

            if (gotAny)
            {
                _currentOffsetNs = best.OffsetNs;
                _currentRttNs = best.RttNs;
                _hasOffset = true;
                State = SyncState.Synced;
                LastError = null;
            }
            else
            {
                State = SyncState.Unsynced;
            }
        }

        /// <summary>Convert a client monotonic ns stamp to the PC's clock.</summary>
        public long ToServerNs(long clientMonotonicNs) =>
            clientMonotonicNs + _currentOffsetNs;
    }
}
