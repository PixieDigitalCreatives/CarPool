using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RacingPrototype.EditorTools
{
    /// <summary>
    /// Reusable street dressing (PPT slides 8, 9, 14): street lights, palms and street trees,
    /// direction boards, benches and bins, market stalls, containers, port cranes, a ship, the
    /// civic-square monument, thegameio billboards and low-poly spectators.
    /// </summary>
    public class PropKit
    {
        public GameObject StreetLight, Palm, Tree, Bench, Bin, Stall, Chevron, Billboard, Crane, Monument, Bollard, MastLight;
        // Scanned real-world props (Poly Haven, CC0); null when not downloaded.
        public GameObject HeritageLamp, Shutter, UtilityBox, Manhole, RoadBarrier, Barrel;
        public bool ScannedTrees;
        public Mesh ContainerMesh, CubeMesh, SphereMesh;
        public readonly List<Mesh> Spectators = new List<Mesh>();

        const string PrefabFolder = "Assets/Prefabs/Environment";
        const string MeshFolder = "Assets/Models/Environment";

        public static PropKit Build(CityMaterials m)
        {
            var kit = new PropKit();
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            kit.CubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);
            tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            kit.SphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);

            kit.StreetLight = kit.SaveStreetLight(m);
            kit.MastLight = kit.SaveMastLight(m);
            kit.Palm = kit.SavePalm(m);
            kit.Tree = kit.SaveTree(m);
            kit.Bench = SavePrefab("Bench", m, k =>
            {
                k.Box(new Vector3(0, 0.45f, 0), new Vector3(1.8f, 0.08f, 0.45f), Quaternion.identity, 0);
                k.Box(new Vector3(0, 0.8f, 0.2f), new Vector3(1.8f, 0.4f, 0.06f), Quaternion.identity, 0);
                foreach (float x in new[] { -0.8f, 0.8f }) k.Box(new Vector3(x, 0.22f, 0), new Vector3(0.08f, 0.44f, 0.4f), Quaternion.identity, 1);
            }, m.Wood, m.Pole);
            kit.Bin = SavePrefab("Bin", m, k => k.Cylinder(Vector3.zero, 0.28f, 0.9f, 10, 0), m.Bin);
            kit.Bollard = SavePrefab("Bollard", m, k => k.Cylinder(Vector3.zero, 0.12f, 0.9f, 8, 0), m.CraneRed);
            kit.Stall = SavePrefab("MarketStall", m, k =>
            {
                foreach (var p in new[] { new Vector3(-1.4f, 0, -0.9f), new Vector3(1.4f, 0, -0.9f), new Vector3(-1.4f, 0, 0.9f), new Vector3(1.4f, 0, 0.9f) })
                    k.Box(p + Vector3.up * 1.15f, new Vector3(0.08f, 2.3f, 0.08f), Quaternion.identity, 0);
                k.Box(new Vector3(0, 0.85f, 0), new Vector3(2.9f, 0.08f, 1.9f), Quaternion.identity, 0);
                k.QuadUV(new Vector3(-1.6f, 2.15f, -1.2f), new Vector3(1.6f, 2.15f, -1.2f), new Vector3(1.6f, 2.55f, 1.1f), new Vector3(-1.6f, 2.55f, 1.1f),
                    Vector2.zero, new Vector2(1.6f, 0), new Vector2(1.6f, 1), new Vector2(0, 1), Vector3.up, 1);
                var rng = new System.Random(5);
                for (int i = 0; i < 6; i++)
                    k.Box(new Vector3(-1.1f + i * 0.44f, 1.02f, (float)rng.NextDouble() * 0.6f - 0.3f), new Vector3(0.36f, 0.26f, 0.5f), Quaternion.Euler(0, rng.Next(-20, 20), 0), 2);
            }, m.Wood, m.Awning[1], m.Container[2]);
            kit.Chevron = SavePrefab("DirectionBoard", m, k =>
            {
                foreach (float x in new[] { -1.3f, 1.3f }) k.Box(new Vector3(x, 0.8f, 0.05f), new Vector3(0.1f, 1.6f, 0.1f), Quaternion.identity, 1);
                k.QuadUV(new Vector3(-1.6f, 1.2f, 0), new Vector3(1.6f, 1.2f, 0), new Vector3(1.6f, 2.0f, 0), new Vector3(-1.6f, 2.0f, 0),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), Vector3.back, 0);
                k.Box(new Vector3(0, 1.6f, 0.04f), new Vector3(3.3f, 0.9f, 0.06f), Quaternion.identity, 1);
            }, m.Chevron, m.Pole);
            kit.Billboard = kit.SaveBillboard(m);
            kit.Crane = kit.SaveCrane(m);
            kit.Monument = kit.SaveMonument(m);

            var cont = new MeshKit();
            cont.Box(new Vector3(0, 1.3f, 0), new Vector3(6.06f, 2.59f, 2.44f), Quaternion.identity, 0, 2.6f);
            kit.ContainerMesh = ProceduralAssets.SaveOrReplace(cont.Build("Container"), $"{MeshFolder}/Container.asset");

            kit.BuildSpectators();
            kit.LoadScannedProps();
            return kit;
        }

        /// <summary>Real props replace the generated trees where available; the rest add street detail.</summary>
        void LoadScannedProps()
        {
            // The tree scans are millions of triangles each: billboard impostors of them line the routes.
            var streetTree = PolyHaven.Impostor("tree_small_02");
            var coastTree = PolyHaven.Impostor("island_tree_02");
            if (streetTree != null || coastTree != null)
            {
                Tree = streetTree != null ? streetTree : coastTree;
                Palm = coastTree != null ? coastTree : streetTree;   // coastal districts get the windswept island tree
                ScannedTrees = true;
            }
            HeritageLamp = PolyHaven.Prefab("street_lamp_01", 32000, 0.015f, (go, b) => AddLampLight(go, new Vector3(b.center.x, b.max.y - 0.5f, b.center.z), 12f, 5f));
            Shutter = PolyHaven.Prefab("rollershutter_door", 2000, 0.008f);
            UtilityBox = PolyHaven.Prefab("utility_box_01", 5000, 0.015f);
            Manhole = PolyHaven.Prefab("water_manhole_cover", 7000, 0.01f);
            RoadBarrier = PolyHaven.Prefab("concrete_road_barrier_02", 6000, 0.012f);
            Barrel = PolyHaven.Prefab("Barrel_01", 3000, 0.012f);
        }

        static void AddLampLight(GameObject lamp, Vector3 localPosition, float range, float intensity)
        {
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(lamp.transform, false);
            lightGo.transform.localPosition = localPosition;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = new Color(1f, 0.8f, 0.55f);
            light.shadows = LightShadows.None;
        }

        static GameObject SavePrefab(string name, CityMaterials m, System.Action<MeshKit> build, params Material[] mats)
        {
            var k = new MeshKit(mats.Length);
            build(k);
            var mesh = ProceduralAssets.SaveOrReplace(k.Build(name), $"{MeshFolder}/{name}.asset");
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        GameObject SaveStreetLight(CityMaterials m)
        {
            var k = new MeshKit(2);
            k.Cylinder(Vector3.zero, 0.16f, 0.5f, 10, 0);
            k.Cylinder(new Vector3(0, 0.5f, 0), 0.09f, 8f, 10, 0, 0.07f);
            k.Box(new Vector3(0, 8.35f, 1.1f), new Vector3(0.1f, 0.1f, 2.3f), Quaternion.Euler(-4f, 0, 0), 0);
            k.Box(new Vector3(0, 8.3f, 2.25f), new Vector3(0.36f, 0.14f, 0.7f), Quaternion.identity, 0);
            k.Box(new Vector3(0, 8.21f, 2.25f), new Vector3(0.3f, 0.04f, 0.6f), Quaternion.identity, 1);
            var mesh = ProceduralAssets.SaveOrReplace(k.Build("StreetLight"), $"{MeshFolder}/StreetLight.asset");
            var go = new GameObject("StreetLight");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.Pole, m.LampGlow };
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 8.0f, 2.4f);
            lightGo.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 118f;
            light.innerSpotAngle = 60f;
            light.range = 26f;
            light.intensity = 9f;
            light.color = new Color(1f, 0.78f, 0.52f);   // ~3000 K sodium-white
            light.shadows = LightShadows.None;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/StreetLight.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        GameObject SaveMastLight(CityMaterials m)
        {
            var k = new MeshKit(2);
            k.Cylinder(Vector3.zero, 0.35f, 22f, 10, 0, 0.22f);
            k.Box(new Vector3(0, 22.2f, 0), new Vector3(3.2f, 0.3f, 0.6f), Quaternion.identity, 0);
            for (int i = 0; i < 4; i++) k.Box(new Vector3(-1.2f + i * 0.8f, 21.95f, 0), new Vector3(0.5f, 0.2f, 0.5f), Quaternion.identity, 1);
            var mesh = ProceduralAssets.SaveOrReplace(k.Build("MastLight"), $"{MeshFolder}/MastLight.asset");
            var go = new GameObject("MastLight");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.Pole, m.LampGlow };
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 21.5f, 0);
            lightGo.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 125f;
            light.range = 48f;
            light.intensity = 14f;
            light.color = new Color(1f, 0.7f, 0.42f);
            light.shadows = LightShadows.None;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/MastLight.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        GameObject SavePalm(CityMaterials m)
        {
            var k = new MeshKit(2);
            Vector3 p = Vector3.zero;
            float radius = 0.22f;
            for (int i = 0; i < 7; i++)
            {
                Vector3 next = p + new Vector3(0.12f * i * 0.18f, 1.25f, 0f);
                k.Cylinder(p, radius, 1.28f, 8, 0, radius * 0.93f);
                p = next;
                radius *= 0.93f;
            }
            Vector3 crown = p;
            for (int f = 0; f < 9; f++)
            {
                float yaw = f * 40f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                Vector3 prev = crown;
                for (int s = 1; s <= 4; s++)
                {
                    float t = s / 4f;
                    Vector3 pt = crown + dir * (t * 3.0f) + Vector3.up * (0.5f * Mathf.Sin(t * Mathf.PI) - t * t * 1.9f);
                    float wPrev = 0.34f * Mathf.Sin((s - 1) / 4f * Mathf.PI) + 0.06f, wCur = 0.34f * Mathf.Sin(t * Mathf.PI) * (1f - t * 0.3f) + 0.03f;
                    k.QuadUV(prev - side * wPrev, prev + side * wPrev, pt + side * wCur, pt - side * wCur, new Vector2(0, t - 0.25f), new Vector2(1, t - 0.25f), new Vector2(1, t), new Vector2(0, t), Vector3.up, 1);
                    prev = pt;
                }
            }
            var mesh = ProceduralAssets.SaveOrReplace(k.Build("Palm"), $"{MeshFolder}/Palm.asset");
            var go = new GameObject("Palm");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.PalmTrunk, m.PalmLeaf };
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/Palm.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        GameObject SaveTree(CityMaterials m)
        {
            var root = new GameObject("StreetTree");
            var trunk = new MeshKit();
            trunk.Cylinder(Vector3.zero, 0.2f, 3f, 8, 0, 0.14f);
            var trunkMesh = ProceduralAssets.SaveOrReplace(trunk.Build("TreeTrunk"), $"{MeshFolder}/TreeTrunk.asset");
            var t = new GameObject("Trunk");
            t.transform.SetParent(root.transform, false);
            t.AddComponent<MeshFilter>().sharedMesh = trunkMesh;
            t.AddComponent<MeshRenderer>().sharedMaterial = m.Trunk;
            Vector3[] crowns = { new Vector3(0, 4.2f, 0), new Vector3(0.9f, 3.8f, 0.3f), new Vector3(-0.7f, 3.9f, -0.5f) };
            float[] sizes = { 3.6f, 2.6f, 2.6f };
            for (int i = 0; i < crowns.Length; i++)
            {
                var c = new GameObject("Crown " + i);
                c.transform.SetParent(root.transform, false);
                c.transform.localPosition = crowns[i];
                c.transform.localScale = new Vector3(sizes[i], sizes[i] * 0.82f, sizes[i]);
                c.AddComponent<MeshFilter>().sharedMesh = SphereMesh;
                c.AddComponent<MeshRenderer>().sharedMaterial = m.Leaves;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/StreetTree.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        GameObject SaveBillboard(CityMaterials m)
        {
            var root = new GameObject("Billboard_thegameio");
            var k = new MeshKit(2);
            foreach (float x in new[] { -3.2f, 3.2f }) k.Box(new Vector3(x, 2.6f, 0.25f), new Vector3(0.3f, 5.2f, 0.3f), Quaternion.identity, 1);
            k.Box(new Vector3(0, 5.6f, 0.12f), new Vector3(9.4f, 3.6f, 0.2f), Quaternion.identity, 1);
            k.QuadUV(new Vector3(-4.5f, 3.95f, -0.01f), new Vector3(4.5f, 3.95f, -0.01f), new Vector3(4.5f, 7.25f, -0.01f), new Vector3(-4.5f, 7.25f, -0.01f),
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.back, 0);
            var mesh = ProceduralAssets.SaveOrReplace(k.Build("Billboard"), $"{MeshFolder}/Billboard.asset");
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.BillboardFace, m.BillboardFrame };
            var brand = BrandConfig.Current;
            UIFactory.WorldText("Wordmark", root.transform, brand.wordmark, new Vector3(0, 6.05f, -0.03f), Quaternion.identity, new Vector2(8.2f, 1.8f), brand.primary, FontStyle.Bold, 300);
            UIFactory.WorldText("Tagline", root.transform, brand.productLine.ToUpperInvariant(), new Vector3(0, 4.6f, -0.03f), Quaternion.identity, new Vector2(8f, 0.6f), Color.white, FontStyle.Bold, 120);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/Billboard_thegameio.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        GameObject SaveCrane(CityMaterials m)
        {
            var k = new MeshKit(2);
            foreach (float x in new[] { -6f, 6f })
                foreach (float z in new[] { -5f, 5f })
                    k.Box(new Vector3(x, 15f, z), new Vector3(1f, 30f, 1f), Quaternion.identity, (x > 0) == (z > 0) ? 0 : 1);
            k.Box(new Vector3(0, 30.5f, 0), new Vector3(14f, 1.5f, 12f), Quaternion.identity, 0);
            k.Box(new Vector3(0, 33f, -14f), new Vector3(2f, 2f, 52f), Quaternion.identity, 1);
            k.Box(new Vector3(0, 36f, 4f), new Vector3(7f, 4f, 5f), Quaternion.identity, 0);
            for (int i = 0; i < 6; i++) k.Box(new Vector3(0, 33f + (i % 2) * 1.8f, -36f + i * 9f), new Vector3(0.3f, 4f, 0.3f), Quaternion.Euler(0, 0, 30f), 1);
            var mesh = ProceduralAssets.SaveOrReplace(k.Build("Crane"), $"{MeshFolder}/Crane.asset");
            var go = new GameObject("PortCrane");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.CraneRed, m.CraneWhite };
            var lamp = new GameObject("Beacon");
            lamp.transform.SetParent(go.transform, false);
            lamp.transform.localPosition = new Vector3(0, 38.5f, 4f);
            lamp.AddComponent<MeshFilter>().sharedMesh = CubeMesh;
            lamp.AddComponent<MeshRenderer>().sharedMaterial = m.TailLightProxy();
            lamp.transform.localScale = Vector3.one * 0.6f;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/PortCrane.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        GameObject SaveMonument(CityMaterials m)
        {
            var k = new MeshKit(2);
            for (int i = 0; i < 3; i++) k.Box(new Vector3(0, 0.25f + i * 0.5f, 0), new Vector3(9f - i * 2.2f, 0.5f, 9f - i * 2.2f), Quaternion.identity, 0, 2f);
            k.Cylinder(new Vector3(0, 1.5f, 0), 1.3f, 14f, 4, 1, 0.75f);
            k.Cylinder(new Vector3(0, 15.5f, 0), 0.75f, 1.6f, 4, 1, 0.05f);
            var mesh = ProceduralAssets.SaveOrReplace(k.Build("Monument"), $"{MeshFolder}/Monument.asset");
            var go = new GameObject("CivicMonument");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.Stone, m.Monument };
            foreach (var p in new[] { new Vector3(4.3f, 0.6f, 4.3f), new Vector3(-4.3f, 0.6f, -4.3f) })
            {
                var l = new GameObject("Uplight");
                l.transform.SetParent(go.transform, false);
                l.transform.localPosition = p;
                l.transform.LookAt(go.transform.position + Vector3.up * 9f);
                var light = l.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 40f;
                light.range = 22f;
                light.intensity = 10f;
                light.color = new Color(1f, 0.86f, 0.66f);
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/CivicMonument.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>
        /// 24 low-poly spectator variants in contemporary everyday clothing, all sharing one palette
        /// material (skin tones from the palette's first row), so the crowd batches cheaply.
        /// </summary>
        void BuildSpectators()
        {
            var rng = new System.Random(77);
            for (int v = 0; v < 24; v++)
            {
                var k = new MeshKit();
                int skin = rng.Next(0, 8);
                int top = 8 + rng.Next(0, 24);
                int bottom = 8 + new[] { 1, 2, 7, 8, 9, 13, 16, 18 }[rng.Next(8)];
                int hair = 40 + rng.Next(0, 2);
                bool dress = rng.NextDouble() < 0.25;
                bool shorts = !dress && rng.NextDouble() < 0.2;
                bool cap = rng.NextDouble() < 0.3;
                int pose = rng.Next(0, 4); // 0 arms down, 1 one arm up, 2 both up, 3 hands on barrier
                Vector2 Uv(int cell) => ProceduralAssets.PaletteUV(cell);
                void B(Vector3 c, Vector3 s, int cell, float rotX = 0f) => k.Box(c, s, Quaternion.Euler(rotX, 0, 0), 0, 0f, false, Uv(cell));

                B(new Vector3(-0.1f, 0.04f, 0.03f), new Vector3(0.12f, 0.08f, 0.26f), 42);
                B(new Vector3(0.1f, 0.04f, 0.03f), new Vector3(0.12f, 0.08f, 0.26f), 42);
                if (dress)
                {
                    B(new Vector3(-0.1f, 0.3f, 0), new Vector3(0.11f, 0.45f, 0.12f), skin);
                    B(new Vector3(0.1f, 0.3f, 0), new Vector3(0.11f, 0.45f, 0.12f), skin);
                    k.Cylinder(new Vector3(0, 0.5f, 0), 0.27f, 0.6f, 8, 0, 0.19f, Uv(top));
                }
                else
                {
                    B(new Vector3(-0.1f, 0.08f + (0.84f - 0.08f) * 0.5f, 0), new Vector3(0.14f, 0.76f, 0.16f), bottom);
                    B(new Vector3(0.1f, 0.08f + (0.84f - 0.08f) * 0.5f, 0), new Vector3(0.14f, 0.76f, 0.16f), bottom);
                    if (shorts)
                    {
                        B(new Vector3(-0.1f, 0.28f, 0), new Vector3(0.145f, 0.38f, 0.165f), skin);
                        B(new Vector3(0.1f, 0.28f, 0), new Vector3(0.145f, 0.38f, 0.165f), skin);
                    }
                }
                B(new Vector3(0, 1.14f, 0), new Vector3(0.42f, 0.6f, 0.24f), top);
                B(new Vector3(0, 1.5f, 0), new Vector3(0.1f, 0.1f, 0.1f), skin);
                B(new Vector3(0, 1.66f, 0), new Vector3(0.21f, 0.25f, 0.23f), skin);
                if (cap) { B(new Vector3(0, 1.8f, 0), new Vector3(0.23f, 0.06f, 0.25f), top); B(new Vector3(0, 1.78f, -0.15f), new Vector3(0.2f, 0.03f, 0.12f), top); }
                else B(new Vector3(0, 1.8f, 0.01f), new Vector3(0.22f, 0.06f, 0.24f), hair);

                for (int side = -1; side <= 1; side += 2)
                {
                    bool up = pose == 2 || (pose == 1 && side > 0);
                    float x = side * 0.27f;
                    if (up)
                    {
                        B(new Vector3(x, 1.6f, 0), new Vector3(0.11f, 0.34f, 0.12f), top);
                        B(new Vector3(x, 1.92f, 0), new Vector3(0.1f, 0.34f, 0.11f), skin);
                    }
                    else if (pose == 3)
                    {
                        B(new Vector3(x, 1.25f, -0.1f), new Vector3(0.11f, 0.3f, 0.12f), top, 30f);
                        B(new Vector3(x, 1.02f, -0.3f), new Vector3(0.1f, 0.3f, 0.11f), skin, 60f);
                    }
                    else
                    {
                        B(new Vector3(x, 1.28f, 0), new Vector3(0.11f, 0.32f, 0.12f), top);
                        B(new Vector3(x, 0.96f, 0), new Vector3(0.1f, 0.34f, 0.11f), skin);
                    }
                }
                Spectators.Add(ProceduralAssets.SaveOrReplace(k.Build("Spectator_" + v), $"{MeshFolder}/Spectator_{v:00}.asset"));
            }
        }
    }

    static class CityMaterialExtensions
    {
        /// <summary>Red warning beacon (reuses the tail-light material).</summary>
        public static Material TailLightProxy(this CityMaterials m) => m.TailLight;
    }
}
