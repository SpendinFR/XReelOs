// MLOmega V19 — E34 §5 / E35 §4
// EntityHotUpdate + generalised hot-context messages the PC scene adapter pushes.
// E34 wired ONLY the person relation-pack prefetch (entity_hot_update). E35 §4
// generalises the mechanism to every hot sub-cache (GUIDE §9.1): a durable OBJECT
// (entity_hot_update kind=object), a recognised ZONE (spatial_hot_update, with
// last-seens + matching daily routines), and an active TASK (task_hot_update).
// All are live-only DataChannel messages (not generated schema contracts), so they
// live in the Scene assembly next to SceneCache. Additive: E34 stays untouched.
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MLOmega.XR.Scene
{
    /// <summary>PC-&gt;device entity hot update: a person relation pack (E34 §5) OR a
    /// durable object (E35 §4b, <c>kind=object</c>).</summary>
    public sealed class EntityHotUpdate
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("entity_id")] public string EntityId { get; set; }
        [JsonProperty("person_id")] public string PersonId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("label")] public string Label { get; set; }
        [JsonProperty("last_seen")] public string LastSeen { get; set; }
        [JsonProperty("confidence")] public double Confidence { get; set; }
        [JsonProperty("relation_pack")] public List<Dictionary<string, object>> RelationPack { get; set; }
        [JsonProperty("relations")] public List<Dictionary<string, object>> Relations { get; set; }
        [JsonProperty("as_of")] public string AsOf { get; set; }

        /// <summary>True when this update is for a durable object rather than a person.</summary>
        public bool IsObject => string.Equals(Kind, "object", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>True when a raw DataChannel message is an entity_hot_update.</summary>
        public static bool IsEntityHotUpdate(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            return json.IndexOf("\"entity_hot_update\"", System.StringComparison.Ordinal) >= 0;
        }
    }

    /// <summary>PC-&gt;device spatial hot update (E35 §4a): the recognised session
    /// zone + measured map_quality + a few useful last-seens + matching daily
    /// routines ("ici, d'habitude tu…").</summary>
    public sealed class SpatialHotUpdate
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("zone")] public string Zone { get; set; }
        [JsonProperty("place_hint")] public string PlaceHint { get; set; }
        [JsonProperty("map_quality")] public double MapQuality { get; set; }
        [JsonProperty("last_seens")] public List<Dictionary<string, object>> LastSeens { get; set; }
        [JsonProperty("routines")] public List<Dictionary<string, object>> Routines { get; set; }
        [JsonProperty("as_of")] public string AsOf { get; set; }

        public static bool IsSpatialHotUpdate(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            return json.IndexOf("\"spatial_hot_update\"", System.StringComparison.Ordinal) >= 0;
        }
    }

    /// <summary>PC-&gt;device task hot update (E35 §4c): the active task/situation —
    /// goal, current step, tools — for the single task_hot slot (§9.1).</summary>
    public sealed class TaskHotUpdate
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("task_key")] public string TaskKey { get; set; }
        [JsonProperty("goal")] public string Goal { get; set; }
        [JsonProperty("step")] public string Step { get; set; }
        [JsonProperty("tools")] public List<string> Tools { get; set; }
        [JsonProperty("as_of")] public string AsOf { get; set; }

        public static bool IsTaskHotUpdate(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            return json.IndexOf("\"task_hot_update\"", System.StringComparison.Ordinal) >= 0;
        }
    }
}
