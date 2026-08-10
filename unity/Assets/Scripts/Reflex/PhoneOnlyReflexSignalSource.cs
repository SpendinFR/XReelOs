// MLOmega V19 — E60
// Production source for the two baseline signals that cannot be discovered while
// their detector is asleep: speech/wake words need ASR, and a hand/palm needs the
// gesture graph. Environmental skills remain signal/budget driven in the scheduler.
using MLOmega.XR.Core;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    public sealed class PhoneOnlyReflexSignalSource : MonoBehaviour
    {
        [SerializeField] private ReflexScheduler _scheduler;
        [SerializeField] private XrSessionController _session;

        public bool IsEmitting => _session != null && _session.State == XrSessionState.Running;

        private void Awake()
        {
            if (_scheduler == null) _scheduler = FindAnyObjectByType<ReflexScheduler>();
            if (_session == null) _session = FindAnyObjectByType<XrSessionController>();
        }

        private void Update()
        {
            if (!IsEmitting || _scheduler == null) return;
            EmitBaselineSignals();
        }

        public void EmitBaselineSignals()
        {
            if (_scheduler == null) return;
            _scheduler.RaiseSignal(ReflexSignal.ContinuousSpeech);
            _scheduler.RaiseSignal(ReflexSignal.ContinuousGestures);
        }

        internal void ConfigureForTest(ReflexScheduler scheduler, XrSessionController session)
        {
            _scheduler = scheduler;
            _session = session;
        }
    }
}
