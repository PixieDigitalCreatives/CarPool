using System.IO;
using UnityEditor;
using UnityEngine;

namespace RacingPrototype.EditorTools
{
    /// <summary>Renders a route's centre line into a transparent minimap sprite (PPT: every route has its own minimap).</summary>
    public static class MinimapBuilder
    {
        public static Sprite Build(string name, Vector3[] pts, float[] checkpointFractions, out Vector2 min, out Vector2 max)
        {
            const int size = 512;
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in pts)
            {
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            Vector2 ext = max - min;
            float span = Mathf.Max(ext.x, ext.y) * 1.12f;
            Vector2 centre = (min + max) * 0.5f;
            min = centre - Vector2.one * span * 0.5f;
            max = centre + Vector2.one * span * 0.5f;

            var outline = new float[size * size];
            var fill = new float[size * size];
            Vector2 origin = min;
            Vector2 ToPx(Vector3 p) => new Vector2((p.x - origin.x) / span * size, (p.z - origin.y) / span * size);
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                Stroke(outline, size, ToPx(pts[i]), ToPx(pts[(i + 1) % n]), 9f);
                Stroke(fill, size, ToPx(pts[i]), ToPx(pts[(i + 1) % n]), 5.5f);
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                Color c = new Color(0.05f, 0.06f, 0.08f, outline[i] * 0.95f);
                px[i] = Color.Lerp(c, new Color(0.88f, 0.9f, 0.94f, 1f), fill[i]);
            }
            // Start/finish tick (amber) and checkpoint ticks (green) across the line.
            Tick(px, size, pts, 0, new Color(1f, 0.71f, 0f), ToPx, 16f);
            foreach (float f in checkpointFractions) Tick(px, size, pts, Mathf.RoundToInt(f * n) % n, new Color(0.2f, 0.8f, 0.45f), ToPx, 10f);
            tex.SetPixels(px);
            tex.Apply();

            string folder = "Assets/Resources/UI/Minimaps";
            ProceduralAssets.EnsureFolder(folder);
            string path = $"{folder}/{name}.png";
            File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;   // the default here is Multiple, which yields no Sprite asset
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void Tick(Color[] px, int size, Vector3[] pts, int i, Color colour, System.Func<Vector3, Vector2> toPx, float halfLength)
        {
            int n = pts.Length;
            Vector2 a = toPx(pts[i]);
            Vector2 dir = (toPx(pts[(i + 1) % n]) - toPx(pts[(i - 1 + n) % n])).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x);
            var mask = new float[size * size];
            Stroke(mask, size, a - normal * halfLength, a + normal * halfLength, 3.2f);
            for (int k = 0; k < px.Length; k++)
                if (mask[k] > 0f) px[k] = Color.Lerp(px[k], colour, mask[k]);
        }

        static void Stroke(float[] buffer, int size, Vector2 a, Vector2 b, float radius)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius - 1)), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius - 1)), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius + 1));
            Vector2 ab = b - a;
            float len2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    float d = Vector2.Distance(p, a + ab * t);
                    float v = Mathf.Clamp01(radius - d + 0.5f);
                    int idx = y * size + x;
                    if (v > buffer[idx]) buffer[idx] = v;
                }
        }
    }
}
