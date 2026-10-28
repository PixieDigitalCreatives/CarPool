using System;
using System.Collections.Generic;
using UnityEngine;

namespace RacingPrototype.EditorTools
{
    /// <summary>
    /// Road construction from a centre line with per-sample widths (PPT slide 9 "Road & surface
    /// work"): chunked asphalt, lane markings, kerbs, gutters with drains, raised sidewalks with
    /// junction gaps, jersey barriers or guardrails with continuous colliders, crosswalks, side
    /// streets and grid boxes. All pieces share the same sample frame, so they connect without gaps.
    /// </summary>
    public class TrackGeometryBuilder
    {
        public readonly Vector3[] Pts;
        public readonly Vector3[] Right;
        public readonly Vector3[] Tangent;
        public readonly float[] Cum;
        public readonly float[] Curv;
        public readonly float[] Hw;
        public readonly int N;
        public float Length { get; }
        public float Gap { get; }
        public const float BarrierWidth = 0.6f;
        public const float SidewalkHeight = 0.15f;

        public TrackGeometryBuilder(Vector3[] centre, float[] halfWidths, float barrierGap)
        {
            Pts = centre;
            N = Pts.Length;
            Hw = halfWidths;
            Gap = barrierGap;
            Right = new Vector3[N];
            Tangent = new Vector3[N];
            Cum = new float[N + 1];
            Curv = new float[N];
            for (int i = 0; i < N; i++)
            {
                Tangent[i] = (Pts[(i + 1) % N] - Pts[(i - 1 + N) % N]).normalized;
                Right[i] = Vector3.Cross(Vector3.up, Tangent[i]).normalized;
                Cum[i + 1] = Cum[i] + Vector3.Distance(Pts[i], Pts[(i + 1) % N]);
            }
            Length = Cum[N];
            float spacing = Length / N;
            for (int i = 0; i < N; i++)
                Curv[i] = Vector3.SignedAngle(Tangent[(i - 2 + N) % N], Tangent[(i + 2) % N], Vector3.up) * Mathf.Deg2Rad / (4f * spacing);
        }

        public int Idx(int i) => (i % N + N) % N;
        public float Barrier(int i) => Hw[Idx(i)] + Gap;
        public float SidewalkInner(int i) => Barrier(i) + BarrierWidth;
        public float Fraction(int i) => Cum[Idx(i)] / Length;
        public int SampleAt(float distance) => Idx(Mathf.RoundToInt(((distance % Length) + Length) % Length / (Length / N)));

        public float MinCornerRadius()
        {
            float max = 0f;
            foreach (float k in Curv) max = Mathf.Max(max, Mathf.Abs(k));
            return max > 1e-6f ? 1f / max : float.PositiveInfinity;
        }

        /// <summary>+1 when the loop runs anticlockwise (inside on the left), -1 when clockwise.</summary>
        public float Winding()
        {
            float area = 0f;
            for (int i = 0; i < N; i++) { var a = Pts[i]; var b = Pts[(i + 1) % N]; area += a.x * b.z - b.x * a.z; }
            return area > 0f ? 1f : -1f;
        }

        /// <summary>Side (+1 right / -1 left of the driving direction) that faces outside the loop.</summary>
        public float OutsideSide => Winding() > 0f ? 1f : -1f;

        public Vector3 PointAt(float distance, out Vector3 tangent, out Vector3 right)
        {
            float d = ((distance % Length) + Length) % Length;
            int i = Mathf.Clamp(Mathf.FloorToInt(d / (Length / N)), 0, N - 1);
            while (i < N - 1 && Cum[i + 1] < d) i++;
            while (i > 0 && Cum[i] > d) i--;
            float t = (d - Cum[i]) / Mathf.Max(1e-5f, Cum[i + 1] - Cum[i]);
            int j = (i + 1) % N;
            tangent = Vector3.Slerp(Tangent[i], Tangent[j], t).normalized;
            right = Vector3.Cross(Vector3.up, tangent).normalized;
            return Vector3.Lerp(Pts[i], Pts[j], t);
        }

