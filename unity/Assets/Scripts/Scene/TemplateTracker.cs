// MLOmega V19 — E26
// TemplateTracker: pure local anchoring for LocalTrackStore when the PC is cut
// (handoff §8.4 "UI locale, tracks/zoom continuent"). It tracks a small image
// patch across frames by Normalized Cross-Correlation (NCC) over a bounded
// search window on a sub-sampled greyscale camera texture. Real implementation,
// no stub.
//
// ADR (DECISIONS §E26): a managed C# NCC over a down-sampled luma buffer is
// chosen over Burst/Jobs or a compute shader. Reasons: (1) the search runs on a
// down-sampled buffer (≤160px) so the O(window·template) cost is small and stays
// inside the reflex budget on the device; (2) it is fully deterministic and
// EditMode-testable with a synthetic texture, which the plan requires as the key
// offline proof; (3) it introduces no extra package dependency (Burst) on the
// Scene assembly. If profiling on S25 shows the CPU path is too costly at the
// target cadence, the same NCC kernel ports to a compute shader behind this API
// (Track()/Reacquire()) without touching callers — the choice is reversible.
using System;
using UnityEngine;

namespace MLOmega.XR.Scene
{
    /// <summary>
    /// Tracks one template patch by NCC. Feed a fresh greyscale frame (row-major,
    /// width×height) each tick; the tracker returns the best-match centre and its
    /// correlation score, moving its search window with the target. When the score
    /// drops below the re-acquire floor it widens the search once (bounded), then
    /// reports lost — the caller ages the track out (very-short tracks TTL).
    /// </summary>
    public sealed class TemplateTracker
    {
        /// <summary>Result of one track step.</summary>
        public readonly struct Result
        {
            public readonly bool Found;
            public readonly Vector2 Center;   // normalised 0..1 in the frame
            public readonly float Score;      // NCC in [-1, 1]
            public Result(bool found, Vector2 center, float score)
            {
                Found = found;
                Center = center;
                Score = score;
            }
        }

        private readonly int _templateSize;   // odd, e.g. 15
        private readonly int _searchRadius;    // px in the sub-sampled frame
        private readonly float _acceptScore;   // NCC accept threshold
        private readonly float _reacquireScore;// below this -> widen search once
        private readonly int _reacquireRadius; // widened search radius

        private float[] _template;             // normalised (zero-mean) template
        private float _templateNorm;           // sqrt(sum sq) of zero-mean template
        private int _frameW, _frameH;
        private Vector2Int _lastCenterPx;      // in sub-sampled frame pixels
        private bool _hasTemplate;

        public bool HasTemplate => _hasTemplate;
        public Vector2 LastCenter =>
            _frameW > 0 ? new Vector2((_lastCenterPx.x + 0.5f) / _frameW, (_lastCenterPx.y + 0.5f) / _frameH)
                        : new Vector2(0.5f, 0.5f);

        public TemplateTracker(
            int templateSize = 15,
            int searchRadius = 12,
            float acceptScore = 0.55f,
            float reacquireScore = 0.35f,
            int reacquireRadius = 28)
        {
            _templateSize = templateSize | 1; // force odd
            _searchRadius = Mathf.Max(1, searchRadius);
            _acceptScore = acceptScore;
            _reacquireScore = reacquireScore;
            _reacquireRadius = Mathf.Max(_searchRadius, reacquireRadius);
        }

        /// <summary>
        /// Seed the template from a frame region centred on the normalised point.
        /// </summary>
        public void Acquire(float[] grey, int width, int height, Vector2 centerNorm)
        {
            _frameW = width;
            _frameH = height;
            int cx = Mathf.Clamp(Mathf.RoundToInt(centerNorm.x * width), 0, width - 1);
            int cy = Mathf.Clamp(Mathf.RoundToInt(centerNorm.y * height), 0, height - 1);
            _lastCenterPx = new Vector2Int(cx, cy);
            ExtractTemplate(grey, width, height, cx, cy);
            _hasTemplate = _template != null;
        }

        /// <summary>
        /// Track the template into a new frame. Searches a window around the last
        /// centre; widens once on a weak match before giving up.
        /// </summary>
        public Result Track(float[] grey, int width, int height)
        {
            if (!_hasTemplate || grey == null || width != _frameW || height != _frameH)
            {
                return new Result(false, LastCenter, 0f);
            }

            Result best = SearchWindow(grey, width, height, _lastCenterPx, _searchRadius);
            if (best.Score < _reacquireScore)
            {
                // Bounded re-acquire: widen the search window once.
                Result wide = SearchWindow(grey, width, height, _lastCenterPx, _reacquireRadius);
                if (wide.Score > best.Score) best = wide;
            }

            if (best.Score >= _acceptScore)
            {
                _lastCenterPx = new Vector2Int(
                    Mathf.RoundToInt(best.Center.x * width),
                    Mathf.RoundToInt(best.Center.y * height));
                return best;
            }
            return new Result(false, best.Center, best.Score);
        }

