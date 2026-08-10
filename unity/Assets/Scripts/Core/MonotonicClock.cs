// MLOmega V19 — E23
// One shared monotonic nanosecond clock. EyeCaptureSource, PosePublisher and
// ClockSync must timestamp against the SAME origin so frame_id timestamps, pose
// samples and the clock-sync offset all live on one client timeline. Backed by
// System.Diagnostics.Stopwatch (high-resolution, monotonic on all Unity targets).
using System.Diagnostics;

namespace MLOmega.XR.Core
{
    /// <summary>Monotonic nanosecond source; injectable so tests are deterministic.</summary>
    public interface IMonotonicClock
    {
        long NowNs();
    }

    /// <summary>Stopwatch-backed monotonic clock. Never goes backwards.</summary>
    public sealed class StopwatchMonotonicClock : IMonotonicClock
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        // Precomputed to avoid a divide per call; nanoseconds per tick.
        private static readonly double NsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

        public long NowNs() => (long)(_sw.ElapsedTicks * NsPerTick);
    }

    /// <summary>
    /// Deterministic clock for EditMode tests: advances only when told. Lets tests
    /// feed exact client_send/client_recv stamps into ClockSync.
    /// </summary>
    public sealed class ManualMonotonicClock : IMonotonicClock
    {
        private long _nowNs;

        public ManualMonotonicClock(long startNs = 0)
        {
            _nowNs = startNs;
        }

        public long NowNs() => _nowNs;

        public void Set(long ns) => _nowNs = ns;

        public void Advance(long deltaNs) => _nowNs += deltaNs;
    }
}
