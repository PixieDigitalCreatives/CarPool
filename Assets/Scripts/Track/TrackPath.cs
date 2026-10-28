using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// Runtime model of a route's centre line. Distance 0 is the start/finish line and distances
    /// increase in the driving direction. Road width can vary along the lap (wider opening
    /// straight). Used for race progress, AI steering, resets and wrong-way detection.
    /// </summary>
    public class TrackPath : MonoBehaviour
    {
        [SerializeField] TrackDefinition definition;
        [SerializeField] Vector3[] points;
        [SerializeField] float[] halfWidths;
        [SerializeField] float barrierGap = 1.2f;

        float[] cumulative;
        Vector3[] tangents;
        float[] curvature;
        float length;
        float spacing;
        float maxHalfWidth;
        float minHalfWidth;

        public TrackDefinition Definition => definition;
        public int SampleCount => points != null ? points.Length : 0;
        public float Length { get { EnsureCache(); return length; } }
        public float Spacing { get { EnsureCache(); return spacing; } }
        public float BarrierGap => barrierGap;
        public float MinHalfWidth { get { EnsureCache(); return minHalfWidth; } }
        public float MaxBarrierOffset { get { EnsureCache(); return maxHalfWidth + barrierGap; } }

        /// <summary>Called by the editor scene builder.</summary>
        public void Initialise(TrackDefinition def, Vector3[] centreLine, float[] sampleHalfWidths, float gap)
        {
            definition = def;
            points = centreLine;
            halfWidths = sampleHalfWidths;
            barrierGap = gap;
            cumulative = null;
            EnsureCache();
        }

        void Awake() => EnsureCache();

        void EnsureCache()
        {
            if (points == null || points.Length < 3) return;
            int n = points.Length;
            if (cumulative != null && cumulative.Length == n + 1) return;

            cumulative = new float[n + 1];
            for (int i = 0; i < n; i++)
                cumulative[i + 1] = cumulative[i] + Vector3.Distance(points[i], points[(i + 1) % n]);
            length = cumulative[n];
            spacing = length / n;

            tangents = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 t = points[(i + 1) % n] - points[(i - 1 + n) % n];
                t.y = 0f;
                tangents[i] = t.normalized;
            }

            curvature = new float[n];
            for (int i = 0; i < n; i++)
            {
                float angle = Vector3.SignedAngle(tangents[(i - 2 + n) % n], tangents[(i + 2) % n], Vector3.up) * Mathf.Deg2Rad;
                curvature[i] = angle / (4f * spacing);
            }

            if (halfWidths == null || halfWidths.Length != n)
            {
                halfWidths = new float[n];
                for (int i = 0; i < n; i++) halfWidths[i] = 7f;
            }
            maxHalfWidth = 0f;
            minHalfWidth = float.MaxValue;
            foreach (float w in halfWidths)
            {
                maxHalfWidth = Mathf.Max(maxHalfWidth, w);
                minHalfWidth = Mathf.Min(minHalfWidth, w);
            }
        }

        public float Wrap(float distance)
        {
            EnsureCache();
            float d = distance % length;
            return d < 0f ? d + length : d;
        }

        int SegmentAt(float distance, out float t)
        {
            float d = Wrap(distance);
            int n = points.Length;
            int i = Mathf.Clamp(Mathf.FloorToInt(d / spacing), 0, n - 1);
            while (i < n - 1 && cumulative[i + 1] < d) i++;
            while (i > 0 && cumulative[i] > d) i--;
            float segLen = cumulative[i + 1] - cumulative[i];
            t = segLen > 1e-5f ? (d - cumulative[i]) / segLen : 0f;
            return i;
        }

        public int SampleIndexAt(float distance)
        {
            EnsureCache();
            int i = SegmentAt(distance, out float t);
            return t > 0.5f ? (i + 1) % points.Length : i;
        }

        public Vector3 GetPoint(float distance)
        {
            EnsureCache();
            int i = SegmentAt(distance, out float t);
            return Vector3.Lerp(points[i], points[(i + 1) % points.Length], t);
        }

        public Vector3 GetTangent(float distance)
        {
            EnsureCache();
            int i = SegmentAt(distance, out float t);
            return Vector3.Slerp(tangents[i], tangents[(i + 1) % points.Length], t).normalized;
        }

        public Vector3 GetRight(float distance) => Vector3.Cross(Vector3.up, GetTangent(distance)).normalized;

        /// <summary>Half of the asphalt width at a distance.</summary>
        public float HalfWidthAt(float distance)
        {
            EnsureCache();
            int i = SegmentAt(distance, out float t);
            return Mathf.Lerp(halfWidths[i], halfWidths[(i + 1) % points.Length], t);
        }

        /// <summary>Distance from the centre line to the inner face of the barriers.</summary>
        public float BarrierOffsetAt(float distance) => HalfWidthAt(distance) + barrierGap;

        int Idx(int index) => (index % points.Length + points.Length) % points.Length;
        public Vector3 GetSamplePoint(int index) => points[Idx(index)];
        public Vector3 GetSampleTangent(int index) { EnsureCache(); return tangents[Idx(index)]; }
        public float GetSampleDistance(int index) { EnsureCache(); return cumulative[Idx(index)]; }
        public float GetSampleCurvature(int index) { EnsureCache(); return curvature[Idx(index)]; }
        public float GetSampleHalfWidth(int index) { EnsureCache(); return halfWidths[Idx(index)]; }

        /// <summary>Signed lateral offset of a position from the centre line (positive = right of the driving direction).</summary>
        public float GetLateralOffset(Vector3 position, float distance)
        {
            Vector3 delta = position - GetPoint(distance);
            delta.y = 0f;
            return Vector3.Dot(delta, GetRight(distance));
        }

        /// <summary>
        /// Distance along the track of the closest centre-line point. With a hint (the previous
        /// result) only a local window is searched, which keeps it cheap and avoids snapping to a
        /// different part of the circuit.
        /// </summary>
        public float FindClosestDistance(Vector3 position, float hint = -1f)
        {
            EnsureCache();
            int n = points.Length;
            if (hint >= 0f)
            {
                int centre = SegmentAt(hint, out _);
                int back = Mathf.CeilToInt(30f / spacing);
                int ahead = Mathf.CeilToInt(60f / spacing);
                float d = SearchRange(position, centre - back, centre + ahead, out float sqr);
                float limit = MaxBarrierOffset + 6f;
                if (sqr <= limit * limit) return d;
            }
            return SearchRange(position, 0, n - 1, out _);
        }

        float SearchRange(Vector3 position, int from, int to, out float bestSqr)
        {
            int n = points.Length;
            bestSqr = float.MaxValue;
            float bestDistance = 0f;
            Vector2 p = new Vector2(position.x, position.z);
            for (int k = from; k <= to; k++)
            {
                int i = (k % n + n) % n;
                int j = (i + 1) % n;
                Vector2 a = new Vector2(points[i].x, points[i].z);
                Vector2 b = new Vector2(points[j].x, points[j].z);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                float sqr = (a + ab * t - p).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestDistance = cumulative[i] + (cumulative[i + 1] - cumulative[i]) * t;
                }
            }
            return Wrap(bestDistance);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (points == null || points.Length < 2) return;
            Gizmos.color = Color.yellow;
            for (int i = 0; i < points.Length; i++)
                Gizmos.DrawLine(points[i] + Vector3.up * 0.3f, points[(i + 1) % points.Length] + Vector3.up * 0.3f);
        }
#endif
    }
}