        // ----------------------------------------------------------------------

        private void ExtractTemplate(float[] grey, int width, int height, int cx, int cy)
        {
            int half = _templateSize / 2;
            int n = _templateSize * _templateSize;
            var t = new float[n];
            double mean = 0.0;
            int k = 0;
            for (int dy = -half; dy <= half; dy++)
            {
                int y = Mathf.Clamp(cy + dy, 0, height - 1);
                for (int dx = -half; dx <= half; dx++)
                {
                    int x = Mathf.Clamp(cx + dx, 0, width - 1);
                    float v = grey[y * width + x];
                    t[k++] = v;
                    mean += v;
                }
            }
            mean /= n;
            double sq = 0.0;
            for (int i = 0; i < n; i++)
            {
                t[i] -= (float)mean;
                sq += (double)t[i] * t[i];
            }
            _template = t;
            _templateNorm = (float)Math.Sqrt(sq);
            if (_templateNorm < 1e-6f) _templateNorm = 1e-6f;
        }

        private Result SearchWindow(float[] grey, int width, int height, Vector2Int center, int radius)
        {
            int half = _templateSize / 2;
            float bestScore = float.NegativeInfinity;
            int bestX = center.x, bestY = center.y;

            int x0 = Mathf.Clamp(center.x - radius, half, width - 1 - half);
            int x1 = Mathf.Clamp(center.x + radius, half, width - 1 - half);
            int y0 = Mathf.Clamp(center.y - radius, half, height - 1 - half);
            int y1 = Mathf.Clamp(center.y + radius, half, height - 1 - half);

            for (int cy = y0; cy <= y1; cy++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    float score = Ncc(grey, width, cx, cy, half);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = cx; bestY = cy;
                    }
                }
            }
            Vector2 centerNorm = new Vector2((bestX + 0.5f) / width, (bestY + 0.5f) / height);
            return new Result(bestScore >= _acceptScore, centerNorm,
                float.IsNegativeInfinity(bestScore) ? 0f : bestScore);
        }

        /// <summary>Zero-mean normalised cross-correlation of the template centred at (cx,cy).</summary>
        private float Ncc(float[] grey, int width, int cx, int cy, int half)
        {
            int n = _templateSize * _templateSize;
            // First pass: patch mean.
            double mean = 0.0;
            int k = 0;
            for (int dy = -half; dy <= half; dy++)
            {
                int row = (cy + dy) * width;
                for (int dx = -half; dx <= half; dx++)
                {
                    mean += grey[row + cx + dx];
                    k++;
                }
            }
            mean /= n;

            // Second pass: correlation + patch norm.
            double dot = 0.0, patchSq = 0.0;
            k = 0;
            for (int dy = -half; dy <= half; dy++)
            {
                int row = (cy + dy) * width;
                for (int dx = -half; dx <= half; dx++)
                {
                    float pv = grey[row + cx + dx] - (float)mean;
                    dot += (double)_template[k] * pv;
                    patchSq += (double)pv * pv;
                    k++;
                }
            }
            float patchNorm = (float)Math.Sqrt(patchSq);
            if (patchNorm < 1e-6f) patchNorm = 1e-6f;
            return (float)(dot / (_templateNorm * patchNorm));
        }

        /// <summary>
        /// Down-sample an RGBA32 texture buffer to a greyscale (luma) array at the
        /// target width/height. Static utility so callers (and tests) share one
        /// deterministic path from a camera texture to the tracker's input.
        /// </summary>
        public static float[] DownsampleLuma(Color32[] rgba, int srcW, int srcH, int dstW, int dstH)
        {
            var grey = new float[dstW * dstH];
            for (int y = 0; y < dstH; y++)
            {
                int sy = Mathf.Min(srcH - 1, y * srcH / dstH);
                for (int x = 0; x < dstW; x++)
                {
                    int sx = Mathf.Min(srcW - 1, x * srcW / dstW);
                    Color32 c = rgba[sy * srcW + sx];
                    // Rec. 601 luma, 0..1.
                    grey[y * dstW + x] = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                }
            }
            return grey;
        }
    }
}
