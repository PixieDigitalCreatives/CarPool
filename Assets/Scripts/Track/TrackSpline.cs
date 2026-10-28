using System.Collections.Generic;
using UnityEngine;

namespace RacingPrototype
{
    /// <summary>Turns a closed list of control points into an evenly spaced, smoothed centre line.</summary>
    public static class TrackSpline
    {
        public static Vector3[] BuildCentreline(IList<Vector2> controlPoints, float spacing, int smoothingIterations)
        {
            int n = controlPoints.Count;
            if (n < 4) throw new System.ArgumentException("A closed track needs at least 4 control points.");

            const int perSegment = 64;
            var dense = new List<Vector2>(n * perSegment);
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = controlPoints[(i - 1 + n) % n];
                Vector2 p1 = controlPoints[i];
                Vector2 p2 = controlPoints[(i + 1) % n];
                Vector2 p3 = controlPoints[(i + 2) % n];
                for (int k = 0; k < perSegment; k++)
                    dense.Add(CentripetalCatmullRom(p0, p1, p2, p3, k / (float)perSegment));
            }

            // Laplacian smoothing on a 1 m polyline evens out curvature spikes at control points.
            var pts = Resample(dense, 1f);
            for (int it = 0; it < smoothingIterations; it++) pts = SmoothOnce(pts);
            pts = Resample(pts, spacing);

            // Rotate so sample 0 is the start/finish line (closest to control point 0).
            int start = 0;
            float best = float.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                float d = (pts[i] - controlPoints[0]).sqrMagnitude;
                if (d < best) { best = d; start = i; }
            }

            var result = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 p = pts[(i + start) % pts.Count];
                result[i] = new Vector3(p.x, 0f, p.y);
            }
            return result;
        }

        static Vector2 CentripetalCatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t0 = 0f;
            float t1 = Knot(t0, p0, p1);
            float t2 = Knot(t1, p1, p2);
            float t3 = Knot(t2, p2, p3);
            float tt = Mathf.Lerp(t1, t2, t);

            Vector2 a1 = Lerp(p0, p1, t0, t1, tt);
            Vector2 a2 = Lerp(p1, p2, t1, t2, tt);
            Vector2 a3 = Lerp(p2, p3, t2, t3, tt);
            Vector2 b1 = Lerp(a1, a2, t0, t2, tt);
            Vector2 b2 = Lerp(a2, a3, t1, t3, tt);
            return Lerp(b1, b2, t1, t2, tt);
        }

        static float Knot(float ti, Vector2 a, Vector2 b) => ti + Mathf.Sqrt(Mathf.Max(Vector2.Distance(a, b), 1e-4f));

        static Vector2 Lerp(Vector2 a, Vector2 b, float ta, float tb, float t) =>
            (tb - t) / (tb - ta) * a + (t - ta) / (tb - ta) * b;

        static List<Vector2> SmoothOnce(List<Vector2> pts)
        {
            int n = pts.Count;
            var output = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 avg = (pts[(i - 1 + n) % n] + pts[(i + 1) % n]) * 0.5f;
                output.Add(pts[i] + (avg - pts[i]) * 0.5f);
            }
            return output;
        }

        /// <summary>Resamples a closed polyline at (almost exactly) uniform spacing.</summary>
        static List<Vector2> Resample(List<Vector2> pts, float spacing)
        {
            int n = pts.Count;
            var cumulative = new float[n + 1];
            for (int i = 0; i < n; i++)
                cumulative[i + 1] = cumulative[i] + Vector2.Distance(pts[i], pts[(i + 1) % n]);

            float length = cumulative[n];
            int count = Mathf.Max(8, Mathf.RoundToInt(length / spacing));
            float step = length / count;
            var output = new List<Vector2>(count);
            int seg = 0;
            for (int k = 0; k < count; k++)
            {
                float d = k * step;
                while (seg < n - 1 && cumulative[seg + 1] < d) seg++;
                float segLen = cumulative[seg + 1] - cumulative[seg];
                float t = segLen > 1e-6f ? (d - cumulative[seg]) / segLen : 0f;
                output.Add(Vector2.Lerp(pts[seg], pts[(seg + 1) % n], t));
            }
            return output;
        }
    }
}
