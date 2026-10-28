using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RacingPrototype.EditorTools
{
    /// <summary>Accumulates vertices/triangles (with optional sub-meshes) and orients faces by a hint normal.</summary>
    public class MeshKit
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        readonly List<List<int>> submeshes = new List<List<int>>();

        public MeshKit(int submeshCount = 1)
        {
            for (int i = 0; i < submeshCount; i++) submeshes.Add(new List<int>());
        }

        public int SubmeshCount => submeshes.Count;
        public bool HasTriangles(int submesh) => submeshes[submesh].Count > 0;

        public int Add(Vector3 position, Vector2 uv)
        {
            Vertices.Add(position);
            Uvs.Add(uv);
            return Vertices.Count - 1;
        }

        /// <summary>Adds a triangle whose front face points along <paramref name="facing"/>.</summary>
        public void Tri(int a, int b, int c, Vector3 facing, int submesh = 0)
        {
            Vector3 n = Vector3.Cross(Vertices[b] - Vertices[a], Vertices[c] - Vertices[a]);
            var list = submeshes[submesh];
            if (Vector3.Dot(n, facing) >= 0f) { list.Add(a); list.Add(b); list.Add(c); }
            else { list.Add(a); list.Add(c); list.Add(b); }
        }

        /// <summary>Quad a-b-c-d (in order around the edge) facing <paramref name="facing"/>.</summary>
        public void Quad(int a, int b, int c, int d, Vector3 facing, int submesh = 0)
        {
            Tri(a, b, c, facing, submesh);
            Tri(a, c, d, facing, submesh);
        }

        /// <summary>Flat quad from four corners with explicit UVs (own vertices, flat shading).</summary>
        public void QuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 facing, int submesh = 0)
        {
            int ia = Add(a, ua), ib = Add(b, ub), ic = Add(c, uc), id = Add(d, ud);
            Quad(ia, ib, ic, id, facing, submesh);
        }

        /// <summary>
        /// Axis-aligned (in local space of <paramref name="rot"/>) box with per-face UVs scaled by
        /// <paramref name="uvScale"/> metres per unit (0 = 0..1 per face).
        /// </summary>
        public void Box(Vector3 centre, Vector3 size, Quaternion rot, int submesh = 0, float uvScale = 0f, bool bottom = false, Vector2? uvOverride = null)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => centre + rot * new Vector3(x * h.x, y * h.y, z * h.z);
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, float w, float hh)
            {
                Vector2 s = uvScale > 0f ? new Vector2(w / uvScale, hh / uvScale) : Vector2.one;
                if (uvOverride.HasValue) { var o = uvOverride.Value; QuadUV(a, b, c, d, o, o, o, o, n, submesh); return; }
                QuadUV(a, b, c, d, new Vector2(0, 0), new Vector2(s.x, 0), new Vector2(s.x, s.y), new Vector2(0, s.y), n, submesh);
            }
            Face(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), rot * Vector3.back, size.x, size.y);
            Face(P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), rot * Vector3.forward, size.x, size.y);
            Face(P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), rot * Vector3.left, size.z, size.y);
            Face(P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), rot * Vector3.right, size.z, size.y);
            Face(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), rot * Vector3.up, size.x, size.z);
            if (bottom) Face(P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1), rot * Vector3.down, size.x, size.z);
        }

        /// <summary>Vertical cylinder (prism) with flat caps.</summary>
        public void Cylinder(Vector3 baseCentre, float radius, float height, int sides, int submesh = 0, float topRadius = -1f, Vector2? uvOverride = null)
        {
            float tr = topRadius < 0f ? radius : topRadius;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 b0 = baseCentre + d0 * radius, b1 = baseCentre + d1 * radius;
                Vector3 t0 = baseCentre + d0 * tr + Vector3.up * height, t1 = baseCentre + d1 * tr + Vector3.up * height;
                Vector2 uv0 = uvOverride ?? new Vector2(i / (float)sides, 0), uv1 = uvOverride ?? new Vector2((i + 1) / (float)sides, 0);
                Vector2 uv2 = uvOverride ?? new Vector2((i + 1) / (float)sides, 1), uv3 = uvOverride ?? new Vector2(i / (float)sides, 1);
                QuadUV(b0, b1, t1, t0, uv0, uv1, uv2, uv3, (d0 + d1), submesh);
                int c = Add(baseCentre + Vector3.up * height, uvOverride ?? new Vector2(0.5f, 0.5f));
                int e0 = Add(t0, uvOverride ?? Vector2.zero), e1 = Add(t1, uvOverride ?? Vector2.zero);
                Tri(c, e0, e1, Vector3.up, submesh);
            }
        }

        public Mesh Build(string name, bool recalcNormals = true)
        {
            var mesh = new Mesh { name = name };
            if (Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            mesh.SetUVs(0, Uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
            if (recalcNormals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Flat-shaded cone. Base at y=0, apex at y=height.</summary>
        public static Mesh Cone(string name, int sides, float radius, float height)
        {
            var kit = new MeshKit();
            Vector3 apex = new Vector3(0f, height, 0f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                Vector3 outward = (p0 + p1) * 0.5f + Vector3.up * (radius * radius / height) * 0.5f;
                int ia = kit.Add(p0, new Vector2(0f, 0f)), ib = kit.Add(p1, new Vector2(1f, 0f)), ic = kit.Add(apex, new Vector2(0.5f, 1f));
                kit.Tri(ia, ib, ic, outward);
            }
            return kit.Build(name);
        }
    }
}
