using System.Collections.Generic;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Pure, dependency-free spatial math shared by product rendering and
    /// EditMode contract tests. It contains no SDK or device fallback.
    /// </summary>
    public static class XrealSpatialMath
    {
        public static List<Dictionary<string, object>> ForecastLinear(
            Vector3 origin,
            Vector3 velocity,
            float horizon,
            int steps)
        {
            var result = new List<Dictionary<string, object>>();
            int bounded = Mathf.Clamp(steps, 2, 12);
            float safeHorizon = Mathf.Clamp(horizon, 0.2f, 3f);
            for (int i = 0; i < bounded; i++)
            {
                float t = safeHorizon * i / (bounded - 1f);
                Vector3 point = origin + velocity * t;
                result.Add(new Dictionary<string, object>
                {
                    { "x", point.x },
                    { "y", point.y },
                    { "z", point.z },
                });
            }
            return result;
        }
    }
}
