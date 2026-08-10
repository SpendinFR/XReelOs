using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Origin-independent verification for a set of persistent anchors imported
    /// into another Android package/session. Pair distances and relative rotations
    /// survive a legitimate XR-origin change; deformation does not.
    /// </summary>
    public static class WorldAnchorGeometryGuard
    {
        public readonly struct Sample
        {
            public readonly string Id;
            public readonly Vector3 ExpectedPosition;
            public readonly Quaternion ExpectedRotation;
            public readonly Vector3 ObservedPosition;
            public readonly Quaternion ObservedRotation;

            public Sample(
                string id,
                Vector3 expectedPosition,
                Quaternion expectedRotation,
                Vector3 observedPosition,
                Quaternion observedRotation)
            {
                Id = id ?? string.Empty;
                ExpectedPosition = expectedPosition;
                ExpectedRotation = expectedRotation;
                ObservedPosition = observedPosition;
                ObservedRotation = observedRotation;
            }
        }

        public static bool TryValidate(
            IReadOnlyList<Sample> samples,
            out string error)
        {
            error = string.Empty;
            if (samples == null || samples.Count < 2)
            {
                error = "insufficient_tracked_baseline";
                return false;
            }
            int verifiedPairs = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                Sample a = samples[i];
                if (!Finite(a)) return Invalid("non_finite_pose", out error);
                for (int j = i + 1; j < samples.Count; j++)
                {
                    Sample b = samples[j];
                    if (!Finite(b))
                        return Invalid("non_finite_pose", out error);
                    float expectedDistance = Vector3.Distance(
                        a.ExpectedPosition,
                        b.ExpectedPosition);
                    if (expectedDistance < .25f) continue;
                    verifiedPairs++;
                    float actualDistance = Vector3.Distance(
                        a.ObservedPosition,
                        b.ObservedPosition);
                    float tolerance =
                        Mathf.Max(.12f, expectedDistance * .05f);
                    if (Mathf.Abs(actualDistance - expectedDistance) > tolerance)
                        return Invalid("distance_drift", out error);
                    Quaternion expectedRelative =
                        Quaternion.Inverse(a.ExpectedRotation) *
                        b.ExpectedRotation;
                    Quaternion observedRelative =
                        Quaternion.Inverse(a.ObservedRotation) *
                        b.ObservedRotation;
                    if (
                        Quaternion.Angle(
                            expectedRelative,
                            observedRelative) > 12f)
                        return Invalid("rotation_drift", out error);
                }
            }
            if (verifiedPairs == 0)
                return Invalid("baseline_too_short", out error);
            return true;
        }

        private static bool Finite(Sample sample) =>
            Finite(sample.ExpectedPosition) &&
            Finite(sample.ObservedPosition) &&
            Finite(sample.ExpectedRotation) &&
            Finite(sample.ObservedRotation);

        private static bool Finite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool Finite(Quaternion value) =>
            IsFinite(value.x) && IsFinite(value.y) &&
            IsFinite(value.z) && IsFinite(value.w);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool Invalid(string value, out string error)
        {
            error = value;
            return false;
        }
    }
}
