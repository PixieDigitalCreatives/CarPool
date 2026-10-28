using System.Collections.Generic;
using UnityEngine;

namespace RacingPrototype.EditorTools
{
    public enum BuildingType { House, Shop, Apartment, Office, Tower, Hotel, Warehouse, Civic }

    /// <summary>
    /// Modular building generator (PPT slides 6-9): small houses with boundary walls and gates,
    /// shops with storefronts, shutters, awnings and signs, mid-rise apartments with balconies,
    /// offices, tall skyline towers, waterfront hotels, port warehouses and civic buildings.
    /// Widths are whole 3.2 m window bays and heights whole 3.2 m floors, so facade textures
    /// never stretch. Meshes are cached and reused for identical modules.
    /// Local frame: front facade at z = -depth/2 facing -Z (towards the road), ground at y = 0.
    /// </summary>
    public static class BuildingKit
    {
        public const float Bay = 3.2f;
        public const float Floor = 3.2f;
        const float AtlasMetres = 12.8f;   // 4 bays / floors per facade texture

        const int SubFacade = 0, SubStore = 1, SubRoof = 2, SubTrim = 3, SubAwning = 4, SubSign = 5, SubDoor = 6;

        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        public static void ResetCache() => cache.Clear();

        public static string[] ShopNames =
        {
            "MINI MART", "PHARMACY", "MOBILE SHOP", "BAKERY", "SALON", "TAILOR", "CAFE", "HARDWARE", "FASHION", "ELECTRONICS",
            "BOOKSHOP", "FRESH FOODS", "BARBER", "PRINT SHOP", "SHOES", "SPICES"
        };

        public struct Spec
        {
            public BuildingType type;
            public int bays;
            public float depth;
            public int floors;
            public int variant;
            public float yard;  // houses: front yard depth inside the lot
        }

        /// <summary>Lot depth including any front yard (used for placement).</summary>
        public static float LotDepth(Spec s) => s.depth + s.yard;
        public static float Width(Spec s) => s.bays * Bay;
        public static float Height(Spec s) => s.floors * Floor;

        /// <summary>Picks a building module for a district.</summary>
        public static Spec Pick(BuildingType type, System.Random rng)
        {
            var s = new Spec { type = type, variant = rng.Next(0, 4) };
            switch (type)
            {
                case BuildingType.House: s.bays = rng.Next(2, 4); s.depth = 9f; s.floors = rng.Next(1, 3); s.yard = 2.6f; break;
                case BuildingType.Shop: s.bays = rng.Next(2, 5); s.depth = 10f; s.floors = rng.Next(1, 4); break;
                case BuildingType.Apartment: s.bays = rng.Next(3, 7); s.depth = 13f; s.floors = rng.Next(4, 8); break;
                case BuildingType.Office: s.bays = rng.Next(4, 8); s.depth = 16f; s.floors = rng.Next(6, 13); break;
                case BuildingType.Tower: s.bays = rng.Next(6, 10); s.depth = 22f; s.floors = rng.Next(16, 42); break;
                case BuildingType.Hotel: s.bays = rng.Next(6, 10); s.depth = 15f; s.floors = rng.Next(8, 15); break;
                case BuildingType.Warehouse: s.bays = rng.Next(7, 13); s.depth = rng.Next(20, 30); s.floors = 3; break;
                case BuildingType.Civic: s.bays = rng.Next(8, 12); s.depth = 20f; s.floors = 3; break;
            }
            return s;
        }

        public static GameObject Create(Spec s, CityMaterials m, Transform parent, Vector3 position, Quaternion rotation, System.Random rng, bool withSign)
        {
            string key = $"{s.type}_{s.bays}_{s.depth:0.0}_{s.floors}_{s.variant}_{s.yard:0.0}";
            if (!cache.TryGetValue(key, out var mesh))
            {
                mesh = BuildMesh(s);
                mesh.name = "Building_" + key;
                cache[key] = mesh;
            }

            var go = new GameObject(s.type + " " + s.bays + "x" + s.floors);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = Materials(s, m, rng);

            if (withSign && (s.type == BuildingType.Shop))
            {
                float w = Width(s);
                string name = ShopNames[rng.Next(ShopNames.Length)];
                UIFactory.WorldText("Sign", go.transform, name, new Vector3(0f, Floor + 0.36f, -s.depth * 0.5f - 0.2f), Quaternion.identity,
                    new Vector2(Mathf.Min(w - 1f, 7f), 0.55f), new Color(1f, 0.97f, 0.9f), FontStyle.Bold, 120);
            }
            return go;
        }

