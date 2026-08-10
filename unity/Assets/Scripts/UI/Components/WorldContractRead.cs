using System;
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Strict readers shared by 3D-only World Canvas effects. They never project
    /// screen pixels into space and reject uncalibrated or non-finite geometry.
    /// </summary>
    internal static class WorldContractRead
    {
        public static bool TrackingGate(
            UIIntent intent,
            bool requireDepth,
            float minimumQuality,
            out float quality,
            out string error)
        {
            quality = float.NaN;
            error = "invalid_world_contract";
            if (intent?.Content == null || intent.Anchor == null)
                return false;
            if (!string.Equals(
                    IntentRead.Anchor(intent, "coordinate_space", ""),
                    "tracking_local",
                    StringComparison.Ordinal))
            {
                error = "unsupported_coordinate_space";
                return false;
            }
            if (
                !IntentRead.Flag(intent.Content, "pose_valid") ||
                string.IsNullOrWhiteSpace(IntentRead.Content(
                    intent, "calibration_id", "")))
            {
                error = "unproven_tracking_calibration";
                return false;
            }
            if (requireDepth && !IntentRead.Flag(intent.Content, "depth_valid"))
            {
                error = "depth_required";
                return false;
            }
            quality = (float)IntentRead.Num(
                intent.Content, "spatial_quality", double.NaN);
            if (!Finite(quality) || quality < minimumQuality || quality > 1f)
            {
                error = "spatial_quality_below_threshold";
                return false;
            }
            if (intent.EvidenceRefs == null || intent.EvidenceRefs.Count == 0)
            {
                error = "spatial_evidence_missing";
                return false;
            }
            error = null;
            return true;
        }

        public static bool TryVector(object raw, out Vector3 vector)
        {
            vector = default;
            Dictionary<string, object> fields =
                raw as Dictionary<string, object>;
            if (fields == null && raw is JObject obj)
                fields = obj.ToObject<Dictionary<string, object>>();
            if (fields == null) return false;
            float x = (float)IntentRead.Num(fields, "x", double.NaN);
            float y = (float)IntentRead.Num(fields, "y", double.NaN);
            float z = (float)IntentRead.Num(fields, "z", double.NaN);
            if (!Finite(x) || !Finite(y) || !Finite(z)) return false;
            vector = new Vector3(x, y, z);
            return vector.sqrMagnitude <= 10000f;
        }

        public static bool TryVectorList(
            object raw,
            int minimum,
            int maximum,
            out List<Vector3> points)
        {
            points = new List<Vector3>();
            JArray array;
            try { array = raw as JArray ?? JArray.FromObject(raw); }
            catch { return false; }
            if (array.Count < minimum || array.Count > maximum) return false;
            foreach (JToken token in array)
            {
                if (!TryVector(token, out Vector3 point)) return false;
                if (
                    points.Count > 0 &&
                    Vector3.Distance(points[points.Count - 1], point) > 25f)
                    return false;
                points.Add(point);
            }
            return true;
        }

        public static bool TryArray(object raw, out JArray array)
        {
            array = null;
            if (raw == null) return false;
            try { array = raw as JArray ?? JArray.FromObject(raw); }
            catch { return false; }
            return array != null;
        }

        public static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