        /// <summary>Shortest horizontal distance from a point to the centre line.</summary>
        public float DistanceToCentreLine(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < N; i++)
            {
                Vector3 a = Pts[i], b = Pts[(i + 1) % N];
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                Vector3 q = a + ab * t - p;
                q.y = 0f;
                best = Mathf.Min(best, q.sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>Barrier offset at the closest centre-line sample (for clearance checks).</summary>
        public float ClosestBarrierOffset(Vector3 p, out float distance)
        {
            float best = float.MaxValue; int bi = 0;
            for (int i = 0; i < N; i++)
            {
                Vector3 q = Pts[i] - p; q.y = 0f;
                float s = q.sqrMagnitude;
                if (s < best) { best = s; bi = i; }
            }
            distance = DistanceToCentreLine(p);
            return Barrier(bi);
        }

        // ================================================================== Strips

        public enum Uv { World, Across }

        /// <summary>
        /// Flat strip between two signed lateral offsets (functions of the sample index), split into
        /// chunks for culling and per-object lighting. Returns one mesh per chunk.
        /// </summary>
        public List<Mesh> Strip(string name, Func<int, float> offA, Func<int, float> offB, float y, Uv uv, float vTile, bool[] mask = null, int chunk = 25, float uTile = 8f)
        {
            var meshes = new List<Mesh>();
            for (int start = 0; start < N; start += chunk)
            {
                var kit = new MeshKit();
                for (int i = start; i < Mathf.Min(start + chunk, N); i++)
                {
                    if (mask != null && !mask[i]) continue;
                    int j = (i + 1) % N;
                    float ai = offA(i), bi = offB(i), aj = offA(j), bj = offB(j);
                    Vector3 lift = Vector3.up * y;
                    Vector3 pa = Pts[i] + Right[i] * ai + lift, pb = Pts[i] + Right[i] * bi + lift;
                    Vector3 pc = Pts[j] + Right[j] * bj + lift, pd = Pts[j] + Right[j] * aj + lift;
                    float v0 = Cum[i] / vTile, v1 = Cum[i + 1] / vTile;
                    Vector2 ua, ub, uc, ud;
                    if (uv == Uv.World)
                    {
                        ua = new Vector2(ai / uTile, v0); ub = new Vector2(bi / uTile, v0); uc = new Vector2(bj / uTile, v1); ud = new Vector2(aj / uTile, v1);
                    }
                    else
                    {
                        ua = new Vector2(0, v0); ub = new Vector2(1, v0); uc = new Vector2(1, v1); ud = new Vector2(0, v1);
                    }
                    kit.QuadUV(pa, pb, pc, pd, ua, ub, uc, ud, Vector3.up);
                }
                if (kit.Vertices.Count > 0) meshes.Add(kit.Build($"{name}_{start / chunk:00}"));
            }
            return meshes;
        }

        /// <summary>Dashed line strip (e.g. lane dividers): dash metres on, gap metres off.</summary>
        public Mesh Dashed(string name, Func<int, float> centreOffset, float width, float dash, float gap, float y)
        {
            var kit = new MeshKit();
            float period = dash + gap;
            for (int i = 0; i < N; i++)
            {
                float d0 = Cum[i], d1 = Cum[i + 1];
                float phase0 = d0 % period;
                if (phase0 > dash) continue;
                int j = (i + 1) % N;
                float c0 = centreOffset(i), c1 = centreOffset(j);
                Vector3 lift = Vector3.up * y;
                Vector3 a = Pts[i] + Right[i] * (c0 - width / 2) + lift, b = Pts[i] + Right[i] * (c0 + width / 2) + lift;
                Vector3 c = Pts[j] + Right[j] * (c1 + width / 2) + lift, d = Pts[j] + Right[j] * (c1 - width / 2) + lift;
                kit.QuadUV(a, b, c, d, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            }
            return kit.Build(name);
        }

        /// <summary>Raised sidewalk on one side: top surface plus both kerb faces, chunked.</summary>
        public List<Mesh> Sidewalk(string name, float side, float width, bool[] mask)
        {
            var meshes = new List<Mesh>();
            const int chunk = 25;
            for (int start = 0; start < N; start += chunk)
            {
                var kit = new MeshKit(2);
                for (int i = start; i < Mathf.Min(start + chunk, N); i++)
                {
                    if (mask != null && !mask[i]) continue;
                    int j = (i + 1) % N;
                    float ia = SidewalkInner(i), ja = SidewalkInner(j);
                    Vector3 up = Vector3.up * SidewalkHeight;
                    Vector3 a = Pts[i] + Right[i] * side * ia, b = Pts[i] + Right[i] * side * (ia + width);
                    Vector3 c = Pts[j] + Right[j] * side * (ja + width), d = Pts[j] + Right[j] * side * ja;
                    float v0 = Cum[i] / 2f, v1 = Cum[i + 1] / 2f;
                    kit.QuadUV(a + up, b + up, c + up, d + up, new Vector2(0, v0), new Vector2(width / 2f, v0), new Vector2(width / 2f, v1), new Vector2(0, v1), Vector3.up, 0);
                    kit.QuadUV(a, d, d + up, a + up, new Vector2(0, v0), new Vector2(0, v1), new Vector2(0.1f, v1), new Vector2(0.1f, v0), -Right[i] * side, 1);
                    kit.QuadUV(b, c, c + up, b + up, new Vector2(0, v0), new Vector2(0, v1), new Vector2(0.1f, v1), new Vector2(0.1f, v0), Right[i] * side, 1);
                }
                if (kit.Vertices.Count > 0) meshes.Add(kit.Build($"{name}_{start / chunk:00}"));
            }
            return meshes;
        }

        // ================================================================== Barriers

        /// <summary>Jersey-barrier profile (offset outward from the barrier line, height).</summary>
        static readonly Vector2[] Jersey = { new Vector2(0f, 0f), new Vector2(0.08f, 0.3f), new Vector2(0.2f, 1.05f), new Vector2(0.4f, 1.05f), new Vector2(0.52f, 0.3f), new Vector2(0.6f, 0f) };

        /// <summary>Concrete jersey barrier visual, chunked; per-chunk material picked by the caller.</summary>
        public List<Mesh> JerseyBarrier(string name, float side, int chunk = 25) => Loft(name, side, Jersey, chunk, 4f);

        /// <summary>One continuous collider mesh following the jersey profile (no gaps between chunks).</summary>
        public Mesh BarrierCollider(string name, float side, float height)
        {
            var profile = new[] { new Vector2(0f, 0f), new Vector2(0.05f, height), new Vector2(0.55f, height), new Vector2(0.6f, 0f) };
            return Loft(name, side, profile, N, 4f)[0];
        }

        List<Mesh> Loft(string name, float side, Vector2[] profile, int chunk, float uTile)
        {
            var meshes = new List<Mesh>();
            for (int start = 0; start < N; start += chunk)
            {
                var kit = new MeshKit();
                int end = Mathf.Min(start + chunk, N);
                for (int e = 0; e < profile.Length - 1; e++)
                {
                    Vector2 p0 = profile[e], p1 = profile[e + 1];
                    Vector2 edge = p1 - p0;
                    // Profile runs inner-bottom -> top -> outer-bottom (clockwise), so (-dy, dx) points out of the barrier.
                    Vector2 n2 = new Vector2(-edge.y, edge.x);
                    float vA = e / (float)(profile.Length - 1), vB = (e + 1) / (float)(profile.Length - 1);
                    int baseIndex = kit.Vertices.Count;
                    for (int i = start; i <= end; i++)
                    {
                        int k = Idx(i);
                        float b = Barrier(k);
                        float u = Cum[Mathf.Min(i, N)] / uTile;
                        kit.Add(Pts[k] + Right[k] * side * (b + p0.x) + Vector3.up * p0.y, new Vector2(u, vA));
                        kit.Add(Pts[k] + Right[k] * side * (b + p1.x) + Vector3.up * p1.y, new Vector2(u, vB));
                    }
                    for (int i = 0; i < end - start; i++)
                    {
                        int a = baseIndex + i * 2;
                        int k = Idx(start + i);
                        Vector3 facing = Right[k] * side * n2.x + Vector3.up * n2.y;
                        kit.Quad(a, a + 2, a + 3, a + 1, facing);
                    }
                }
                meshes.Add(kit.Build($"{name}_{start / chunk:00}"));
            }
            return meshes;
        }

        /// <summary>Hillside guardrail: low curb + steel W-beam band on posts (visual only; collider separate).</summary>
        public Mesh Guardrail(string name, float side)
        {
            var kit = new MeshKit(2);
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N;
                float bi = Barrier(i), bj = Barrier(j);
                Vector3 ai = Pts[i] + Right[i] * side * (bi + 0.08f), aj = Pts[j] + Right[j] * side * (bj + 0.08f);
                float u0 = Cum[i] / 4f, u1 = Cum[i + 1] / 4f;
                kit.QuadUV(ai + Vector3.up * 0.45f, aj + Vector3.up * 0.45f, aj + Vector3.up * 0.82f, ai + Vector3.up * 0.82f,
                    new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 1), new Vector2(u0, 1), -Right[i] * side, 0);
                Vector3 ci = Pts[i] + Right[i] * side * bi, cj = Pts[j] + Right[j] * side * bj;
                Vector3 di = Pts[i] + Right[i] * side * (bi + 0.35f), dj = Pts[j] + Right[j] * side * (bj + 0.35f);
                kit.QuadUV(ci + Vector3.up * 0.18f, cj + Vector3.up * 0.18f, dj + Vector3.up * 0.18f, di + Vector3.up * 0.18f, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up, 1);
                kit.QuadUV(ci, cj, cj + Vector3.up * 0.18f, ci + Vector3.up * 0.18f, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, -Right[i] * side, 1);
                if (Mathf.FloorToInt(Cum[i] / 2.5f) != Mathf.FloorToInt(Cum[i + 1] / 2.5f))
                    kit.Box(ai + Right[i] * side * 0.12f + Vector3.up * 0.45f, new Vector3(0.1f, 0.9f, 0.1f), Quaternion.LookRotation(Tangent[i]), 0);
            }
            return kit.Build(name);
        }

        // ================================================================== Markings and details

        /// <summary>A flat quad across the road centred on a track distance (start line, checkpoint lines, crosswalks).</summary>
        public Mesh CrossLine(string name, float distance, float depth, float y, Vector2 uvScale, float lateralTrim = 0f)
        {
            Vector3 centre = PointAt(distance, out Vector3 t, out Vector3 r);
            float hw = Hw[SampleAt(distance)] - lateralTrim;
            Vector3 lift = Vector3.up * y;
            var kit = new MeshKit();
            kit.QuadUV(centre - t * depth / 2 - r * hw + lift, centre - t * depth / 2 + r * hw + lift, centre + t * depth / 2 + r * hw + lift, centre + t * depth / 2 - r * hw + lift,
                Vector2.zero, new Vector2(uvScale.x, 0), uvScale, new Vector2(0, uvScale.y), Vector3.up);
            return kit.Build(name);
        }

        /// <summary>Zebra crossing: stripes parallel to the driving direction across the full road width.</summary>
        public void Zebra(MeshKit kit, float distance, float y)
        {
            Vector3 centre = PointAt(distance, out Vector3 t, out Vector3 r);
            float hw = Hw[SampleAt(distance)] - 0.6f;
            for (float x = -hw; x <= hw - 0.5f; x += 1.1f)
            {
                Vector3 c = centre + r * (x + 0.25f) + Vector3.up * y;
                kit.QuadUV(c - t * 1.6f - r * 0.25f, c - t * 1.6f + r * 0.25f, c + t * 1.6f + r * 0.25f, c + t * 1.6f - r * 0.25f, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            }
        }

        public Mesh GridMarkings(Vector3[] slotPositions, Quaternion[] slotRotations, float y)
        {
            var kit = new MeshKit();
            void Rect(Vector3 centre, Quaternion rot, float width, float depth)
            {
                Vector3 r = rot * Vector3.right * (width * 0.5f), f = rot * Vector3.forward * (depth * 0.5f), lift = Vector3.up * y;
                kit.QuadUV(centre - r - f + lift, centre + r - f + lift, centre + r + f + lift, centre - r + f + lift, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            }
            for (int i = 0; i < slotPositions.Length; i++)
            {
                Vector3 p = slotPositions[i];
                Quaternion rot = slotRotations[i];
                Rect(p + rot * new Vector3(0f, 0f, 2.9f), rot, 2.8f, 0.2f);
                Rect(p + rot * new Vector3(-1.35f, 0f, 2.15f), rot, 0.16f, 1.5f);
                Rect(p + rot * new Vector3(1.35f, 0f, 2.15f), rot, 0.16f, 1.5f);
            }
            return kit.Build("GridMarkings");
        }

        public bool[] CornerMask(float maxRadius, int dilation)
        {
            var raw = new bool[N];
            for (int i = 0; i < N; i++) raw[i] = Mathf.Abs(Curv[i]) > 1f / maxRadius;
            var mask = new bool[N];
            for (int i = 0; i < N; i++)
                for (int o = -dilation; o <= dilation && !mask[i]; o++)
                    mask[i] = raw[Idx(i + o)];
            return mask;
        }
    }
}
