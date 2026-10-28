using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RacingPrototype.EditorTools
{
    /// <summary>
    /// Generates the project's textures (albedo, normal and emission maps), UI sprites, sounds and
    /// URP materials as real assets. Everything is original and procedural; no third-party art.
    /// </summary>
    public static class ProceduralAssets
    {
        public const string TextureFolder = "Assets/Materials/Textures";
        public const string MaterialFolder = "Assets/Materials";
        public const string AudioFolder = "Assets/Audio";
        public const string UIFolder = "Assets/Resources/UI";

        // ================================================================== Helpers

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Creates the asset, or overwrites an existing one in place so its GUID survives a rebuild.</summary>
        public static T SaveOrReplace<T>(T asset, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return asset;
            }
            string name = existing.name;
            EditorUtility.CopySerialized(asset, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(asset);
            return existing;
        }

        public static float Hash(int x, int y, int seed = 0)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }

        static float Fbm(float x, float y, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int i = 0; i < octaves; i++) { sum += Mathf.PerlinNoise(x * freq + 17.3f * i, y * freq + 9.1f * i) * amp; amp *= 0.5f; freq *= 2f; }
            return sum;
        }

        public delegate Color PixelFn(int x, int y, int w, int h);

        public static Texture2D SaveTexture(string name, int w, int h, PixelFn fn, TextureWrapMode wrap = TextureWrapMode.Repeat,
            FilterMode filter = FilterMode.Trilinear, bool sRGB = true, string folder = TextureFolder)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, !sRGB);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = fn(x, y, w, h);
            tex.SetPixels(px);
            string path = $"{folder}/{name}.png";
            WritePng(tex, path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = wrap;
            importer.filterMode = filter;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = sRGB;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Tangent-space normal map computed from a height function (0-1).</summary>
        public static Texture2D SaveNormal(string name, int w, int h, Func<int, int, float> height, float strength)
        {
            var hgt = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    hgt[y * w + x] = height(x, y);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float l = hgt[y * w + (x - 1 + w) % w], r = hgt[y * w + (x + 1) % w];
                    float d = hgt[((y - 1 + h) % h) * w + x], u = hgt[((y + 1) % h) * w + x];
                    var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    px[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            tex.SetPixels(px);
            string path = $"{TextureFolder}/{name}.png";
            WritePng(tex, path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void WritePng(Texture2D tex, string assetPath)
        {
            tex.Apply();
            File.WriteAllBytes(Path.GetFullPath(assetPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        // ================================================================== Surfaces

        /// <summary>Dry asphalt, 8 x 8 m per tile: aggregate speckle, patch repairs, faint cracks.</summary>
        public static Texture2D Asphalt() => SaveTexture("Asphalt", 1024, 1024, (x, y, w, h) =>
        {
            float g = 0.28f + Hash(x, y) * 0.07f + (Fbm(x * 0.006f, y * 0.006f) - 0.5f) * 0.08f;
            if (Hash(x / 2, y / 2, 3) > 0.985f) g += 0.12f;                              // light aggregate
            float patch = Fbm(x * 0.0025f + 40f, y * 0.0025f);
            if (patch > 0.62f) g -= 0.035f;                                             // repair patches
            float crack = Mathf.Abs(Mathf.PerlinNoise(x * 0.01f, y * 0.04f) - 0.5f);
            if (crack < 0.006f) g -= 0.06f;
            return new Color(g, g, g * 1.03f);
        });

        public static Texture2D AsphaltNormal() => SaveNormal("Asphalt_N", 512, 512, (x, y) => Hash(x, y, 7) * 0.6f + Fbm(x * 0.05f, y * 0.05f) * 0.4f, 2.2f);

        /// <summary>Concrete pavers (0.5 m), 2 x 2 m per tile.</summary>
        public static Texture2D Paving(string name, Color tint) => SaveTexture(name, 512, 512, (x, y, w, h) =>
        {
            int cell = 128;
            bool grout = x % cell < 4 || y % cell < 4;
            float n = 0.9f + Hash(x / cell, y / cell, 5) * 0.12f + Hash(x, y) * 0.05f - Fbm(x * 0.01f, y * 0.01f) * 0.08f;
            return grout ? tint * 0.55f : tint * n;
        });

        public static Texture2D PavingNormal() => SaveNormal("Paving_N", 512, 512, (x, y) =>
        {
            int cell = 128;
            float gx = Mathf.Min(x % cell, cell - x % cell), gy = Mathf.Min(y % cell, cell - y % cell);
            return Mathf.Clamp01(Mathf.Min(gx, gy) / 6f) * 0.8f + Hash(x, y, 2) * 0.2f;
        }, 1.6f);

        public static Texture2D Concrete() => SaveTexture("Concrete", 256, 256, (x, y, w, h) =>
        {
            float g = 0.62f + Hash(x, y) * 0.07f + (Fbm(x * 0.02f, y * 0.02f) - 0.5f) * 0.12f;
            return new Color(g, g * 0.99f, g * 0.96f);
        });

        /// <summary>Red / white kerb, 2 m period along V.</summary>
        public static Texture2D Kerb() => SaveTexture("Kerb", 32, 128, (x, y, w, h) =>
            (y < h / 2 ? new Color(0.78f, 0.08f, 0.07f) : new Color(0.92f, 0.92f, 0.9f)) * (0.92f + Hash(x, y) * 0.08f));

        /// <summary>Concrete barrier paint along U (4 m period): two blocks of colour, grime at the base.</summary>
        public static Texture2D BarrierPaint(string name, Color a, Color b) => SaveTexture(name, 256, 64, (x, y, w, h) =>
        {
            Color c = x < w / 2 ? a : b;
            float grime = Mathf.Clamp01(1f - y / (h * 0.35f)) * 0.35f;
            float n = 0.9f + Hash(x, y) * 0.1f - Fbm(x * 0.05f, y * 0.1f) * 0.1f;
            c = Color.Lerp(c * n, new Color(0.2f, 0.19f, 0.18f), grime);
            if (y > h - 3) c *= 0.7f;
            return c;
        });

        public static Texture2D Checker() => SaveTexture("Checker", 64, 64, (x, y, w, h) =>
            ((x / 8) + (y / 8)) % 2 == 0 ? new Color(0.93f, 0.93f, 0.93f) : new Color(0.05f, 0.05f, 0.05f), TextureWrapMode.Repeat, FilterMode.Bilinear);

        /// <summary>Direction board: yellow chevrons pointing left (flip U for right).</summary>
        public static Texture2D Chevron() => SaveTexture("Chevron", 512, 128, (x, y, w, h) =>
        {
            float u = x / (float)w * 4f, v = y / (float)h;
            float f = Mathf.Repeat(u + Mathf.Abs(v - 0.5f) * 0.9f, 1f);
            bool yellow = f < 0.45f;
            if (y < 6 || y > h - 7) return new Color(0.1f, 0.1f, 0.1f);
            return yellow ? new Color(1f, 0.78f, 0.05f) : new Color(0.06f, 0.06f, 0.06f);
        }, TextureWrapMode.Clamp, FilterMode.Trilinear);

        public static Texture2D Grass() => SaveTexture("Grass", 256, 256, (x, y, w, h) =>
        {
            float n = Fbm(x * 0.04f, y * 0.04f) * 0.6f + Hash(x, y) * 0.4f;
            return Color.Lerp(new Color(0.14f, 0.24f, 0.11f), new Color(0.24f, 0.35f, 0.15f), n);
        });

        public static Texture2D Rock() => SaveTexture("Rock", 512, 512, (x, y, w, h) =>
        {
            float strata = Mathf.PerlinNoise(x * 0.004f, y * 0.04f);
            float n = Fbm(x * 0.02f, y * 0.02f) * 0.6f + Hash(x, y) * 0.15f + strata * 0.25f;
            return Color.Lerp(new Color(0.24f, 0.21f, 0.18f), new Color(0.5f, 0.45f, 0.38f), n);
        });

        public static Texture2D RockNormal() => SaveNormal("Rock_N", 256, 256, (x, y) => Fbm(x * 0.05f, y * 0.05f) * 0.8f + Mathf.PerlinNoise(x * 0.01f, y * 0.1f) * 0.2f, 3f);

        public static Texture2D Water() => SaveTexture("Water", 256, 256, (x, y, w, h) =>
        {
            float n = Fbm(x * 0.03f, y * 0.09f);
            return Color.Lerp(new Color(0.01f, 0.025f, 0.04f), new Color(0.025f, 0.05f, 0.07f), n);
        });

        public static Texture2D WaterNormal() => SaveNormal("Water_N", 256, 256, (x, y) => Mathf.PerlinNoise(x * 0.06f, y * 0.18f) * 0.6f + Mathf.PerlinNoise(x * 0.15f + 3f, y * 0.05f) * 0.4f, 1.8f);

        /// <summary>Corrugated sheet metal (roofs, containers, warehouses); tinted by material colour.</summary>
        public static Texture2D Corrugated() => SaveTexture("Corrugated", 256, 256, (x, y, w, h) =>
        {
            float rib = 0.97f + 0.03f * Mathf.Sin(x / (float)w * Mathf.PI * 2f * 16f);
            float rust = Fbm(x * 0.02f, y * 0.02f) > 0.62f ? 0.85f : 1f;
            float g = rib * rust * (0.92f + Hash(x, y) * 0.08f);
            return new Color(g, g, g);
        });

        public static Texture2D CorrugatedNormal() => SaveNormal("Corrugated_N", 256, 64, (x, y) => 0.5f + 0.5f * Mathf.Sin(x / 256f * Mathf.PI * 2f * 16f), 4f);

        public static Texture2D Awning() => SaveTexture("Awning", 128, 64, (x, y, w, h) =>
        {
            bool stripe = (x / 16) % 2 == 0;
            float n = 0.9f + Hash(x, y) * 0.1f;
            return (stripe ? Color.white : new Color(0.55f, 0.55f, 0.55f)) * n;
        });

        public static Texture2D Crowd() => SaveTexture("Crowd", 256, 64, (x, y, w, h) =>
        {
            int cx = x / 4, cy = y / 6;
            float r = Hash(cx * 7, cy * 13);
            if (r < 0.25f) return new Color(0.12f, 0.12f, 0.14f);
            return Color.HSVToRGB(Hash(cx, cy * 3), 0.5f, 0.45f + 0.4f * Hash(cx * 3, cy));
        });

        // ================================================================== Facades

        public enum FacadeKind { Plaster, Apartment, Office, Tower }

        /// <summary>
        /// 4 x 4 window-bay atlas (one bay = 3.2 m wide x one 3.2 m floor). Returns albedo; the
        /// matching emission map lights a selection of windows (restrained night look).
        /// </summary>
        public static Texture2D Facade(FacadeKind kind, out Texture2D emission) => Facade(kind, out emission, out _);

        /// <summary>Also returns a wall mask (alpha 1 = wall, 0 = glass) for layering a real surface on the walls only.</summary>
        public static Texture2D Facade(FacadeKind kind, out Texture2D emission, out Texture2D wallMask)
        {
            const int size = 512, bay = 128;
            var albedo = new Color[size * size];
            var emit = new Color[size * size];
            var wall = new Color[size * size];
            int seed = (int)kind * 101 + 7;
            for (int by = 0; by < 4; by++)
                for (int bx = 0; bx < 4; bx++)
                {
                    float r = Hash(bx, by, seed);
                    float litChance = kind == FacadeKind.Office ? 0.2f : kind == FacadeKind.Tower ? 0.16f : 0.3f;
                    bool lit = Hash(bx + 9, by + 3, seed) < litChance;
                    Color warm = Color.Lerp(new Color(1f, 0.66f, 0.32f), new Color(1f, 0.82f, 0.55f), r);
                    Color cool = Color.Lerp(new Color(0.75f, 0.85f, 1f), new Color(0.95f, 0.95f, 0.9f), r);
                    for (int y = 0; y < bay; y++)
                        for (int x = 0; x < bay; x++)
                        {
                            int px = bx * bay + x, py = by * bay + y;
                            Color a, e = Color.black;
                            float wm = 1f;
                            float wallN = 0.86f + Hash(px, py, 1) * 0.06f - Fbm(px * 0.02f, py * 0.02f) * 0.08f;
                            switch (kind)
                            {
                                case FacadeKind.Office:
                                case FacadeKind.Tower:
                                {
                                    int panels = kind == FacadeKind.Tower ? 4 : 2;
                                    int pw = bay / panels;
                                    bool mullion = x % pw < 3 || y < 18 || y > bay - 3;
                                    if (mullion) a = y < 18 ? new Color(0.42f, 0.43f, 0.45f) * wallN : new Color(0.55f, 0.57f, 0.6f);
                                    else
                                    {
                                        wm = 0f;
                                        int panel = x / pw;
                                        bool panelLit = lit && Hash(panel, py / bay, seed + px / bay) > 0.25f;
                                        float grad = 0.85f + 0.15f * (y / (float)bay);
                                        if (panelLit)
                                        {
                                            // Office interior: bright ceiling strip, dimmer room below, desk/partition silhouettes.
                                            float yy = (y - 18) / (float)(bay - 21);
                                            bool ceiling = y > bay - 24 && y < bay - 17;
                                            bool desk = y < 46 && Hash(px / 7, py / bay, seed + 5) > 0.55f;
                                            a = ceiling ? cool * 0.85f : cool * (0.16f + 0.22f * yy) * (desk ? 0.35f : 1f);
                                            e = ceiling ? cool * 1.1f : cool * (0.1f + 0.28f * yy) * (desk ? 0.3f : 1f);
                                        }
                                        else a = new Color(0.05f, 0.065f, 0.09f) * grad + new Color(0.03f, 0.035f, 0.05f) * (y / (float)bay);
                                    }
                                    break;
                                }
                                default:
                                {
                                    bool balcony = kind == FacadeKind.Apartment && bx % 2 == 1;
                                    int wx0 = balcony ? 30 : 38, wx1 = balcony ? 98 : 90;
                                    int wy0 = balcony ? 12 : 36, wy1 = balcony ? 104 : 100;
                                    bool frame = x >= wx0 - 4 && x < wx1 + 4 && y >= wy0 - 4 && y < wy1 + 4;
                                    bool glass = x >= wx0 && x < wx1 && y >= wy0 && y < wy1;
                                    bool sill = !balcony && y >= wy0 - 9 && y < wy0 - 4 && x >= wx0 - 8 && x < wx1 + 8;
                                    bool shutters = !lit && !balcony && r > 0.72f && glass;
                                    if (glass && !shutters)
                                    {
                                        wm = 0f;
                                        bool mullion = Mathf.Abs(x - (wx0 + wx1) / 2) < 2 || Mathf.Abs(y - (wy0 + wy1) * 0.62f) < 2;
                                        if (mullion) a = new Color(0.82f, 0.8f, 0.76f);
                                        else if (lit)
                                        {
                                            bool curtain = x < wx0 + 10 || x > wx1 - 10;
                                            a = curtain ? warm * 0.45f : warm * 0.7f;
                                            e = curtain ? warm * 0.4f : warm * 0.8f;
                                        }
                                        else a = new Color(0.08f, 0.1f, 0.12f) + new Color(0.06f, 0.07f, 0.08f) * (y - wy0) / (float)(wy1 - wy0);
                                    }
                                    else if (shutters) { a = (x % 8 < 2 ? new Color(0.3f, 0.22f, 0.16f) : new Color(0.42f, 0.31f, 0.21f)) * wallN; wm = 0.35f; }
                                    else if (frame || sill) { a = new Color(0.78f, 0.76f, 0.72f) * wallN; wm = 0.6f; }
                                    else a = new Color(wallN, wallN * 0.98f, wallN * 0.95f) * (y < 6 ? 0.85f : 1f);
                                    break;
                                }
                            }
                            albedo[py * size + px] = a;
                            emit[py * size + px] = e;
                            wall[py * size + px] = new Color(wm, wm, wm, wm);
                        }
                }
            string n = "Facade_" + kind;
            wallMask = SavePixels(n + "_M", size, size, wall, false);
            emission = SavePixels(n + "_E", size, size, emit, true);
            return SavePixels(n, size, size, albedo, true);
        }

        /// <summary>Ground-floor shop fronts: 4 variants of one 3.2 m bay (lit warm, shutter, lit cool, open stall).</summary>
        public static Texture2D Storefront(out Texture2D emission)
        {
            const int w = 512, h = 128, bay = 128;
            var albedo = new Color[w * h];
            var emit = new Color[w * h];
            for (int v = 0; v < 4; v++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < bay; x++)
                    {
                        int px = v * bay + x;
                        Color a, e = Color.black;
                        bool pillar = x < 8 || x > bay - 9 || y > 112;
                        if (pillar) a = new Color(0.55f, 0.53f, 0.5f) * (0.9f + Hash(px, y) * 0.1f);
                        else if (v == 1)
                        {
                            a = (y % 6 < 2 ? new Color(0.36f, 0.37f, 0.39f) : new Color(0.55f, 0.56f, 0.58f)) * (0.85f + Fbm(px * 0.05f, y * 0.05f) * 0.2f);
                        }
                        else
                        {
                            Color inside = v == 2 ? new Color(0.85f, 0.92f, 1f) : new Color(1f, 0.74f, 0.42f);
                            bool frame = (x > 60 && x < 66) || y < 10 || (y > 100 && y < 104);
                            bool goods = y > 22 && y < 70 && ((x / 10 + y / 14) % 3 != 0);
                            if (frame) a = new Color(0.2f, 0.2f, 0.22f);
                            else if (v == 3 && goods)
                            {
                                a = Color.HSVToRGB(Hash(px / 10, y / 14, 4), 0.6f, 0.75f);
                                e = a * 0.35f;
                            }
                            else
                            {
                                float shelf = (y % 22 < 3) ? 0.6f : 1f;
                                a = inside * 0.5f * shelf;
                                e = inside * 0.55f * shelf;
                            }
                        }
                        albedo[y * w + px] = a;
                        emit[y * w + px] = e;
                    }
            emission = SavePixels("Storefront_E", w, h, emit, true);
            return SavePixels("Storefront", w, h, albedo, true);
        }

        static Texture2D SavePixels(string name, int w, int h, Color[] pixels, bool sRGB)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, !sRGB);
            tex.SetPixels(pixels);
            string path = $"{TextureFolder}/{name}.png";
            WritePng(tex, path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.sRGBTexture = sRGB;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>8 x 8 colour palette for spectators (skin tones, clothing, hair). Cells are sampled by UV.</summary>
        public static readonly Color[] SpectatorPalette = BuildPalette();

        static Color[] BuildPalette()
        {
            var p = new Color[64];
            Color[] skin =
            {
                new Color(0.24f, 0.15f, 0.1f), new Color(0.31f, 0.19f, 0.12f), new Color(0.38f, 0.24f, 0.15f), new Color(0.45f, 0.29f, 0.18f),
                new Color(0.2f, 0.13f, 0.09f), new Color(0.52f, 0.35f, 0.23f), new Color(0.34f, 0.21f, 0.14f), new Color(0.28f, 0.17f, 0.11f)
            };
            Color[] clothes =
            {
                new Color(0.92f, 0.92f, 0.9f), new Color(0.1f, 0.1f, 0.11f), new Color(0.2f, 0.3f, 0.55f), new Color(0.65f, 0.12f, 0.1f),
                new Color(0.95f, 0.7f, 0.1f), new Color(0.12f, 0.45f, 0.25f), new Color(0.85f, 0.4f, 0.12f), new Color(0.45f, 0.45f, 0.48f),
                new Color(0.18f, 0.22f, 0.34f), new Color(0.6f, 0.5f, 0.35f), new Color(0.55f, 0.15f, 0.45f), new Color(0.1f, 0.55f, 0.65f),
                new Color(0.8f, 0.78f, 0.7f), new Color(0.32f, 0.2f, 0.12f), new Color(0.25f, 0.35f, 0.18f), new Color(0.9f, 0.55f, 0.6f),
                new Color(0.15f, 0.2f, 0.45f), new Color(0.7f, 0.2f, 0.2f), new Color(0.35f, 0.38f, 0.42f), new Color(0.95f, 0.85f, 0.3f),
                new Color(0.25f, 0.5f, 0.35f), new Color(0.5f, 0.25f, 0.15f), new Color(0.85f, 0.85f, 0.95f), new Color(0.4f, 0.12f, 0.15f)
            };
            for (int i = 0; i < 8; i++) p[i] = skin[i];
            for (int i = 0; i < clothes.Length; i++) p[8 + i] = clothes[i];
            p[40] = new Color(0.06f, 0.05f, 0.05f); // hair
            p[41] = new Color(0.12f, 0.08f, 0.06f);
            p[42] = new Color(0.08f, 0.08f, 0.09f); // shoes
            p[43] = new Color(0.85f, 0.85f, 0.85f);
            for (int i = 44; i < 64; i++) p[i] = clothes[i % clothes.Length];
            return p;
        }

        public static Texture2D PaletteTexture() => SaveTexture("SpectatorPalette", 8, 8, (x, y, w, h) => SpectatorPalette[y * 8 + x],
            TextureWrapMode.Clamp, FilterMode.Point);

        public static Vector2 PaletteUV(int cell) => new Vector2((cell % 8 + 0.5f) / 8f, (cell / 8 + 0.5f) / 8f);

        // ================================================================== UI sprites

        public static Sprite RoundedPanel()
        {
            const int size = 64, radius = 14;
            var sprite = SaveSprite("RoundedPanel", size, size, (x, y) =>
            {
                float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0f);
                float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0f);
                return Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
            }, new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
            return sprite;
        }

        public static Sprite Circle() => SaveSprite("Circle", 128, 128, (x, y) => Mathf.Clamp01(63.5f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64, 64)) + 0.5f), Vector4.zero);

        /// <summary>White icon on transparent background, rasterised from polygons (4x supersampled).</summary>
        public static Sprite Icon(string name, Func<Vector2, bool> inside) => SaveSprite(name, 128, 128, (x, y) =>
        {
            int hits = 0;
            for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                    if (inside(new Vector2((x + (sx + 0.5f) / 4f) / 128f, (y + (sy + 0.5f) / 4f) / 128f))) hits++;
            return hits / 16f;
        }, Vector4.zero);

        public static bool InPolygon(Vector2 p, Vector2[] poly)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) && (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)) c = !c;
            return c;
        }

        static Sprite SaveSprite(string name, int w, int h, Func<int, int, float> alpha, Vector4 border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha(x, y)));
            string path = $"{UIFolder}/{name}.png";
            WritePng(tex, path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        public static void CarCategoryIcons()
        {
            Vector2 V(float x, float y) => new Vector2(x, y);
            var body = new[] { V(0.08f, 0.38f), V(0.92f, 0.38f), V(0.92f, 0.56f), V(0.76f, 0.6f), V(0.63f, 0.76f), V(0.34f, 0.76f), V(0.24f, 0.6f), V(0.08f, 0.56f) };
            Icon("Icon_Car", p => (InPolygon(p, body) && !(Vector2.Distance(p, V(0.29f, 0.38f)) < 0.13f) && !(Vector2.Distance(p, V(0.71f, 0.38f)) < 0.13f))
                                 || Vector2.Distance(p, V(0.29f, 0.36f)) < 0.09f || Vector2.Distance(p, V(0.71f, 0.36f)) < 0.09f);
            var flame = new[] { V(0.5f, 0.92f), V(0.62f, 0.7f), V(0.76f, 0.55f), V(0.78f, 0.36f), V(0.68f, 0.18f), V(0.5f, 0.1f), V(0.32f, 0.18f), V(0.22f, 0.36f), V(0.26f, 0.56f), V(0.38f, 0.64f), V(0.42f, 0.78f) };
            var inner = new[] { V(0.5f, 0.5f), V(0.6f, 0.34f), V(0.56f, 0.2f), V(0.5f, 0.17f), V(0.44f, 0.2f), V(0.4f, 0.34f) };
            Icon("Icon_Flame", p => InPolygon(p, flame) && !InPolygon(p, inner));
            var bolt = new[] { V(0.58f, 0.92f), V(0.24f, 0.46f), V(0.47f, 0.46f), V(0.38f, 0.08f), V(0.76f, 0.56f), V(0.53f, 0.56f) };
            Icon("Icon_Bolt", p => InPolygon(p, bolt));
            Icon("Icon_Gauge", p =>
            {
                Vector2 c = V(0.5f, 0.34f);
                float d = Vector2.Distance(p, c);
                bool ring = d > 0.3f && d < 0.4f && p.y > 0.3f;
                Vector2 needleDir = V(0.55f, 0.83f).normalized;
                Vector2 rel = p - c;
                float along = Vector2.Dot(rel, needleDir), across = Mathf.Abs(rel.x * needleDir.y - rel.y * needleDir.x);
                bool needle = along > 0f && along < 0.32f && across < 0.035f;
                bool hub = d < 0.07f;
                return ring || needle || hub;
            });
            var mountain = new[] { V(0.06f, 0.2f), V(0.4f, 0.8f), V(0.56f, 0.55f), V(0.66f, 0.66f), V(0.94f, 0.2f) };
            var snow = new[] { V(0.31f, 0.64f), V(0.4f, 0.8f), V(0.49f, 0.66f), V(0.44f, 0.6f), V(0.38f, 0.66f) };
            Icon("Icon_Mountain", p => InPolygon(p, mountain) && !InPolygon(p, snow));
            var cup = new[] { V(0.24f, 0.88f), V(0.76f, 0.88f), V(0.72f, 0.6f), V(0.6f, 0.46f), V(0.56f, 0.36f), V(0.56f, 0.26f), V(0.68f, 0.2f), V(0.68f, 0.1f), V(0.32f, 0.1f), V(0.32f, 0.2f), V(0.44f, 0.26f), V(0.44f, 0.36f), V(0.4f, 0.46f), V(0.28f, 0.6f) };
            Icon("Trophy", p =>
            {
                float dl = Vector2.Distance(p, V(0.24f, 0.72f)), dr = Vector2.Distance(p, V(0.76f, 0.72f));
                bool handles = (dl > 0.08f && dl < 0.14f && p.x < 0.3f) || (dr > 0.08f && dr < 0.14f && p.x > 0.7f);
                return InPolygon(p, cup) || handles;
            });
        }

        // ================================================================== Audio

        public class SoundSet
        {
            public AudioClip Countdown, Go, Checkpoint, Lap, Finish, Warning, Click, Engine;
        }

        public static SoundSet Sounds() => new SoundSet
        {
            Countdown = SaveWav("Countdown", Notes(0.18f, 660f)),
            Go = SaveWav("Go", Notes(0.55f, 990f)),
            Checkpoint = SaveWav("Checkpoint", Notes(0.11f, 880f, 1320f)),
            Lap = SaveWav("Lap", Notes(0.12f, 660f, 880f, 1100f)),
            Finish = SaveWav("Finish", Notes(0.16f, 523f, 659f, 784f, 1046f, 1046f)),
            Warning = SaveWav("Warning", Buzz(0.32f, 160f)),
            Click = SaveWav("Click", Notes(0.04f, 1500f)),
            Engine = SaveWav("EngineLoop", EngineLoop())
        };

        const int SampleRate = 44100;

        static float[] Notes(float noteLength, params float[] freqs)
        {
            int per = Mathf.RoundToInt(noteLength * SampleRate);
            var data = new float[per * freqs.Length];
            for (int n = 0; n < freqs.Length; n++)
                for (int i = 0; i < per; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Min(1f, i / 200f) * Mathf.Exp(-3.5f * i / per);
                    float s = Mathf.Sin(2f * Mathf.PI * freqs[n] * t) * 0.7f + Mathf.Sin(4f * Mathf.PI * freqs[n] * t) * 0.15f;
                    data[n * per + i] = s * env * 0.6f;
                }
            return data;
        }

        static float[] Buzz(float length, float freq)
        {
            int count = Mathf.RoundToInt(length * SampleRate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * t));
                float env = Mathf.Min(1f, i / 300f) * Mathf.Clamp01((count - i) / 2000f);
                data[i] = square * env * 0.25f;
            }
            return data;
        }

        static float[] EngineLoop()
        {
            var data = new float[SampleRate];
            float[] harmonics = { 1f, 0.55f, 0.35f, 0.22f, 0.12f };
            const float fundamental = 55f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float s = 0f;
                for (int h = 0; h < harmonics.Length; h++) s += Mathf.Sin(2f * Mathf.PI * fundamental * (h + 1) * t) * harmonics[h];
                float pulse = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 27f * t);
                data[i] = s * pulse * 0.22f;
            }
            return data;
        }

        static AudioClip SaveWav(string name, float[] samples)
        {
            string path = $"{AudioFolder}/{name}.wav";
            using (var stream = new FileStream(Path.GetFullPath(path), FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                int byteCount = samples.Length * 2;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + byteCount);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(byteCount);
                foreach (float s in samples) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(s, -1f, 1f) * 32767f));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // ================================================================== Materials (URP Lit)

        static Shader lit;
        public static Shader Lit => lit != null ? lit : (lit = Shader.Find("Universal Render Pipeline/Lit"));

        public class MatSpec
        {
            public Color colour = Color.white;
            public float smoothness = 0.3f, metallic;
            public Texture albedo, normal, emissionMap;
            public float normalScale = 1f;
            public Vector2 tiling = Vector2.one;
            public Color? emission;
            public bool doubleSided;
        }

        public static Material Mat(string name, MatSpec s, string folder = MaterialFolder)
        {
            var mat = new Material(Lit) { name = name };
            mat.SetColor("_BaseColor", s.colour);
            mat.SetFloat("_Smoothness", s.smoothness);
            mat.SetFloat("_Metallic", s.metallic);
            if (s.albedo != null)
            {
                mat.SetTexture("_BaseMap", s.albedo);
                mat.SetTextureScale("_BaseMap", s.tiling);
            }
            if (s.normal != null)
            {
                mat.SetTexture("_BumpMap", s.normal);
                mat.SetFloat("_BumpScale", s.normalScale);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (s.emission.HasValue || s.emissionMap != null)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", s.emission ?? Color.white);
                if (s.emissionMap != null) mat.SetTexture("_EmissionMap", s.emissionMap);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (s.doubleSided)
            {
                mat.SetFloat("_Cull", 0f);
                mat.doubleSidedGI = true;
            }
            mat.enableInstancing = true;
            return SaveOrReplace(mat, $"{folder}/{name}.mat");
        }

        public static Material Mat(string name, Color colour, float smoothness = 0.3f, float metallic = 0f, Color? emission = null) =>
            Mat(name, new MatSpec { colour = colour, smoothness = smoothness, metallic = metallic, emission = emission });

        public static Material NightSky()
        {
            var mat = new Material(Shader.Find("Skybox/Procedural")) { name = "Sky_Night" };
            mat.SetFloat("_SunDisk", 0f);
            mat.SetFloat("_AtmosphereThickness", 0.28f);
            mat.SetColor("_SkyTint", new Color(0.14f, 0.19f, 0.4f));
            mat.SetColor("_GroundColor", new Color(0.03f, 0.03f, 0.035f));
            mat.SetFloat("_Exposure", 0.34f);
            return SaveOrReplace(mat, $"{MaterialFolder}/Sky_Night.mat");
        }
    }
}
