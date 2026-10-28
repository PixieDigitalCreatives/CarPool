using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RacingPrototype.EditorTools
{
    /// <summary>
    /// Assembles one complete route scene from a <see cref="TrackDefinition"/>: night lighting,
    /// road and race furniture, 10-car grid, checkpoints, the city districts along the route
    /// (buildings, street lights, trees, spectators behind barriers, signage), theme scenery
    /// (sea, port, cliffs), skyline, race systems, camera, HUD and minimap.
    /// </summary>
    public class RouteBuilder
    {
        readonly TrackDefinition def;
        readonly ProjectBuilder.Shared shared;
        readonly CityMaterials m;
        readonly PropKit props;
        readonly System.Random rng;
        TrackGeometryBuilder g;
        Transform root, surface, barriers, city, dressing, crowd, lights;
        readonly List<Footprint> footprints = new List<Footprint>();
        readonly List<ReflectionProbe> probes = new List<ReflectionProbe>();
        readonly List<Vector3> lampPositions = new List<Vector3>();
        bool[][] sidewalkMask;
        float outside;
        int spectatorCount, buildingCount, lightCount;

        struct Footprint
        {
            public Vector2 c, ax, az; public float hx, hz;
            public Footprint(Vector3 centre, Quaternion rot, float halfX, float halfZ)
            {
                c = new Vector2(centre.x, centre.z);
                Vector3 x = rot * Vector3.right, z = rot * Vector3.forward;
                ax = new Vector2(x.x, x.z).normalized; az = new Vector2(z.x, z.z).normalized;
                hx = halfX; hz = halfZ;
            }
            public IEnumerable<Vector2> Corners()
            {
                yield return c + ax * hx + az * hz; yield return c - ax * hx + az * hz;
                yield return c - ax * hx - az * hz; yield return c + ax * hx - az * hz;
            }
            public bool Overlaps(Footprint o)
            {
                foreach (var axis in new[] { ax, az, o.ax, o.az })
                {
                    float d = Mathf.Abs(Vector2.Dot(o.c - c, axis));
                    float ra = hx * Mathf.Abs(Vector2.Dot(ax, axis)) + hz * Mathf.Abs(Vector2.Dot(az, axis));
                    float rb = o.hx * Mathf.Abs(Vector2.Dot(o.ax, axis)) + o.hz * Mathf.Abs(Vector2.Dot(o.az, axis));
                    if (d > ra + rb) return false;
                }
                return true;
            }
        }

        public RouteBuilder(TrackDefinition definition, ProjectBuilder.Shared sharedAssets)
        {
            def = definition;
            shared = sharedAssets;
            m = shared.Materials;
            props = shared.Props;
            rng = new System.Random(def.environmentSeed);
        }

        public RouteCatalog.Entry Build()
        {
            BuildingKit.ResetCache();
            var centre = TrackSpline.BuildCentreline(def.controlPoints, def.sampleSpacing, def.smoothingIterations);
            int n = centre.Length;
            var cum = new float[n + 1];
            for (int i = 0; i < n; i++) cum[i + 1] = cum[i] + Vector3.Distance(centre[i], centre[(i + 1) % n]);
            var halfWidths = new float[n];
            for (int i = 0; i < n; i++) halfWidths[i] = def.WidthAtFraction(cum[i] / cum[n]) * 0.5f;
            g = new TrackGeometryBuilder(centre, halfWidths, def.barrierGap);
            outside = g.OutsideSide;
            Debug.Log($"[RouteBuilder] Route {def.routeNumber} {def.routeName}: {g.Length:F0} m, tightest corner {g.MinCornerRadius():F1} m, {(g.Winding() > 0 ? "anticlockwise" : "clockwise")}");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("Route " + def.routeNumber).transform;
            var lighting = SetupLighting();

            var trackGo = new GameObject("Track");
            var path = trackGo.AddComponent<TrackPath>();
            path.Initialise(def, centre, halfWidths, def.barrierGap);
            surface = Child(trackGo.transform, "Surface");
            barriers = Child(trackGo.transform, "Barriers");
            city = new GameObject("City").transform;
            dressing = Child(city, "Street Dressing");
            crowd = Child(city, "Spectators");
            lights = Child(city, "Street Lights");

            BuildSidewalkMasks();
            BuildRoad();
            BuildBarriers();
            BuildSidewalks();
            BuildJunctions();

            var systems = BuildRaceSystems(path, out var lapManager, out var slots);
            BuildGantry();
            BuildGround();
            DressTheme();
            DressDistricts();
            BuildSkyline();
            BuildReflectionProbes();

            // Minimap + route info.
            var minimap = MinimapBuilder.Build($"Route{def.routeNumber:00}", centre, def.checkpointFractions, out Vector2 mapMin, out Vector2 mapMax);
            var info = new GameObject("Route Info").AddComponent<RouteInfo>();
            float par = g.Length / def.parSpeed;
            string districts = string.Join(" · ", def.sectors.Select(s => s.name));
            info.Configure(def.routeNumber - 1, def.routeName, districts, minimap, mapMin, mapMax, par, g.Length);

            // Camera, post-processing, HUD.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2600f;
            cam.fieldOfView = 60f;
            cam.backgroundColor = Color.black;
            camGo.AddComponent<AudioListener>();
            var follow = camGo.AddComponent<CameraFollow>();
            camGo.transform.SetPositionAndRotation(slots[slots.Length - 1].position + Vector3.up * 3f - slots[0].forward * 8f, slots[0].rotation);
            UrpSetup.SetupCamera(cam, shared.NightProfile, lighting);

            var hud = new GameObject("Race UI").AddComponent<RaceHUD>();
            hud.Build();
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var race = systems.GetComponent<RaceManager>();
            race.Configure(path, info, lapManager, systems.GetComponent<RaceTimer>(), systems.GetComponent<LeaderboardManager>(),
                systems.GetComponent<CarSelection>(), follow, systems.GetComponent<RaceAudio>(), slots);

            MarkStatic(surface.gameObject);
            MarkStatic(barriers.gameObject);
            MarkStatic(city.gameObject);

            string scenePath = $"Assets/Scenes/{def.sceneName}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            BakeReflectionProbes(scenePath);
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[RouteBuilder] Saved {scenePath}: {buildingCount} buildings, {spectatorCount} spectators, {lightCount} street lights");

            return new RouteCatalog.Entry
            {
                number = def.routeNumber, name = def.routeName, sceneName = def.sceneName, districts = districts,
                description = def.description, lengthMetres = g.Length, parLapSeconds = par, minimap = minimap
            };
        }

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        GameObject AddMesh(string name, Transform parent, Mesh mesh, params Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        // ================================================================== Lighting

        Transform SetupLighting()
        {
            var settings = new LightingSettings { name = "Night_Lighting", bakedGI = false, realtimeGI = false };
            settings = ProceduralAssets.SaveOrReplace(settings, "Assets/Settings/NightLighting.lighting");
            Lightmapping.lightingSettings = settings;

            RenderSettings.skybox = m.Sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Night ambient kept above black so shadowed areas and spectators stay readable (PPT: no pitch-black regions).
            RenderSettings.ambientSkyColor = new Color(0.17f, 0.2f, 0.3f);
            RenderSettings.ambientEquatorColor = new Color(0.13f, 0.13f, 0.16f);
            RenderSettings.ambientGroundColor = new Color(0.07f, 0.07f, 0.075f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.7f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = def.theme == RouteTheme.Hillside ? 0.0016f : 0.0024f;
            RenderSettings.fogColor = new Color(0.07f, 0.085f, 0.12f);

            var t = Child(root, "Lighting");
            var moonGo = new GameObject("Moonlight");
            moonGo.transform.SetParent(t, false);
            moonGo.transform.rotation = Quaternion.Euler(52f, 140f, 0f);
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.62f, 0.7f, 0.92f);
            moon.intensity = 0.42f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.6f;
            RenderSettings.sun = moon;
            return t;
        }

        // ================================================================== Road

        void BuildSidewalkMasks()
        {
            sidewalkMask = new[] { Enumerable.Repeat(true, g.N).ToArray(), Enumerable.Repeat(true, g.N).ToArray() };
            foreach (float f in def.junctions)
            {
                float side = JunctionSide(f);
                int centreSample = g.SampleAt(f * g.Length);
                int halfSpan = Mathf.CeilToInt(6f / (g.Length / g.N));
                for (int o = -halfSpan; o <= halfSpan; o++) sidewalkMask[side > 0 ? 1 : 0][g.Idx(centreSample + o)] = false;
            }
        }

        /// <summary>Side streets join from the outside of the loop, unless that side is sea / quay / cliff.</summary>
        float JunctionSide(float f) => SideIsScenery(def.DistrictAt(f), outside) ? -outside : outside;

        void BuildRoad()
        {
            int k = 0;
            foreach (var mesh in g.Strip("Road", i => -g.Hw[i], i => g.Hw[i], 0f, TrackGeometryBuilder.Uv.World, 8f))
                AddMesh("Road " + k++, surface, mesh, m.Asphalt);
            foreach (float side in new[] { -1f, 1f })
            {
                string s = side < 0 ? "L" : "R";
                k = 0;
                foreach (var mesh in g.Strip("Gutter" + s, i => side < 0 ? -g.Barrier(i) - 0.02f : g.Hw[i], i => side < 0 ? -g.Hw[i] : g.Barrier(i) + 0.02f, 0.002f, TrackGeometryBuilder.Uv.World, 2f, null, 25, 2f))
                    AddMesh("Gutter " + s + k++, surface, mesh, m.Gutter);
                AddMesh("Edge Line " + s, surface, Combine(g.Strip("EdgeLine" + s, i => side * (g.Hw[i] - 0.45f), i => side * (g.Hw[i] - 0.3f), 0.01f, TrackGeometryBuilder.Uv.Across, 4f, null, g.N)), m.LineWhite);
            }
            // Lane dividers: lanes of about 4.5 m that follow the varying width.
            float maxWidth = g.Hw.Max() * 2f;
            int lanes = Mathf.Max(2, Mathf.RoundToInt(maxWidth / 4.6f));
            for (int l = 1; l < lanes; l++)
            {
                float frac = l / (float)lanes;
                AddMesh("Lane Divider " + l, surface, g.Dashed("LaneDivider" + l, i => -g.Hw[i] + 2f * g.Hw[i] * frac, 0.14f, 3f, 6f, 0.01f), m.LineWhite);
            }
            // Kerbs in corners (red/white), drains along the gutters.
            var corners = g.CornerMask(70f, 6);
            foreach (float side in new[] { -1f, 1f })
                AddMesh("Kerb " + (side < 0 ? "L" : "R"), surface, Combine(g.Strip("Kerb", i => side * (g.Hw[i] - 0.3f), i => side * (g.Hw[i] + 0.75f), 0.012f, TrackGeometryBuilder.Uv.Across, 2f, corners, g.N)), m.Kerb);
            var drains = new MeshKit();
            for (float d = 9f; d < g.Length; d += 18f)
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 p = g.PointAt(d, out var t, out var r);
                    int i = g.SampleAt(d);
                    Vector3 c = p + r * side * (g.Hw[i] + 0.55f) + Vector3.up * 0.008f;
                    drains.QuadUV(c - t * 0.45f - r * 0.22f, c - t * 0.45f + r * 0.22f, c + t * 0.45f + r * 0.22f, c + t * 0.45f - r * 0.22f, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
                }
            AddMesh("Drains", surface, drains.Build("Drains"), m.Drain);
            // Zebra crossings at junctions and civic-square approaches.
            var zebra = new MeshKit();
            foreach (float f in def.junctions) g.Zebra(zebra, f * g.Length + 9f, 0.011f);
            if (zebra.Vertices.Count > 0) AddMesh("Crosswalks", surface, zebra.Build("Crosswalks"), m.LineWhite);
            // Start/finish and checkpoint lines.
            AddMesh("Start Finish Line", surface, g.CrossLine("StartFinishLine", 0f, 2f, 0.013f, new Vector2(g.Hw[0] * 2f / 0.5f / 8f, 0.5f)), m.Checker);
            for (int c = 0; c < def.checkpointFractions.Length; c++)
                AddMesh("Checkpoint Line " + (c + 1), surface, g.CrossLine("CheckpointLine" + (c + 1), def.checkpointFractions[c] * g.Length, 0.4f, 0.012f, Vector2.one), m.CheckpointLine);
        }

        Mesh Combine(List<Mesh> meshes)
        {
            var combine = meshes.Select(x => new CombineInstance { mesh = x, transform = Matrix4x4.identity }).ToArray();
            var mesh = new Mesh { name = meshes.Count > 0 ? meshes[0].name : "Combined", indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(combine, true, false);
            foreach (var x in meshes) Object.DestroyImmediate(x);
            return mesh;
        }

        void BuildBarriers()
        {
            foreach (float side in new[] { -1f, 1f })
            {
                string s = side < 0 ? "Left" : "Right";
                bool guardrail = def.theme == RouteTheme.Hillside && side == outside;
                if (guardrail)
                    AddMesh("Guardrail " + s, barriers, g.Guardrail("Guardrail" + s, side), m.Guardrail, m.Concrete);
                else
                {
                    var chunks = g.JerseyBarrier("Barrier" + s, side);
                    for (int c = 0; c < chunks.Count; c++)
                    {
                        float f = c * 25f / g.N;
                        // Yellow/black in the busy commercial districts, red/white elsewhere (concept slides 3-8).
                        var district = def.DistrictAt(f);
                        bool red = !(district == District.DowntownAvenue || district == District.CommercialJunction || district == District.MarketStreet || district == District.WaterfrontPromenade);
                        AddMesh($"Barrier {s} {c:00}", barriers, chunks[c], red ? m.BarrierRed : m.BarrierYellow);
                    }
                }
                var colliderGo = new GameObject("Barrier Collider " + s);
                colliderGo.transform.SetParent(barriers, false);
                colliderGo.AddComponent<MeshCollider>().sharedMesh = g.BarrierCollider("BarrierCollider" + s, side, 1.1f);
            }
        }

        bool HasSidewalk(float side) => !(def.theme == RouteTheme.Hillside && side == outside);

        void BuildSidewalks()
        {
            foreach (float side in new[] { -1f, 1f })
            {
                if (!HasSidewalk(side)) continue;
                int k = 0;
                foreach (var mesh in g.Sidewalk("Sidewalk" + (side < 0 ? "L" : "R"), side, def.sidewalkWidth, sidewalkMask[side > 0 ? 1 : 0]))
                    AddMesh("Sidewalk " + (side < 0 ? "L" : "R") + k++, surface, mesh, m.Paving, m.KerbStone);
            }
        }

        /// <summary>Visual side streets (PPT "junctions"): blocked by the race barrier, with lane marking and parked cars.</summary>
        void BuildJunctions()
        {
            foreach (float f in def.junctions)
            {
                float side = JunctionSide(f);
                float d = f * g.Length;
                Vector3 p = g.PointAt(d, out var t, out var r);
                int i = g.SampleAt(d);
                float start = g.SidewalkInner(i) - 0.05f;
                Vector3 dir = r * side;
                Vector3 a = p + dir * start;
                var kit = new MeshKit(2);
                const float len = 42f, hw = 5f;
                Vector3 e = a + dir * len;
                kit.QuadUV(a - t * hw, a + t * hw, e + t * hw, e - t * hw, Vector2.zero, new Vector2(hw / 4f, 0), new Vector2(hw / 4f, len / 8f), new Vector2(0, len / 8f), Vector3.up, 0);   // 8 m per UV, like the main road
                for (float x = 2f; x < len; x += 9f)
                {
                    Vector3 c = a + dir * x + Vector3.up * 0.01f;
                    kit.QuadUV(c - t * 0.07f, c + t * 0.07f, c + t * 0.07f + dir * 3f, c - t * 0.07f + dir * 3f, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up, 1);
                }
                var go = AddMesh("Side Street", surface, kit.Build("SideStreet"), m.Asphalt, m.LineWhite);
                // Side-street sidewalks.
                foreach (float s2 in new[] { -1f, 1f })
                {
                    var sw = new MeshKit();
                    Vector3 o = t * s2 * (hw + 1.5f);
                    sw.Box(a + dir * (len / 2f) + o + Vector3.up * 0.075f, new Vector3(3f, 0.15f, len), Quaternion.LookRotation(dir), 0, 2f);
                    AddMesh("Side Street Sidewalk", surface, sw.Build("SideStreetSidewalk"), m.Paving);
                }
                // A couple of parked cars (static shells of the Regular car).
                for (int c = 0; c < 2; c++)
                {
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(shared.ParkedCar, dressing);
                    Vector3 pos = a + dir * (14f + c * 11f) + t * (c % 2 == 0 ? -2.8f : 2.8f);
                    car.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(c % 2 == 0 ? dir : -dir));
                }
                // Street-circuit closure: a row of scanned concrete road barriers across the side street.
                if (props.RoadBarrier != null)
                    for (float x = -hw + 0.8f; x <= hw - 0.7f; x += 1.62f)
                        Place(props.RoadBarrier, a + dir * 7f + t * x, Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0, (float)rng.NextDouble() * 6f - 3f, 0), 1f);
                Reserve(a + dir * (len / 2f), Quaternion.LookRotation(dir), hw + 3f, len / 2f);
            }
        }

        // ================================================================== Race systems

        GameObject BuildRaceSystems(TrackPath path, out LapManager lapManager, out Transform[] slots)
        {
            var systems = new GameObject("Race Systems");
            var timer = systems.AddComponent<RaceTimer>();
            lapManager = systems.AddComponent<LapManager>();
            var leaderboard = systems.AddComponent<LeaderboardManager>();
            var selection = systems.AddComponent<CarSelection>();
            systems.AddComponent<AudioSource>();
            var audio = systems.AddComponent<RaceAudio>();
            systems.AddComponent<RaceManager>();
            leaderboard.Configure(lapManager);
            selection.Configure(shared.Catalog);
            var s = shared.Sounds;
            audio.Configure(s.Countdown, s.Go, s.Checkpoint, s.Lap, s.Finish, s.Warning, s.Click);

            var cpRoot = new GameObject("Checkpoints").transform;
            cpRoot.SetParent(path.transform, false);
            var cps = new List<TrackCheckpoint> { Gate(cpRoot, 0, 0f, lapManager, false) };
            for (int i = 0; i < def.checkpointFractions.Length; i++)
                cps.Add(Gate(cpRoot, i + 1, def.checkpointFractions[i] * g.Length, lapManager, true));
            lapManager.Configure(cps.ToArray(), timer);

            // 10-car grid: two staggered columns, five rows, after the start line.
            var gridRoot = new GameObject("Starting Grid").transform;
            gridRoot.SetParent(path.transform, false);
            slots = new Transform[RaceRules.MaxRacers];
            var pos = new Vector3[slots.Length];
            var rot = new Quaternion[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                int row = i / 2, col = i % 2;
                float d = def.gridPoleDistance - row * def.gridRowSpacing - col * def.gridRowSpacing * 0.5f;
                Vector3 p = g.PointAt(d, out var t, out var r);
                float lateral = (col == 0 ? -1f : 1f) * def.gridLateralOffset;
                var slot = new GameObject($"Grid Slot {i + 1:00}").transform;
                slot.SetParent(gridRoot, false);
                slot.SetPositionAndRotation(p + r * lateral + Vector3.up * 0.02f, Quaternion.LookRotation(t, Vector3.up));
                slots[i] = slot;
                pos[i] = p + r * lateral;
                rot[i] = slot.rotation;
            }
            AddMesh("Grid Markings", surface, g.GridMarkings(pos, rot, 0.012f), m.LineWhite);
            return systems;
        }

        TrackCheckpoint Gate(Transform parent, int index, float distance, LapManager lapManager, bool withFrame)
        {
            Vector3 p = g.PointAt(distance, out var t, out var r);
            int i = g.SampleAt(distance);
            var go = new GameObject(index == 0 ? "Checkpoint 0 (Start-Finish)" : $"Checkpoint {index}");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(t, Vector3.up));
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 2.5f, 0f);
            trigger.size = new Vector3(g.Barrier(i) * 2f, 5f, 3f);

            var highlights = new List<Renderer>();
            if (withFrame)
            {
                float postX = g.SidewalkInner(i) + 0.4f;
                var kit = new MeshKit(1);
                foreach (float s in new[] { -1f, 1f }) kit.Box(new Vector3(s * postX, 3.3f, 0), new Vector3(0.35f, 6.6f, 0.35f), Quaternion.identity, 0);
                kit.Box(new Vector3(0, 6.85f, 0), new Vector3(postX * 2f + 0.35f, 0.25f, 0.3f), Quaternion.identity, 0);
                AddMesh("Gate Frame", go.transform, kit.Build("GateFrame"), m.Truss);
                var banner = new MeshKit();
                banner.Box(new Vector3(0, 6.15f, 0), new Vector3(Mathf.Min(postX * 1.1f, 10f), 0.9f, 0.18f), Quaternion.identity, 0);
                var bannerGo = AddMesh("Banner", go.transform, banner.Build("GateBanner"), m.GateBanner);
                highlights.Add(bannerGo.GetComponent<Renderer>());
                var brand = BrandConfig.Current;
                UIFactory.WorldText("Label", go.transform, $"CHECKPOINT {index}", new Vector3(0.6f, 6.15f, -0.11f), Quaternion.identity, new Vector2(Mathf.Min(postX * 0.8f, 7f), 0.62f), Color.white);
                UIFactory.WorldText("Brand", go.transform, brand.wordmark, new Vector3(-Mathf.Min(postX * 0.5f, 4.4f), 6.15f, -0.11f), Quaternion.identity, new Vector2(1.6f, 0.4f), brand.primary);
                UIFactory.WorldText("Label Back", go.transform, $"CHECKPOINT {index}", new Vector3(0, 6.15f, 0.11f), Quaternion.Euler(0, 180f, 0), new Vector2(Mathf.Min(postX * 0.8f, 7f), 0.62f), Color.white);
            }
            var cp = go.AddComponent<TrackCheckpoint>();
            cp.Configure(index, lapManager, highlights.ToArray());
            return cp;
        }

        void BuildGantry()
        {
            Vector3 p = g.PointAt(0f, out var t, out var r);
            var go = new GameObject("Start Finish Gantry (thegameio)");
            go.transform.SetParent(city, false);
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(t, Vector3.up));
            float postX = g.SidewalkInner(0) + 0.6f;
            var kit = new MeshKit(2);
            foreach (float s in new[] { -1f, 1f }) kit.Box(new Vector3(s * postX, 4.6f, 0), new Vector3(0.8f, 9.2f, 0.8f), Quaternion.identity, 0);
            kit.Box(new Vector3(0, 8.4f, 0), new Vector3(postX * 2f + 0.8f, 1.9f, 0.9f), Quaternion.identity, 0);
            kit.Box(new Vector3(0, 8.4f, -0.47f), new Vector3(postX * 1.55f, 1.6f, 0.04f), Quaternion.identity, 1);
            kit.Box(new Vector3(0, 8.4f, 0.47f), new Vector3(postX * 1.55f, 1.6f, 0.04f), Quaternion.identity, 1);
            AddMesh("Gantry", go.transform, kit.Build("Gantry"), m.Truss, m.BillboardFace);
            var brand = BrandConfig.Current;
            UIFactory.WorldText("Wordmark", go.transform, brand.wordmark, new Vector3(-postX * 0.38f, 8.4f, -0.5f), Quaternion.identity, new Vector2(postX * 0.7f, 1.2f), brand.primary, FontStyle.Bold, 300);
            UIFactory.WorldText("Start Finish", go.transform, "START  /  FINISH", new Vector3(postX * 0.38f, 8.4f, -0.5f), Quaternion.identity, new Vector2(postX * 0.72f, 1.0f), Color.white, FontStyle.Bold, 220);
            UIFactory.WorldText("Back", go.transform, brand.wordmark, new Vector3(0, 8.4f, 0.5f), Quaternion.Euler(0, 180f, 0), new Vector2(postX * 1.3f, 1.2f), brand.primary, FontStyle.Bold, 300);
            foreach (float s in new[] { -0.5f, 0.5f })
            {
                var l = new GameObject("Gantry Light");
                l.transform.SetParent(go.transform, false);
                l.transform.localPosition = new Vector3(s * postX, 7.3f, -1.2f);
                l.transform.localRotation = Quaternion.Euler(70f, 0, 0);
                var light = l.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 80f;
                light.range = 30f;
                light.intensity = 12f;
                light.color = new Color(1f, 0.93f, 0.82f);
                lightCount++;
            }
            Reserve(p, Quaternion.LookRotation(t), postX + 1f, 3f);
        }

        // ================================================================== Ground & theme

        void BuildGround()
        {
            Vector2 min = new Vector2(g.Pts.Min(p => p.x), g.Pts.Min(p => p.z)) - Vector2.one * 900f;
            Vector2 max = new Vector2(g.Pts.Max(p => p.x), g.Pts.Max(p => p.z)) + Vector2.one * 900f;
            float y = -0.04f;
            if (def.theme == RouteTheme.Waterfront) min.y = CoastZ();
            if (def.theme == RouteTheme.IndustrialPort) max.y = CoastZ();
            if (def.theme != RouteTheme.Hillside)
            {
                var kit = new MeshKit();
                kit.QuadUV(new Vector3(min.x, y, min.y), new Vector3(max.x, y, min.y), new Vector3(max.x, y, max.y), new Vector3(min.x, y, max.y),
                    new Vector2(min.x / 6f, min.y / 6f), new Vector2(max.x / 6f, min.y / 6f), new Vector2(max.x / 6f, max.y / 6f), new Vector2(min.x / 6f, max.y / 6f), Vector3.up);
                AddMesh("City Ground", city, kit.Build("CityGround"), m.Ground);
            }
            var floor = new GameObject("Physics Floor");
            floor.transform.SetParent(city, false);
            floor.transform.position = new Vector3((min.x + max.x) / 2f, -1f, (min.y + max.y) / 2f);
            var box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(max.x - min.x + 2000f, 2f, max.y - min.y + 2000f);
            floor.isStatic = false;
        }

        float CoastZ()
        {
            if (def.theme == RouteTheme.Waterfront) return g.Pts.Min(p => p.z) - 26f;
            return g.Pts.Max(p => p.z) + 24f;
        }

        void DressTheme()
        {
            switch (def.theme)
            {
                case RouteTheme.Waterfront: Sea(CoastZ(), false); break;
                case RouteTheme.IndustrialPort: Sea(CoastZ(), true); Port(); break;
                case RouteTheme.Hillside: Hillside(); break;
            }
        }

        void Sea(float coastZ, bool northOfCoast)
        {
            Vector2 min = new Vector2(g.Pts.Min(p => p.x) - 1500f, northOfCoast ? coastZ : coastZ - 2500f);
            Vector2 max = new Vector2(g.Pts.Max(p => p.x) + 1500f, northOfCoast ? coastZ + 2500f : coastZ);
            var kit = new MeshKit();
            float y = -1.6f;
            kit.QuadUV(new Vector3(min.x, y, min.y), new Vector3(max.x, y, min.y), new Vector3(max.x, y, max.y), new Vector3(min.x, y, max.y),
                new Vector2(min.x / 30f, min.y / 30f), new Vector2(max.x / 30f, min.y / 30f), new Vector2(max.x / 30f, max.y / 30f), new Vector2(min.x / 30f, max.y / 30f), Vector3.up);
            AddMesh("Sea", city, kit.Build("Sea"), m.Water);
            // Quay wall with railing and lamps along the coast.
            var wall = new MeshKit(2);
            float x0 = g.Pts.Min(p => p.x) - 400f, x1 = g.Pts.Max(p => p.x) + 400f;
            float dz = northOfCoast ? 1f : -1f;
            wall.QuadUV(new Vector3(x0, -0.04f, coastZ), new Vector3(x1, -0.04f, coastZ), new Vector3(x1, -2f, coastZ), new Vector3(x0, -2f, coastZ),
                Vector2.zero, new Vector2((x1 - x0) / 4f, 0), new Vector2((x1 - x0) / 4f, 0.5f), new Vector2(0, 0.5f), new Vector3(0, 0, dz), 0);
            if (!northOfCoast)
                for (float x = x0; x < x1; x += 2.5f)
                    wall.Box(new Vector3(x, 0.55f, coastZ - dz * 0.3f), new Vector3(0.06f, 1.1f, 0.06f), Quaternion.identity, 1);
            if (!northOfCoast) wall.Box(new Vector3((x0 + x1) / 2f, 1.1f, coastZ - dz * 0.3f), new Vector3(x1 - x0, 0.06f, 0.08f), Quaternion.identity, 1);
            AddMesh("Quay Wall", city, wall.Build("QuayWall"), m.Concrete, m.Rail);
            // Promenade lamps and palms along the coast (waterfront).
            if (!northOfCoast)
                for (float x = x0 + 30f; x < x1; x += 26f)
                {
                    Vector3 p = new Vector3(x, 0f, coastZ + 2.5f);
                    float clearance = g.DistanceToCentreLine(p);
                    if (clearance < 14f || clearance > 160f) continue;
                    Place(props.Palm, p + Vector3.right * 9f, Quaternion.Euler(0, rng.Next(360), 0), 0.9f + (float)rng.NextDouble() * 0.3f);
                    if ((int)(x / 26f) % 3 == 0) { PlaceLamp(p, Quaternion.LookRotation(Vector3.forward)); }
                }
        }

        void Port()
        {
            float coast = CoastZ();
            // Cranes along the quay edge and a container ship alongside.
            for (float x = g.Pts.Min(p => p.x) + 40f; x < g.Pts.Max(p => p.x) - 20f; x += 78f)
                Place(props.Crane, new Vector3(x, 0f, coast - 6f), Quaternion.Euler(0, 180f, 0), 1f, false);
            var ship = new MeshKit(2);
            float sx = (g.Pts.Min(p => p.x) + g.Pts.Max(p => p.x)) / 2f;
            ship.Box(new Vector3(sx, 3f, coast + 22f), new Vector3(210f, 10f, 30f), Quaternion.identity, 0);
            ship.Box(new Vector3(sx + 78f, 13f, coast + 22f), new Vector3(24f, 14f, 26f), Quaternion.identity, 1);
            AddMesh("Container Ship", city, ship.Build("Ship"), m.ShipHull, m.ShipDeck);
            for (int k = 0; k < 60; k++)
            {
                int colour = rng.Next(m.Container.Length);
                var go = AddMesh("Ship Container", city, props.ContainerMesh, m.Container[colour]);
                go.transform.position = new Vector3(sx - 90f + (k % 20) * 7f, 8f + (k / 20) * 2.6f, coast + 14f + (k % 3) * 2.5f);
                go.transform.rotation = Quaternion.Euler(0, 90f, 0);
            }
        }

        void Hillside()
        {
            // Plateau (polygon of the outer verge), cliff walls, lower city and sea far below.
            float edge = 5.5f;
            var outline = new List<Vector3>();
            for (int i = 0; i < g.N; i += 3)
                outline.Add(g.Pts[i] + g.Right[i] * outside * (g.SidewalkInner(i) + edge));
            var plateau = Triangulate(outline, -0.04f);
            AddMesh("Plateau", city, plateau, m.Grass);
            var cliff = new MeshKit();
            float acc = 0f;
            for (int k = 0; k < outline.Count; k++)
            {
                Vector3 a = outline[k], b = outline[(k + 1) % outline.Count];
                int i = (k * 3) % g.N, j = ((k + 1) * 3) % g.N;
                Vector3 oa = g.Right[i] * outside, ob = g.Right[j] * outside;
                float len = Vector3.Distance(a, b);
                kit_quad(cliff, a, b, b + ob * 9f + Vector3.down * 62f, a + oa * 9f + Vector3.down * 62f, acc, len, oa);
                acc += len;
            }
            AddMesh("Cliffs", city, cliff.Build("Cliffs"), m.Rock);
            // Verge strip between guardrail and cliff edge.
            AddMesh("Verge", surface, Combine(g.Strip("Verge", i => outside * g.SidewalkInner(i) - (outside < 0 ? edge : 0f), i => outside * g.SidewalkInner(i) + (outside > 0 ? edge : 0f), -0.03f, TrackGeometryBuilder.Uv.World, 6f, null, g.N, 6f)), m.Grass);
            var low = new MeshKit();
            low.QuadUV(new Vector3(-3000, -62f, -3000), new Vector3(3000, -62f, -3000), new Vector3(3000, -62f, 3000), new Vector3(-3000, -62f, 3000), Vector2.zero, Vector2.right * 600, Vector2.one * 600, Vector2.up * 600, Vector3.up);
            AddMesh("Lower Ground", city, low.Build("LowerGround"), m.Ground);
            // The city below: lit blocks spread around the hill.
            var below = Child(city, "City Below");
            for (int k = 0; k < 260; k++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f, dist = 280f + (float)rng.NextDouble() * 700f;
                Vector3 p = new Vector3(Mathf.Cos(ang) * dist, -62f, Mathf.Sin(ang) * dist * 0.85f);
                var type = rng.NextDouble() < 0.18 ? BuildingType.Tower : rng.NextDouble() < 0.5 ? BuildingType.Apartment : BuildingType.Office;
                var spec = BuildingKit.Pick(type, rng);
                BuildingKit.Create(spec, m, below, p, Quaternion.Euler(0, rng.Next(0, 4) * 90f, 0), rng, false);
            }
            var sea = new MeshKit();
            sea.QuadUV(new Vector3(-4000, -61.5f, -4000), new Vector3(4000, -61.5f, -4000), new Vector3(4000, -61.5f, -1300), new Vector3(-4000, -61.5f, -1300), Vector2.zero, Vector2.right * 200, Vector2.one * 200, Vector2.up * 200, Vector3.up);
            AddMesh("Sea Far", city, sea.Build("SeaFar"), m.Water);
        }

        static void kit_quad(MeshKit kit, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0, float len, Vector3 outward) =>
            kit.QuadUV(a, b, c, d, new Vector2(u0 / 8f, 0), new Vector2((u0 + len) / 8f, 0), new Vector2((u0 + len) / 8f, -62f / 8f), new Vector2(u0 / 8f, -62f / 8f), outward);

        static Mesh Triangulate(List<Vector3> poly, float y)
        {
            // Ear clipping on the XZ polygon (counter-clockwise order enforced).
            var pts = poly.Select(p => new Vector2(p.x, p.z)).ToList();
            float area = 0f;
            for (int i = 0; i < pts.Count; i++) area += pts[i].x * pts[(i + 1) % pts.Count].y - pts[(i + 1) % pts.Count].x * pts[i].y;
            if (area < 0f) pts.Reverse();
            var kit = new MeshKit();
            var idx = new List<int>();
            for (int i = 0; i < pts.Count; i++) idx.Add(kit.Add(new Vector3(pts[i].x, y, pts[i].y), pts[i] / 6f));
            var remaining = Enumerable.Range(0, pts.Count).ToList();
            int guard = 0;
            while (remaining.Count > 3 && guard++ < 100000)
            {
                bool clipped = false;
                for (int k = 0; k < remaining.Count; k++)
                {
                    int ia = remaining[(k - 1 + remaining.Count) % remaining.Count], ib = remaining[k], ic = remaining[(k + 1) % remaining.Count];
                    Vector2 a = pts[ia], b = pts[ib], c = pts[ic];
                    if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) <= 0f) continue;
                    bool inside = false;
                    foreach (int ip in remaining)
                    {
                        if (ip == ia || ip == ib || ip == ic) continue;
                        if (PointInTri(pts[ip], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    kit.Tri(idx[ia], idx[ib], idx[ic], Vector3.up);
                    remaining.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (remaining.Count == 3) kit.Tri(idx[remaining[0]], idx[remaining[1]], idx[remaining[2]], Vector3.up);
            return kit.Build("Plateau");
        }

        static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ================================================================== Districts

        struct DistrictStyle
        {
            public (BuildingType type, float weight)[] front;
            public BuildingType[] back;
            public float crowd;
            public int crowdRows;
            public bool palms, stalls, containers, mastLights, villas;
        }

        static DistrictStyle StyleOf(District d)
        {
            switch (d)
            {
                case District.DowntownAvenue: return new DistrictStyle { front = new[] { (BuildingType.Office, 0.35f), (BuildingType.Shop, 0.35f), (BuildingType.Apartment, 0.3f) }, back = new[] { BuildingType.Tower, BuildingType.Office }, crowd = 0.9f, crowdRows = 2, palms = true };
                case District.CommercialJunction: return new DistrictStyle { front = new[] { (BuildingType.Shop, 0.45f), (BuildingType.Office, 0.3f), (BuildingType.Apartment, 0.25f) }, back = new[] { BuildingType.Office, BuildingType.Apartment }, crowd = 0.55f, crowdRows = 1, palms = true };
                case District.MarketStreet: return new DistrictStyle { front = new[] { (BuildingType.Shop, 0.78f), (BuildingType.House, 0.12f), (BuildingType.Apartment, 0.1f) }, back = new[] { BuildingType.Apartment }, crowd = 0.95f, crowdRows = 2, stalls = true };
                case District.ResidentialStreet: return new DistrictStyle { front = new[] { (BuildingType.House, 0.55f), (BuildingType.Apartment, 0.35f), (BuildingType.Shop, 0.1f) }, back = new[] { BuildingType.Apartment }, crowd = 0.45f, crowdRows = 1 };
                case District.CivicSquare: return new DistrictStyle { front = new[] { (BuildingType.Civic, 0.4f), (BuildingType.Office, 0.3f), (BuildingType.Apartment, 0.3f) }, back = new[] { BuildingType.Tower, BuildingType.Office }, crowd = 0.75f, crowdRows = 2, palms = true };
                case District.WaterfrontPromenade: return new DistrictStyle { front = new[] { (BuildingType.Hotel, 0.5f), (BuildingType.Apartment, 0.3f), (BuildingType.Shop, 0.2f) }, back = new[] { BuildingType.Tower }, crowd = 0.6f, crowdRows = 1, palms = true };
                case District.HotelBoulevard: return new DistrictStyle { front = new[] { (BuildingType.Hotel, 0.4f), (BuildingType.Office, 0.3f), (BuildingType.Apartment, 0.3f) }, back = new[] { BuildingType.Tower, BuildingType.Hotel }, crowd = 0.35f, crowdRows = 1, palms = true };
                case District.PortQuay: return new DistrictStyle { front = new[] { (BuildingType.Warehouse, 1f) }, back = new BuildingType[0], crowd = 0.15f, crowdRows = 1, containers = true, mastLights = true };
                case District.ContainerYard: return new DistrictStyle { front = new[] { (BuildingType.Warehouse, 0.25f) }, back = new BuildingType[0], crowd = 0.08f, crowdRows = 1, containers = true, mastLights = true };
                case District.Warehouses: return new DistrictStyle { front = new[] { (BuildingType.Warehouse, 0.85f), (BuildingType.Office, 0.15f) }, back = new BuildingType[0], crowd = 0.12f, crowdRows = 1, containers = true, mastLights = true };
                default: return new DistrictStyle { front = new[] { (BuildingType.House, 0.8f), (BuildingType.Apartment, 0.2f) }, back = new BuildingType[0], crowd = 0.3f, crowdRows = 1, palms = true, villas = true };
            }
        }

        static BuildingType Pick((BuildingType type, float weight)[] options, System.Random rng)
        {
            float r = (float)rng.NextDouble() * options.Sum(o => o.weight);
            foreach (var o in options) { if (r < o.weight) return o.type; r -= o.weight; }
            return options[0].type;
        }

        /// <summary>Is this side reserved for theme scenery (sea promenade, quay, cliffs) in this sector?</summary>
        bool SideIsScenery(District d, float side)
        {
            if (side != outside) return false;
            return d == District.WaterfrontPromenade || d == District.PortQuay || d == District.HillsideSwitchbacks || d == District.HillsideRidge;
        }

        void DressDistricts()
        {
            PlaceBillboards();
            PlaceCivicSquare();
            foreach (float side in new[] { -1f, 1f })
            {
                PlaceLampsAndTrees(side);
                PlaceFrontRow(side);
                PlaceBackRow(side);
                PlaceSpectators(side);
            }
            PlaceChevrons();
            PlaceRoadDetail();
            FillCityBlocks();
        }

        /// <summary>Scanned manhole covers set into the lanes (flush, no collider) every ~70 m.</summary>
        void PlaceRoadDetail()
        {
            if (props.Manhole == null) return;
            for (float d = 30f; d < g.Length - 20f; d += 55f + (float)rng.NextDouble() * 30f)
            {
                Vector3 p = g.PointAt(d, out var t, out var r);
                int i = g.SampleAt(d);
                float lane = g.Hw[i] * (rng.NextDouble() < 0.5 ? -0.42f : 0.42f);
                Place(props.Manhole, p + r * lane + Vector3.up * 0.003f, Quaternion.Euler(0, rng.Next(360), 0), 1f);
            }
        }

        /// <summary>
        /// Fills the blocks behind the street frontage (inside and around the loop) so the districts
        /// read as one continuous city from every camera, not a single row of facades.
        /// </summary>
        void FillCityBlocks()
        {
            float minX = g.Pts.Min(p => p.x) - 160f, maxX = g.Pts.Max(p => p.x) + 160f;
            float minZ = g.Pts.Min(p => p.z) - 160f, maxZ = g.Pts.Max(p => p.z) + 160f;
            var fill = Child(city, "City Blocks");
            const float step = 30f;
            for (float x = minX; x <= maxX; x += step)
                for (float z = minZ; z <= maxZ; z += step)
                {
                    var pos = new Vector3(x + ((float)rng.NextDouble() - 0.5f) * 10f, 0f, z + ((float)rng.NextDouble() - 0.5f) * 10f);
                    bool inside = InsideLoop(pos);
                    if (def.theme == RouteTheme.Waterfront && pos.z < CoastZ() + 8f) continue;
                    if (def.theme == RouteTheme.IndustrialPort && pos.z > CoastZ() - 8f) continue;
                    if (def.theme == RouteTheme.Hillside && !inside) continue;
                    float clearance = g.DistanceToCentreLine(pos);
                    if (clearance < g.Hw.Max() + def.barrierGap + def.sidewalkWidth + 26f) continue;
                    // District of the nearest piece of track decides the building mix.
                    int nearest = 0; float best = float.MaxValue;
                    for (int i = 0; i < g.N; i += 4) { float dd = (g.Pts[i] - pos).sqrMagnitude; if (dd < best) { best = dd; nearest = i; } }
                    var style = StyleOf(def.DistrictAt(g.Fraction(nearest)));
                    BuildingType type;
                    if (style.containers) type = rng.NextDouble() < 0.5 ? BuildingType.Warehouse : BuildingType.Office;
                    else if (style.villas) type = BuildingType.House;
                    else if (rng.NextDouble() < 0.25 && style.back.Length > 0) type = style.back[rng.Next(style.back.Length)];
                    else type = Pick(style.front, rng);
                    if (type == BuildingType.Civic) type = BuildingType.Office;
                    var spec = BuildingKit.Pick(type, rng);
                    Vector3 t = g.Tangent[nearest];
                    var rot = Quaternion.LookRotation(rng.NextDouble() < 0.5 ? t : Vector3.Cross(Vector3.up, t), Vector3.up);
                    var fp = new Footprint(pos, rot, BuildingKit.Width(spec) / 2f + 1f, BuildingKit.LotDepth(spec) / 2f + 1f);
                    if (footprints.Any(o => fp.Overlaps(o))) continue;
                    footprints.Add(fp);
                    BuildingKit.Create(spec, m, fill, pos, rot, rng, false);
                    buildingCount++;
                }
        }

        bool InsideLoop(Vector3 p)
        {
            bool c = false;
            for (int i = 0, j = g.N - 1; i < g.N; j = i++)
            {
                Vector3 a = g.Pts[i], b = g.Pts[j];
                if (((a.z > p.z) != (b.z > p.z)) && (p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x)) c = !c;
            }
            return c;
        }

        float FrontLine(int i, float side) => g.SidewalkInner(i) + (HasSidewalk(side) ? def.sidewalkWidth : 5.5f) + 0.5f;

        void PlaceFrontRow(float side)
        {
            float d = 6f;
            while (d < g.Length - 4f)
            {
                var district = def.DistrictAt(d / g.Length);
                var style = StyleOf(district);
                if (SideIsScenery(district, side)) { d += 8f; continue; }
                if (style.containers && rng.NextDouble() < 0.55)
                {
                    d += PlaceContainerBlock(d, side) + 4f;
                    continue;
                }
                var spec = BuildingKit.Pick(Pick(style.front, rng), rng);
                if (style.villas) { spec.yard = 4f; spec.bays = rng.Next(3, 5); spec.floors = rng.Next(1, 3); }
                float w = BuildingKit.Width(spec);
                if (TryPlace(spec, d + w / 2f, side, 0f, out _)) d += w + 0.3f + (float)rng.NextDouble() * 1.6f;
                else d += 3.2f;
            }
        }

        void PlaceBackRow(float side)
        {
            float d = 10f;
            while (d < g.Length - 10f)
            {
                var district = def.DistrictAt(d / g.Length);
                var style = StyleOf(district);
                if (style.back.Length == 0 || SideIsScenery(district, side)) { d += 12f; continue; }
                var spec = BuildingKit.Pick(style.back[rng.Next(style.back.Length)], rng);
                float w = BuildingKit.Width(spec);
                float extra = 16f + (float)rng.NextDouble() * 18f;
                if (TryPlace(spec, d + w / 2f, side, extra, out _)) d += w + 6f + (float)rng.NextDouble() * 14f;
                else d += 6f;
            }
        }

        bool TryPlace(BuildingKit.Spec spec, float dc, float side, float extraSetback, out GameObject placed)
        {
            placed = null;
            Vector3 p = g.PointAt(dc, out var t, out var r);
            int i = g.SampleAt(dc);
            float w = BuildingKit.Width(spec), depth = BuildingKit.LotDepth(spec);
            float front = FrontLine(i, side) + extraSetback;
            Quaternion rot = Quaternion.LookRotation(r * side, Vector3.up);
            Vector3 lotCentre = p + r * side * (front + depth / 2f);
            var fp = new Footprint(lotCentre, rot, w / 2f + 0.15f, depth / 2f + 0.15f);
            foreach (var c in fp.Corners())
                if (g.DistanceToCentreLine(new Vector3(c.x, 0, c.y)) < front - 0.8f) return false;
            foreach (var other in footprints) if (fp.Overlaps(other)) return false;
            footprints.Add(fp);
            // Building origin: centre of the building footprint (yard sits in front of it).
            Vector3 buildingCentre = p + r * side * (front + spec.yard + spec.depth / 2f);
            placed = BuildingKit.Create(spec, m, city, buildingCentre, rot, rng, spec.type == BuildingType.Shop && rng.NextDouble() < 0.55);
            buildingCount++;
            // Closed for the night: scanned roller shutters over some ground-floor shop bays.
            if (spec.type == BuildingType.Shop && props.Shutter != null)
            {
                float bay = w / spec.bays;
                for (int k = 0; k < spec.bays; k++)
                    if (rng.NextDouble() < 0.4)
                        Place(props.Shutter, buildingCentre + rot * new Vector3(-w / 2f + bay * (k + 0.5f), 0f, -spec.depth / 2f - 0.16f), rot, 1f);
            }
            return true;
        }

        void Reserve(Vector3 centre, Quaternion rot, float halfX, float halfZ) => footprints.Add(new Footprint(centre, rot, halfX, halfZ));

        float PlaceContainerBlock(float d, float side)
        {
            int cols = rng.Next(2, 5), rows = rng.Next(2, 4);
            float w = cols * 6.4f, depth = rows * 2.8f;
            Vector3 p = g.PointAt(d + w / 2f, out var t, out var r);
            int i = g.SampleAt(d + w / 2f);
            float front = FrontLine(i, side) + 1f;
            Quaternion rot = Quaternion.LookRotation(r * side, Vector3.up);
            Vector3 centre = p + r * side * (front + depth / 2f);
            var fp = new Footprint(centre, rot, w / 2f + 0.3f, depth / 2f + 0.3f);
            foreach (var c in fp.Corners()) if (g.DistanceToCentreLine(new Vector3(c.x, 0, c.y)) < front - 0.8f) return 3f;
            foreach (var other in footprints) if (fp.Overlaps(other)) return 3f;
            footprints.Add(fp);
            var block = Child(city, "Container Stack");
            if (props.Barrel != null)
                for (int b = 0; b < 3 + rng.Next(3); b++)
                    Place(props.Barrel, centre + rot * new Vector3(-w / 2f + 0.5f + b * 0.62f, 0f, depth / 2f + 0.5f + (float)rng.NextDouble() * 0.3f),
                        Quaternion.Euler(0, rng.Next(360), 0), 1f);
            for (int cx = 0; cx < cols; cx++)
                for (int rz = 0; rz < rows; rz++)
                {
                    int height = rng.Next(1, 5);
                    for (int h = 0; h < height; h++)
                    {
                        var go = AddMesh("Container", block, props.ContainerMesh, m.Container[rng.Next(m.Container.Length)]);
                        Vector3 local = new Vector3(-w / 2f + 3.2f + cx * 6.4f, h * 2.6f, -depth / 2f + 1.4f + rz * 2.8f);
                        go.transform.SetPositionAndRotation(centre + rot * local, rot * Quaternion.Euler(0, 0, 0));
                    }
                }
            return w;
        }

        void PlaceLamp(Vector3 position, Quaternion rotation, GameObject prefab = null)
        {
            var lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab != null ? prefab : props.StreetLight, lights);
            lamp.transform.SetPositionAndRotation(position, rotation);
            lampPositions.Add(position);
            lightCount++;
        }

        void PlaceLampsAndTrees(float side)
        {
            // Consistent spacing (PPT slide 6): 40 m per side, staggered so the road gets a light every 20 m.
            float phase = side > 0 ? 0f : 20f;
            for (float d = phase + 6f; d < g.Length; d += 40f)
            {
                int i = g.SampleAt(d);
                var district = def.DistrictAt(d / g.Length);
                var style = StyleOf(district);
                if (!sidewalkMask[side > 0 ? 1 : 0][i]) continue;
                Vector3 p = g.PointAt(d, out var t, out var r);
                if (style.mastLights && side != outside)
                {
                    if (Mathf.RoundToInt(d / 40f) % 2 == 0)
                    {
                        var mast = (GameObject)PrefabUtility.InstantiatePrefab(props.MastLight, lights);
                        mast.transform.position = p + r * side * (FrontLine(i, side) - 1.2f);
                        lightCount++;
                    }
                    continue;
                }
                bool hillOutside = def.theme == RouteTheme.Hillside && side == outside;
                if (hillOutside) continue;
                float lateral = g.SidewalkInner(i) + 0.4f;
                bool heritage = props.HeritageLamp != null && (district == District.WaterfrontPromenade || district == District.CivicSquare) && side == outside;
                PlaceLamp(p + r * side * (heritage ? lateral + 0.5f : lateral) + Vector3.up * TrackGeometryBuilder.SidewalkHeight, Quaternion.LookRotation(-r * side, Vector3.up),
                    heritage ? props.HeritageLamp : null);

                // Street furniture between lamps: a utility cabinet at the back of the sidewalk.
                if (props.UtilityBox != null && !style.containers && rng.NextDouble() < 0.35)
                {
                    float ud = d + 9f;
                    int ui = g.SampleAt(ud);
                    if (sidewalkMask[side > 0 ? 1 : 0][ui])
                    {
                        Vector3 up2 = g.PointAt(ud, out _, out var ur);
                        Place(props.UtilityBox, up2 + ur * side * (g.SidewalkInner(ui) + def.sidewalkWidth - 0.45f) + Vector3.up * TrackGeometryBuilder.SidewalkHeight,
                            Quaternion.LookRotation(-ur * side, Vector3.up), 1f);
                    }
                }

                // Tree halfway between lamps at the back of the sidewalk.
                float td = d + 20f;
                int ti = g.SampleAt(td);
                if (!sidewalkMask[side > 0 ? 1 : 0][ti] || SideIsScenery(def.DistrictAt(td / g.Length), side)) continue;
                Vector3 tp = g.PointAt(td, out var tt, out var tr) + tr * side * (g.SidewalkInner(ti) + def.sidewalkWidth - 0.9f) + Vector3.up * TrackGeometryBuilder.SidewalkHeight;
                var treePrefab = style.palms ? props.Palm : props.Tree;
                if (style.containers) continue;
                var treeRot = props.ScannedTrees
                    ? Quaternion.LookRotation(tr, Vector3.up) * Quaternion.Euler(0, (float)rng.NextDouble() * 24f - 12f, 0)   // long axis along the road
                    : Quaternion.Euler(0, rng.Next(360), 0);
                Place(treePrefab, tp, treeRot, 0.85f + (float)rng.NextDouble() * 0.3f);
            }
        }

        GameObject Place(GameObject prefab, Vector3 position, Quaternion rotation, float scale, bool dressingParent = true)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, dressingParent ? dressing : city);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        /// <summary>
        /// Spectators stand on the sidewalk behind the barrier (PPT slide 4), never on the racing
        /// surface; clear of lamp posts, trees, side streets and market stalls.
        /// </summary>
        void PlaceSpectators(float side)
        {
            var mat = m.Spectator;
            for (float d = 2f; d < g.Length; d += 0.85f + (float)rng.NextDouble() * 0.5f)
            {
                int i = g.SampleAt(d);
                var district = def.DistrictAt(d / g.Length);
                var style = StyleOf(district);
                bool hillOutside = def.theme == RouteTheme.Hillside && side == outside;
                bool promenade = district == District.WaterfrontPromenade && side == outside;
                if (hillOutside || !sidewalkMask[side > 0 ? 1 : 0][i]) continue;
                if (SideIsScenery(district, side) && !promenade) continue;
                float density = style.crowd;
                if (d < 90f || d > g.Length - 60f) density = Mathf.Max(density, 0.92f);     // start straight
                for (int row = 0; row < style.crowdRows + (d < 90f ? 1 : 0); row++)
                {
                    if (rng.NextDouble() > density * (row == 0 ? 1f : 0.65f)) continue;
                    Vector3 p = g.PointAt(d, out var t, out var r);
                    float lateral = g.SidewalkInner(i) + 0.55f + row * 0.75f + (float)rng.NextDouble() * 0.2f;
                    Vector3 pos = p + r * side * lateral + t * ((float)rng.NextDouble() - 0.5f) * 0.3f + Vector3.up * TrackGeometryBuilder.SidewalkHeight;
                    bool blocked = false;
                    foreach (var lp in lampPositions) if ((lp - pos).sqrMagnitude < 0.8f) { blocked = true; break; }
                    if (blocked) continue;
                    var go = new GameObject("Spectator");
                    go.transform.SetParent(crowd, false);
                    go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(r * side, Vector3.up) * Quaternion.Euler(0, rng.Next(-25, 26), 0));
                    float s = 0.92f + (float)rng.NextDouble() * 0.16f;
                    go.transform.localScale = new Vector3(s, s * (0.96f + (float)rng.NextDouble() * 0.08f), s);
                    go.AddComponent<MeshFilter>().sharedMesh = props.Spectators[rng.Next(props.Spectators.Count)];
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    spectatorCount++;
                }
                if (style.stalls && Mathf.FloorToInt(d / 7f) != Mathf.FloorToInt((d - 1.2f) / 7f) && rng.NextDouble() < 0.6)
                {
                    Vector3 p = g.PointAt(d, out var t, out var r);
                    Vector3 sp = p + r * side * (g.SidewalkInner(i) + def.sidewalkWidth - 1.1f) + Vector3.up * TrackGeometryBuilder.SidewalkHeight;
                    Place(props.Stall, sp, Quaternion.LookRotation(-r * side, Vector3.up) * Quaternion.Euler(0, 180f, 0), 0.9f);
                }
            }
        }

        void PlaceChevrons()
        {
            // Direction boards on the outside of the tighter corners, facing approaching cars.
            int i = 0;
            int lastPlaced = -1000;
            while (i < g.N)
            {
                if (Mathf.Abs(g.Curv[i]) > 1f / 60f && i - lastPlaced > 18)
                {
                    int apex = i;
                    for (int k = i; k < i + 20 && k < g.N; k++) if (Mathf.Abs(g.Curv[k]) > Mathf.Abs(g.Curv[apex])) apex = k;
                    float side = -Mathf.Sign(g.Curv[apex]);
                    if (!(def.theme == RouteTheme.Hillside && side == outside))
                    {
                        Vector3 p = g.Pts[apex] + g.Right[apex] * side * (g.SidewalkInner(apex) + 0.6f) + Vector3.up * TrackGeometryBuilder.SidewalkHeight;
                        Vector3 approach = g.Tangent[g.Idx(apex - 12)];
                        var board = Place(props.Chevron, p, Quaternion.LookRotation(approach, Vector3.up), 1f);
                        if (side > 0) board.transform.localScale = new Vector3(-1f, 1f, 1f);
                    }
                    else
                    {
                        // Hillside outer corners: board mounted just behind the guardrail.
                        Vector3 p = g.Pts[apex] + g.Right[apex] * side * (g.Barrier(apex) + 1.2f);
                        var board = Place(props.Chevron, p, Quaternion.LookRotation(g.Tangent[g.Idx(apex - 12)], Vector3.up), 1f);
                        if (side > 0) board.transform.localScale = new Vector3(-1f, 1f, 1f);
                    }
                    lastPlaced = apex;
                    i = apex + 10;
                }
                i++;
            }
        }

        void PlaceBillboards()
        {
            float[] fractions = { 0.08f, 0.29f, 0.51f, 0.7f, 0.9f };
            foreach (float f in fractions)
            {
                float d = f * g.Length;
                Vector3 p = g.PointAt(d, out var t, out var r);
                int i = g.SampleAt(d);
                float side = -outside;   // inside of the loop has more open space for boards
                if (def.theme == RouteTheme.Hillside) side = -outside;
                Vector3 pos = p + r * side * (FrontLine(i, side) + 3f);
                Quaternion rot = Quaternion.LookRotation((t - r * side * 0.6f).normalized, Vector3.up);
                var fp = new Footprint(pos, rot, 5f, 1.5f);
                bool clear = footprints.All(o => !fp.Overlaps(o)) && fp.Corners().All(c => g.DistanceToCentreLine(new Vector3(c.x, 0, c.y)) > FrontLine(i, side) - 0.5f);
                if (!clear) continue;
                footprints.Add(fp);
                Place(props.Billboard, pos, rot, 1f, false);
            }
        }

        void PlaceCivicSquare()
        {
            int s = System.Array.FindIndex(def.sectors, x => x.district == District.CivicSquare);
            if (s < 0) return;
            var sector = def.sectors[s];
            float d = (sector.start + Mathf.Repeat(sector.end - sector.start, 1f) * 0.5f) * g.Length;
            Vector3 p = g.PointAt(d, out var t, out var r);
            int i = g.SampleAt(d);
            float side = -outside;
            float front = FrontLine(i, side);
            Quaternion rot = Quaternion.LookRotation(r * side, Vector3.up);
            Vector3 centre = p + r * side * (front + 26f);
            var plaza = new MeshKit();
            plaza.Box(centre + Vector3.up * 0.06f, new Vector3(56f, 0.12f, 50f), rot, 0, 2f);
            AddMesh("Civic Square Plaza", city, plaza.Build("Plaza"), m.Plaza);
            Reserve(centre, rot, 30f, 27f);
            Place(props.Monument, centre + Vector3.up * 0.12f, rot, 1f, false);
            for (int k = 0; k < 10; k++)
            {
                float a = k / 10f * Mathf.PI * 2f;
                Vector3 off = rot * new Vector3(Mathf.Cos(a) * 19f, 0.12f, Mathf.Sin(a) * 16f);
                Place(props.Palm, centre + off, Quaternion.Euler(0, rng.Next(360), 0), 0.95f + (float)rng.NextDouble() * 0.2f);
                if (k % 2 == 0) Place(props.Bench, centre + off * 0.78f, Quaternion.LookRotation(-off, Vector3.up), 1f);
            }
            for (int k = 0; k < 4; k++)
            {
                Vector3 off = rot * new Vector3(k < 2 ? -24f : 24f, 0.12f, k % 2 == 0 ? -21f : 21f);
                PlaceLamp(centre + off, Quaternion.LookRotation(-off, Vector3.up));
            }
        }

        // ================================================================== Skyline & probes

        void BuildSkyline()
        {
            var sky = Child(city, "Skyline");
            Vector3 c = new Vector3((g.Pts.Min(p => p.x) + g.Pts.Max(p => p.x)) / 2f, 0, (g.Pts.Min(p => p.z) + g.Pts.Max(p => p.z)) / 2f);
            float rx = (g.Pts.Max(p => p.x) - g.Pts.Min(p => p.x)) / 2f, rz = (g.Pts.Max(p => p.z) - g.Pts.Min(p => p.z)) / 2f;
            int count = def.theme == RouteTheme.Hillside ? 0 : 70;
            for (int k = 0; k < count; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = 1f + 0.55f + (float)rng.NextDouble() * 1.6f;
                Vector3 p = c + new Vector3(Mathf.Cos(a) * (rx * dist + 140f), 0f, Mathf.Sin(a) * (rz * dist + 140f));
                bool overWater = (def.theme == RouteTheme.Waterfront && p.z < CoastZ()) || (def.theme == RouteTheme.IndustrialPort && p.z > CoastZ());
                if (overWater)
                {
                    // Distant city across the bay.
                    p = c + new Vector3(Mathf.Cos(a) * (rx + 700f + (float)rng.NextDouble() * 500f), 0f, Mathf.Sin(a) * (rz + 900f));
                    p.y = -1.6f;
                }
                if (g.DistanceToCentreLine(p) < 120f) continue;
                var spec = BuildingKit.Pick(rng.NextDouble() < 0.7 ? BuildingType.Tower : BuildingType.Office, rng);
                BuildingKit.Create(spec, m, sky, p, Quaternion.Euler(0, rng.Next(0, 8) * 45f, 0), rng, false);
            }
        }

        /// <summary>
        /// Box-projected reflection probes about every 120 m along the track, baked when the scene is
        /// built (no runtime cost): car paint, glass and the wet road reflect the street they're on.
        /// </summary>
        void BuildReflectionProbes()
        {
            var t = Child(city, "Reflection Probes");
            int count = Mathf.Max(6, Mathf.RoundToInt(g.Length / 120f));
            for (int k = 0; k < count; k++)
            {
                Vector3 p = g.PointAt((k + 0.5f) / count * g.Length, out _, out _);
                var go = new GameObject("Reflection Probe " + (k + 1));
                go.transform.SetParent(t, false);
                go.transform.position = p + Vector3.up * 2.5f;
                var probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Baked;
                probe.size = new Vector3(150f, 44f, 150f);
                probe.center = new Vector3(0f, 19.5f, 0f);   // box from the road surface up to ~42 m
                probe.boxProjection = true;
                probe.resolution = 128;
                probe.hdr = true;
                probe.farClipPlane = 700f;
                probe.blendDistance = 25f;
                probes.Add(probe);
            }
        }

        void BakeReflectionProbes(string scenePath)
        {
            string folder = scenePath.Substring(0, scenePath.Length - ".unity".Length);
            ProceduralAssets.EnsureFolder(folder);
            for (int k = 0; k < probes.Count; k++)
            {
                string path = $"{folder}/ReflectionProbe-{k}.exr";
                if (Lightmapping.BakeReflectionProbe(probes[k], path))
                    probes[k].bakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            }
        }

        static void MarkStatic(GameObject rootGo)
        {
            foreach (var tr in rootGo.GetComponentsInChildren<Transform>(true))
            {
                if (tr.GetComponentInParent<Canvas>(true) != null) continue;
                if (tr.GetComponent<Light>() != null || tr.GetComponent<ReflectionProbe>() != null) continue;
                // Scanned props (LOD-grouped) are many copies of one heavy mesh: GPU instancing beats batching them.
                if (tr.GetComponentInParent<LODGroup>(true) != null) { GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.OccludeeStatic); continue; }
                GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
        }
    }
}