        static Material[] Materials(Spec s, CityMaterials m, System.Random rng)
        {
            Material facade;
            switch (s.type)
            {
                case BuildingType.Office: facade = m.Office[s.variant % m.Office.Length]; break;
                case BuildingType.Tower: facade = m.Tower[s.variant % m.Tower.Length]; break;
                case BuildingType.Apartment:
                case BuildingType.Hotel: facade = m.Apartment[s.variant % m.Apartment.Length]; break;
                case BuildingType.Warehouse: facade = m.Warehouse; break;
                default: facade = m.Plaster[rng.Next(m.Plaster.Length)]; break;
            }
            Material roof = s.type == BuildingType.House || s.type == BuildingType.Warehouse ? m.RoofMetal[rng.Next(m.RoofMetal.Length)] : m.Roof;
            return new[]
            {
                facade, m.Storefront, roof, m.BuildingTrim, m.Awning[rng.Next(m.Awning.Length)], m.Sign[rng.Next(m.Sign.Length)],
                s.type == BuildingType.Warehouse ? m.WarehouseDoor : m.Gate
            };
        }

        static Mesh BuildMesh(Spec s)
        {
            var kit = new MeshKit(7);
            float w = Width(s), d = s.depth, h = Height(s);
            float hx = w * 0.5f, hz = d * 0.5f;
            int seed = s.variant * 31 + s.bays * 7 + s.floors;
            float u0 = (seed % 4) * 0.25f, v0 = ((seed / 4) % 4) * 0.25f;
            bool storefront = s.type == BuildingType.Shop || s.type == BuildingType.Office || s.type == BuildingType.Hotel || s.type == BuildingType.Civic;
            float facadeBottom = storefront ? Floor : 0f;

            if (s.type == BuildingType.Warehouse)
            {
                // Corrugated walls: U along the wall at 1 tile / 4 m, V up.
                void WallW(Vector3 a, Vector3 b, Vector3 n, float len)
                {
                    kit.QuadUV(a, b, b + Vector3.up * h, a + Vector3.up * h, new Vector2(0, 0), new Vector2(len / 4f, 0), new Vector2(len / 4f, h / 4f), new Vector2(0, h / 4f), n, SubFacade);
                }
                WallW(new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), Vector3.back, w);
                WallW(new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz), Vector3.forward, w);
                WallW(new Vector3(-hx, 0, hz), new Vector3(-hx, 0, -hz), Vector3.left, d);
                WallW(new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), Vector3.right, d);
                // Roller doors on the front.
                int doors = Mathf.Max(1, s.bays / 3);
                for (int i = 0; i < doors; i++)
                {
                    float cx = -hx + (i + 0.5f) * w / doors;
                    kit.Box(new Vector3(cx, 2.6f, -hz - 0.03f), new Vector3(4.6f, 5.2f, 0.06f), Quaternion.identity, SubDoor);
                }
                // Shallow pitched corrugated roof.
                float ridge = h + 1.6f;
                kit.QuadUV(new Vector3(-hx - 0.3f, h, -hz - 0.3f), new Vector3(hx + 0.3f, h, -hz - 0.3f), new Vector3(hx + 0.3f, ridge, 0), new Vector3(-hx - 0.3f, ridge, 0),
                    Vector2.zero, new Vector2(w / 4f, 0), new Vector2(w / 4f, hz / 4f), new Vector2(0, hz / 4f), new Vector3(0, 1, -0.6f), SubRoof);
                kit.QuadUV(new Vector3(hx + 0.3f, h, hz + 0.3f), new Vector3(-hx - 0.3f, h, hz + 0.3f), new Vector3(-hx - 0.3f, ridge, 0), new Vector3(hx + 0.3f, ridge, 0),
                    Vector2.zero, new Vector2(w / 4f, 0), new Vector2(w / 4f, hz / 4f), new Vector2(0, hz / 4f), new Vector3(0, 1, 0.6f), SubRoof);
                foreach (float sx in new[] { -hx, hx })
                {
                    int a = kit.Add(new Vector3(sx, h, -hz), Vector2.zero), b = kit.Add(new Vector3(sx, h, hz), new Vector2(d / 4f, 0)), c = kit.Add(new Vector3(sx, ridge, 0), new Vector2(hz / 4f, 0.4f));
                    kit.Tri(a, b, c, new Vector3(Mathf.Sign(sx), 0, 0), SubFacade);
                }
                return kit.Build("Warehouse");
            }

            // Facade walls (window atlas).
            void Wall(Vector3 a, Vector3 b, Vector3 n, float len, float y0, float y1, float uStart)
            {
                Vector3 up0 = Vector3.up * y0, up1 = Vector3.up * y1;
                kit.QuadUV(a + up0, b + up0, b + up1, a + up1,
                    new Vector2(uStart, v0 + y0 / AtlasMetres), new Vector2(uStart + len / AtlasMetres, v0 + y0 / AtlasMetres),
                    new Vector2(uStart + len / AtlasMetres, v0 + y1 / AtlasMetres), new Vector2(uStart, v0 + y1 / AtlasMetres), n, SubFacade);
            }
            Wall(new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), Vector3.back, w, facadeBottom, h, u0);
            Wall(new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz), Vector3.forward, w, 0f, h, u0 + 0.5f);
            Wall(new Vector3(-hx, 0, hz), new Vector3(-hx, 0, -hz), Vector3.left, d, 0f, h, u0 + 0.25f);
            Wall(new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), Vector3.right, d, 0f, h, u0 + 0.75f);

            if (storefront)
            {
                for (int i = 0; i < s.bays; i++)
                {
                    int variant = s.type == BuildingType.Office || s.type == BuildingType.Civic ? 2 : (int)(ProceduralAssets.Hash(i, seed, 11) * 4f) % 4;
                    if (s.type == BuildingType.Hotel) variant = i % 2 == 0 ? 0 : 2;
                    float x0 = -hx + i * Bay, x1 = x0 + Bay;
                    kit.QuadUV(new Vector3(x0, 0, -hz), new Vector3(x1, 0, -hz), new Vector3(x1, Floor, -hz), new Vector3(x0, Floor, -hz),
                        new Vector2(variant / 4f, 0), new Vector2((variant + 1) / 4f, 0), new Vector2((variant + 1) / 4f, 1), new Vector2(variant / 4f, 1), Vector3.back, SubStore);
                }
                // Fascia band between shop front and upper floors.
                kit.Box(new Vector3(0, Floor + 0.05f, -hz - 0.08f), new Vector3(w, 0.22f, 0.16f), Quaternion.identity, SubTrim);
            }

            if (s.type == BuildingType.Shop)
            {
                // Sloped awning over the shop front (double-sided material) and a sign board.
                float aw = w - 0.4f;
                kit.QuadUV(new Vector3(-aw / 2, Floor - 0.25f, -hz), new Vector3(aw / 2, Floor - 0.25f, -hz), new Vector3(aw / 2, Floor - 0.85f, -hz - 1.5f), new Vector3(-aw / 2, Floor - 0.85f, -hz - 1.5f),
                    new Vector2(0, 1), new Vector2(aw / 2f, 1), new Vector2(aw / 2f, 0), new Vector2(0, 0), new Vector3(0, 1, -0.4f), SubAwning);
                kit.Box(new Vector3(0, Floor + 0.36f, -hz - 0.1f), new Vector3(Mathf.Min(w - 0.6f, 7.6f), 0.72f, 0.14f), Quaternion.identity, SubSign);
            }

            if (s.type == BuildingType.Apartment || s.type == BuildingType.Hotel)
            {
                for (int f = 1; f < s.floors; f++)
                    for (int i = 1; i < s.bays; i += 2)
                    {
                        float cx = -hx + (i + 0.5f) * Bay;
                        float y = f * Floor;
                        kit.Box(new Vector3(cx, y + 0.07f, -hz - 0.55f), new Vector3(Bay - 0.5f, 0.14f, 1.1f), Quaternion.identity, SubTrim);
                        kit.Box(new Vector3(cx, y + 0.6f, -hz - 1.07f), new Vector3(Bay - 0.5f, 0.95f, 0.06f), Quaternion.identity, SubTrim);
                    }
                if (s.type == BuildingType.Apartment)
                    kit.Box(new Vector3(0, 1.1f, -hz - 0.02f), new Vector3(1.4f, 2.2f, 0.06f), Quaternion.identity, SubDoor);
            }

            if (s.type == BuildingType.House)
            {
                // Pitched metal roof with overhang + gable ends.
                float ridge = h + 1.9f, ov = 0.45f;
                kit.QuadUV(new Vector3(-hx - ov, h - 0.2f, -hz - ov), new Vector3(hx + ov, h - 0.2f, -hz - ov), new Vector3(hx + ov, ridge, 0), new Vector3(-hx - ov, ridge, 0),
                    Vector2.zero, new Vector2(w / 4f, 0), new Vector2(w / 4f, 1.4f), new Vector2(0, 1.4f), new Vector3(0, 1, -0.7f), SubRoof);
                kit.QuadUV(new Vector3(hx + ov, h - 0.2f, hz + ov), new Vector3(-hx - ov, h - 0.2f, hz + ov), new Vector3(-hx - ov, ridge, 0), new Vector3(hx + ov, ridge, 0),
                    Vector2.zero, new Vector2(w / 4f, 0), new Vector2(w / 4f, 1.4f), new Vector2(0, 1.4f), new Vector3(0, 1, 0.7f), SubRoof);
                foreach (float sx in new[] { -hx, hx })
                {
                    int a = kit.Add(new Vector3(sx, h, -hz), new Vector2(u0, v0)), b = kit.Add(new Vector3(sx, h, hz), new Vector2(u0 + 0.2f, v0)), c = kit.Add(new Vector3(sx, ridge, 0), new Vector2(u0 + 0.1f, v0 + 0.1f));
                    kit.Tri(a, b, c, new Vector3(Mathf.Sign(sx), 0, 0), SubFacade);
                }
                // Front door, boundary wall with gate (yard in front of the house).
                kit.Box(new Vector3(-hx + 1.6f, 1.05f, -hz - 0.02f), new Vector3(1.0f, 2.1f, 0.06f), Quaternion.identity, SubDoor);
                float wz = -hz - s.yard;
                float gate = 2.6f;
                kit.Box(new Vector3((-hx + (-gate / 2)) / 2f, 0.85f, wz), new Vector3(hx - gate / 2, 1.7f, 0.25f), Quaternion.identity, SubTrim, 2f);
                kit.Box(new Vector3((hx + gate / 2) / 2f, 0.85f, wz), new Vector3(hx - gate / 2, 1.7f, 0.25f), Quaternion.identity, SubTrim, 2f);
                kit.Box(new Vector3(0, 0.75f, wz), new Vector3(gate, 1.5f, 0.06f), Quaternion.identity, SubDoor);
                kit.Box(new Vector3(-hx + 0.12f, 0.85f, wz + s.yard / 2), new Vector3(0.25f, 1.7f, s.yard), Quaternion.identity, SubTrim, 2f);
                kit.Box(new Vector3(hx - 0.12f, 0.85f, wz + s.yard / 2), new Vector3(0.25f, 1.7f, s.yard), Quaternion.identity, SubTrim, 2f);
            }
            else
            {
                // Flat roof with parapet and rooftop plant.
                kit.QuadUV(new Vector3(-hx, h, -hz), new Vector3(hx, h, -hz), new Vector3(hx, h, hz), new Vector3(-hx, h, hz),
                    Vector2.zero, new Vector2(w / 6f, 0), new Vector2(w / 6f, d / 6f), new Vector2(0, d / 6f), Vector3.up, SubRoof);
                float pt = 0.25f, ph = 0.7f;
                kit.Box(new Vector3(0, h + ph / 2, -hz + pt / 2), new Vector3(w, ph, pt), Quaternion.identity, SubTrim);
                kit.Box(new Vector3(0, h + ph / 2, hz - pt / 2), new Vector3(w, ph, pt), Quaternion.identity, SubTrim);
                kit.Box(new Vector3(-hx + pt / 2, h + ph / 2, 0), new Vector3(pt, ph, d), Quaternion.identity, SubTrim);
                kit.Box(new Vector3(hx - pt / 2, h + ph / 2, 0), new Vector3(pt, ph, d), Quaternion.identity, SubTrim);
                if (s.type == BuildingType.Shop || s.type == BuildingType.Apartment)
                {
                    kit.Cylinder(new Vector3(hx * 0.4f, h, hz * 0.3f), 0.8f, 1.6f, 10, SubTrim);   // water tank
                    kit.Box(new Vector3(-hx * 0.4f, h + 0.5f, hz * 0.2f), new Vector3(1.4f, 1f, 1f), Quaternion.identity, SubTrim);
                }
                if (s.type == BuildingType.Office || s.type == BuildingType.Hotel)
                    kit.Box(new Vector3(0, h + 1.6f, hz * 0.2f), new Vector3(w * 0.45f, 3.2f, d * 0.4f), Quaternion.identity, SubTrim, 4f);
                if (s.type == BuildingType.Tower)
                {
                    // Setback crown with a lit band and antenna: a readable skyline silhouette.
                    float cw = w * 0.7f, cd = d * 0.7f, ch = Floor * 2f;
                    kit.Box(new Vector3(0, h + ch / 2, 0), new Vector3(cw, ch, cd), Quaternion.identity, SubFacade, AtlasMetres);
                    kit.Box(new Vector3(0, h + ch + 0.3f, 0), new Vector3(cw + 0.3f, 0.6f, cd + 0.3f), Quaternion.identity, SubSign);
                    kit.Box(new Vector3(0, h + ch + 6f, 0), new Vector3(0.4f, 11f, 0.4f), Quaternion.identity, SubTrim);
                }
                if (s.type == BuildingType.Civic)
                {
                    for (int i = 0; i <= s.bays; i += 2)
                        kit.Cylinder(new Vector3(-hx + i * Bay, 0, -hz - 1.6f), 0.45f, h - 0.4f, 12, SubTrim);
                    kit.Box(new Vector3(0, h - 0.2f, -hz - 1.0f), new Vector3(w + 0.6f, 0.4f, 2.6f), Quaternion.identity, SubTrim);
                    kit.Box(new Vector3(0, 0.15f, -hz - 1.4f), new Vector3(w + 1.6f, 0.3f, 3.4f), Quaternion.identity, SubTrim);
                }
            }
            return kit.Build("Building");
        }
    }
}
