// MLOmega V19 — E25
// Tiny typed readers over the loosely-typed contract dictionaries (UIIntent.content
// / anchor / ui_hint are Dictionary<string,object> because they come from JSON).
// Centralised so every component parses them identically and no component invents
// its own casting rules. Mirrors the same helpers SceneCache uses internally.
using System.Collections.Generic;
using System.Globalization;
using MLOmega.Contracts.V19;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    public static class IntentRead
    {
        public static string Str(Dictionary<string, object> d, string key, string fallback = null)
        {
            if (d != null && d.TryGetValue(key, out object v) && v != null)
            {
                return v as string ?? v.ToString();
            }
            return fallback;
        }

        public static string Content(UIIntent intent, string key, string fallback = null) =>
            Str(intent?.Content, key, fallback);

        public static string Anchor(UIIntent intent, string key, string fallback = null) =>
            Str(intent?.Anchor, key, fallback);

        public static string Hint(UIIntent intent, string key, string fallback = null) =>
            Str(intent?.UiHint, key, fallback);

        public static double Num(Dictionary<string, object> d, string key, double fallback)
        {
            if (d != null && d.TryGetValue(key, out object v) && v != null)
            {
                switch (v)
                {
                    case double dv: return dv;
                    case float fv: return fv;
                    case long lv: return lv;
                    case int iv: return iv;
                    default:
                        if (double.TryParse(v.ToString(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double p)) return p;
                        break;
                }
            }
            return fallback;
        }

        public static bool Flag(Dictionary<string, object> d, string key, bool fallback = false)
        {
            if (d != null && d.TryGetValue(key, out object v) && v != null)
            {
                if (v is bool b) return b;
                if (bool.TryParse(v.ToString(), out bool p)) return p;
            }
            return fallback;
        }

        /// <summary>Read a 2D screen point [x,y] (0..1 normalised) from an anchor/content list.</summary>
        public static bool TryPoint(Dictionary<string, object> d, string key, out Vector2 point)
        {
            point = Vector2.zero;
            if (d == null || !d.TryGetValue(key, out object v) || v == null) return false;
            if (v is IList<object> list && list.Count >= 2)
            {
                point = new Vector2(ToFloat(list[0]), ToFloat(list[1]));
                return true;
            }
            // Values received from Newtonsoft through Dictionary<string, object>
            // are JArray, whereas locally-created Reflex intents use List<object>.
            if (v is JArray array && array.Count >= 2)
            {
                point = new Vector2(ToFloat(array[0]), ToFloat(array[1]));
                return true;
            }
            return false;
        }

        /// <summary>Read a normalised {x,y,w,h} rectangle from a contract dictionary.</summary>
        public static bool TryRect(Dictionary<string, object> d, string key, out Rect rect)
        {
            rect = default;
            if (d == null || !d.TryGetValue(key, out object value) || value == null)
                return false;

            Dictionary<string, object> box = value as Dictionary<string, object>;
            if (box == null && value is JObject obj)
                box = obj.ToObject<Dictionary<string, object>>();
            if (box == null) return false;

            float x = (float)Num(box, "x", double.NaN);
            float y = (float)Num(box, "y", double.NaN);
            float w = (float)Num(box, "w", Num(box, "width", double.NaN));
            float h = (float)Num(box, "h", Num(box, "height", double.NaN));
            if (float.IsNaN(x) || float.IsInfinity(x) ||
                float.IsNaN(y) || float.IsInfinity(y) ||
                float.IsNaN(w) || float.IsInfinity(w) ||
                float.IsNaN(h) || float.IsInfinity(h) ||
                w <= 0f || h <= 0f)
                return false;

            x = Mathf.Clamp01(x);
            y = Mathf.Clamp01(y);
            w = Mathf.Clamp(w, 0f, 1f - x);
            h = Mathf.Clamp(h, 0f, 1f - y);
            if (w <= 0f || h <= 0f) return false;
            rect = new Rect(x, y, w, h);
            return true;
        }

        private static float ToFloat(object o)
        {
            if (o == null) return 0f;
            if (double.TryParse(o.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return (float)d;
            return 0f;
        }
    }
}
