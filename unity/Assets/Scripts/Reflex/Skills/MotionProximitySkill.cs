// MLOmega V19 — E26
// MotionProximitySkill (§9.2/§14.10): a peripheral, directional cue when
// something grows fast in the field of view. It compares sub-sampled greyscale
// frames (per-cell mean absolute difference), discounts self-motion using the
// IMU/pose rotation delta (a head turn is not an approaching object), and — when
// the fastest-growing region exceeds a threshold — emits a directional
// offscreen_arrow-style cue with graduated severity. Information only, never
// "you can go" (§17.3). Cues are aggregated (min interval), never per frame.
using System.Collections.Generic;
using UnityEngine;

namespace MLOmega.XR.Reflex.Skills
{
    public sealed class MotionProximitySkill : ReflexSkillBase
    {
        [Tooltip("Grid columns/rows the frame is reduced to for the growth estimate.")]
        [SerializeField] private int _gridCols = 8;
        [SerializeField] private int _gridRows = 6;

        public override ReflexSkillId SkillId => ReflexSkillId.MotionProximity;

        private float[] _prevCells;
        private long _lastCueMs = -1;

        /// <summary>
        /// Feed one sub-sampled greyscale frame (width×height row-major) plus the
        /// head yaw/pitch rotation delta since the last frame (radians) to discount
        /// self-motion. Returns the growth estimate (0..1) so callers/tests can assert.
        /// </summary>
        public float Analyze(float[] grey, int width, int height, float yawDelta, float pitchDelta, long nowMs)
        {
            if (!IsActive || grey == null) return 0f;
            float[] cells = ReduceToCells(grey, width, height);
            float growth = 0f;
            int growthCol = _gridCols / 2, growthRow = _gridRows / 2;

            if (_prevCells != null && _prevCells.Length == cells.Length)
            {
                // Self-motion discount: a large rotation means the whole frame
                // shifts, so scale the raw diff down proportionally.
                float selfMotion = Mathf.Clamp01((Mathf.Abs(yawDelta) + Mathf.Abs(pitchDelta)) * SelfMotionGain);
                float keep = 1f - selfMotion;
                for (int i = 0; i < cells.Length; i++)
                {
                    float d = Mathf.Abs(cells[i] - _prevCells[i]) * keep;
                    if (d > growth)
                    {
                        growth = d;
                        growthCol = i % _gridCols;
                        growthRow = i / _gridCols;
                    }
                }
            }
            _prevCells = cells;

            EvaluateCue(growth, growthCol, growthRow, nowMs);
            return growth;
        }

        private void EvaluateCue(float growth, int col, int row, long nowMs)
        {
            float cueThreshold = _config != null ? _config.MotionGrowthCue : 0.18f;
            float critThreshold = _config != null ? _config.MotionGrowthCritical : 0.42f;
            long minInterval = _config != null ? _config.MotionCueMinIntervalMs : 700;

            if (growth < cueThreshold) return;
            bool critical = growth >= critThreshold;
            // Graduated severity always feeds the aggregator; but the *visible* cue
            // respects the min interval so we don't flash the periphery every frame.
            string severity = critical ? "critical" : (growth >= (cueThreshold + critThreshold) * 0.5f ? "warning" : "info");

            RecordReflex(
                aggregateKey: "motion_proximity",
                prediction: new Dictionary<string, object>
                {
                    { "growth", growth }, { "col", col }, { "row", row }
                },
                horizonMs: 400, confidence: Mathf.Clamp01(growth / critThreshold), severity: severity,
                nowMs: nowMs);

            if (!critical && _lastCueMs >= 0 && nowMs - _lastCueMs < minInterval) return;
            _lastCueMs = nowMs;
            EmitCue(growth, col, row, severity, critical);
        }

        private void EmitCue(float growth, int col, int row, string severity, bool critical)
        {
            // Direction from the growth cell: left/right/up/down relative to centre.
            float nx = (col + 0.5f) / _gridCols;
            float ny = (row + 0.5f) / _gridRows;
            float bearing = Mathf.Atan2(nx - 0.5f, 0.5f - ny) * Mathf.Rad2Deg; // 0=up, +90=right

            var intent = NewIntent("offscreen_arrow", "ul_motion_cue");
            intent.TruthLevel = "observed";
            intent.Confidence = Mathf.Clamp01(growth);
            if (critical) intent.UiHint["critical"] = true;
            intent.UiHint["severity"] = severity;
            intent.Content["direction_deg"] = bearing;
            intent.Content["text"] = "movement";
            intent.Anchor["bearing_deg"] = bearing;
            // A peripheral cue is short-lived.
            intent.TtlMs = 900;
            EmitIntent(intent);
        }

        private float[] ReduceToCells(float[] grey, int width, int height)
        {
            var cells = new float[_gridCols * _gridRows];
            for (int gy = 0; gy < _gridRows; gy++)
            {
                int y0 = gy * height / _gridRows;
                int y1 = (gy + 1) * height / _gridRows;
                for (int gx = 0; gx < _gridCols; gx++)
                {
                    int x0 = gx * width / _gridCols;
                    int x1 = (gx + 1) * width / _gridCols;
                    double sum = 0; int n = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++) { sum += grey[y * width + x]; n++; }
                    cells[gy * _gridCols + gx] = n > 0 ? (float)(sum / n) : 0f;
                }
            }
            return cells;
        }

        protected override void OnDeactivated()
        {
            _prevCells = null;
            _lastCueMs = -1;
        }

        private const float SelfMotionGain = 2.0f;
    }
}
