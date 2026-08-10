// MLOmega V19 — E53 (Viki mode aide)
// Typed views over the FROZEN PC→device contract carried by the two E53 UIIntents,
// so the atoms and the TaskOverlayRenderer never cast raw dictionaries:
//   * task_panel  content = { title, domain, steps:[{index,text,status}], progress, ghost_next }
//   * task_anchor content = { label_en, name, role, track_id?/entity_id?,
//                             gesture:{kind,from?,to?}, quantity?, timer_seconds?, caution? }
// Parsing is done once on Bind (not per frame) and reuses the same IntentRead
// helpers every E25 component uses, so the loosely-typed JSON is read identically.
using System.Collections.Generic;
using MLOmega.Contracts.V19;
using UnityEngine;

namespace MLOmega.XR.UI.Components.TaskAtoms
{
    /// <summary>Status of one plan step, mapped from the "status" string.</summary>
    public enum StepStatus { Pending = 0, Next = 1, Current = 2, Done = 3 }

    /// <summary>One row of the task plan.</summary>
    public readonly struct TaskStep
    {
        public readonly int Index;
        public readonly string Text;
        public readonly StepStatus Status;
        public TaskStep(int index, string text, StepStatus status)
        {
            Index = index; Text = text; Status = status;
        }
    }

    /// <summary>Parsed task_panel content.</summary>
    public sealed class TaskPanelContent
    {
        public string Title;
        public string Domain;
        public float Progress;      // 0..1
        public bool GhostNext;
        public readonly List<TaskStep> Steps = new List<TaskStep>();

        public static TaskPanelContent From(UIIntent intent)
        {
            var c = new TaskPanelContent();
            Dictionary<string, object> d = intent?.Content;
            c.Title = IntentRead.Str(d, "title", "Aide");
            c.Domain = IntentRead.Str(d, "domain", null);
            c.Progress = Mathf.Clamp01((float)IntentRead.Num(d, "progress", 0.0));
            c.GhostNext = IntentRead.Flag(d, "ghost_next", true);

            if (d != null && d.TryGetValue("steps", out object raw) && raw is IList<object> list)
            {
                foreach (object o in list)
                {
                    if (o is Dictionary<string, object> s)
                    {
                        int idx = (int)IntentRead.Num(s, "index", c.Steps.Count);
                        string text = IntentRead.Str(s, "text", "");
                        c.Steps.Add(new TaskStep(idx, text, ParseStatus(IntentRead.Str(s, "status", "pending"))));
                    }
                }
            }
            return c;
        }

        public static StepStatus ParseStatus(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "done": return StepStatus.Done;
                case "current": return StepStatus.Current;
                case "next": return StepStatus.Next;
                default: return StepStatus.Pending;
            }
        }
    }

    /// <summary>Gesture primitive to animate on the anchored object.</summary>
    public enum GestureKind { None = 0, Arc = 1, Circular = 2, Linear = 3, Pulse = 4 }

    /// <summary>Parsed task_anchor content.</summary>
    public sealed class TaskAnchorContent
    {
        public string LabelEn;      // "bowl"
        public string Name;         // "le bol"
        public string Role;         // target|tool|ingredient|part
        public string TrackId;      // set when the PC matched a VisionRT track
        public string EntityId;
        public string FromTrackId;  // optional source object for cross-object gesture
        public string ToTrackId;    // optional target object for cross-object gesture
        public bool Ghost;          // preloaded N+1 anchor: configured but invisible
        public GestureKind Gesture;
        public Vector2 GestureFrom; // 0..1 local to the anchored region
        public Vector2 GestureTo;
        public bool HasFrom;
        public bool HasTo;
        public string Quantity;     // "200 g"
        public float TimerSeconds;  // <=0 => no timer
        public string Caution;      // null => none

        public bool HasGesture => Gesture != GestureKind.None;
        public bool HasTimer => TimerSeconds > 0f;
        public bool HasQuantity => !string.IsNullOrEmpty(Quantity);
        public bool HasCaution => !string.IsNullOrEmpty(Caution);

        public static TaskAnchorContent From(UIIntent intent)
        {
            var c = new TaskAnchorContent();
            Dictionary<string, object> d = intent?.Content;
            c.LabelEn = IntentRead.Str(d, "label_en", IntentRead.Str(d, "label", ""));
            c.Name = IntentRead.Str(d, "name", c.LabelEn);
            c.Role = IntentRead.Str(d, "role", "target");
            // track_id/entity_id may live on the content or on the intent's typed fields.
            c.TrackId = IntentRead.Str(d, "track_id", intent?.TargetTrackId);
            c.EntityId = IntentRead.Str(d, "entity_id", intent?.EntityId);
            c.FromTrackId = IntentRead.Str(d, "from_track_id", null);
            c.ToTrackId = IntentRead.Str(d, "to_track_id", null);
            c.Ghost = IntentRead.Flag(d, "ghost", IntentRead.Flag(d, "lookahead", false));
            c.Quantity = IntentRead.Str(d, "quantity", null);
            c.TimerSeconds = (float)IntentRead.Num(d, "timer_seconds", 0.0);
            c.Caution = IntentRead.Str(d, "caution", null);

            if (d != null && d.TryGetValue("gesture", out object g) && g is Dictionary<string, object> gd)
            {
                c.Gesture = ParseGesture(IntentRead.Str(gd, "kind", "none"));
                c.HasFrom = IntentRead.TryPoint(gd, "from", out c.GestureFrom);
                c.HasTo = IntentRead.TryPoint(gd, "to", out c.GestureTo);
            }
            return c;
        }

        public static GestureKind ParseGesture(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "arc": return GestureKind.Arc;
                case "circular": return GestureKind.Circular;
                case "linear": return GestureKind.Linear;
                case "pulse": return GestureKind.Pulse;
                default: return GestureKind.None;
            }
        }
    }
}
