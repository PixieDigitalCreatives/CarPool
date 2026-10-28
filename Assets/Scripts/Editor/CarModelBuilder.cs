using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RacingPrototype.EditorTools
{
    /// <summary>
    /// The playable cars. Each car is one Style: an imported model from the client's own assets when
    /// one is set and present, else an original low-poly body lofted from cross-section stations.
    /// Front plate: thegameio + race number (PPT branding touchpoint); rear plate: unisolutions.in
    /// (handwritten requirement).
    /// </summary>
    public static class CarModelBuilder
    {
        public struct Station
        {
            public float z, hw, bottom, top;   // z, hw normalised (z -0.5 rear .. 0.5 front, hw 0..1 of half width)
            public Station(float z, float hw, float bottom, float top) { this.z = z; this.hw = hw; this.bottom = bottom; this.top = top; }
        }

        public enum Lights { Round, Slim, Rect, Quad }
        public enum Tail { Bar, Pair }
        public enum Wing { None, Ducktail, Lip, Active, Large, Roof }

        public class Style
        {
            public string id, category, modelName, description;
            public float length, width, wheelRadius, wheelWidth, wheelbase, trackWidth, rideLift;
            public Station[] body; public float bodyRoundTop, bodyRoundBottom;
            public Station[] cabin; public float cabinRound;
            public Color colour;
            public Lights headlights; public Tail taillights; public Wing wing;
            public bool sideSkirts, sideAccent, hoodScoop, chromeBumpers, stripes, mudFlaps, lightPod, nitro, quadExhaust;
            // Real tuning; the garage bars are derived from these (CarStats).
            public float topSpeed, acceleration, lowSteer, highSteer, grip, mass;
            public string icon = "UI/Icon_Car";   // garage card icon (Resources sprite)
            // Imported model (client's Asset Store car): prefab or FBX path; null or missing = generated body.
            public string importedModel;
            public string paletteVariants;        // palette-textured models: folder of colour-variant palettes
            public string paintMaterials;         // other models: regex for the paint material slots
            public string importedBodyLod;        // optional LOD body (FBX with an LOD group) replacing the plain body
            public string importedWheel;          // optional wheel prefab fitted on the model's wheel anchors
            public float importedYaw;             // when the model doesn't face +Z
        }

        /// <summary>
        /// The garage line-up: the client's own cars (user decision, replacing the PPT's 5 categories
        /// for now): Regular = "ARCADE: FREE Racing Car" (Mena), Sport = "Sport Car Free" (ALIyerEdon),
        /// both from the Unity Asset Store. Add a car by adding a Style that points importedModel at its
        /// prefab or FBX: the catalog, garage, AI grid and stat bars pick it up. If a package is missing
        /// the car falls back to a generated lofted body so the project still builds.
        /// </summary>
        public static Style[] Styles() => new[]
        {
            new Style
            {
                id = "Regular", category = "Regular", modelName = "thegameio Street",
                description = "Balanced street coupe, easy to drive",
                icon = "UI/Icon_Car",
                // "ARCADE: FREE Racing Car" (Mena): palette-textured, repainted per paint cell.
                importedModel = "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car.prefab",
                paletteVariants = "Assets/ARCADE - FREE Racing Car/Textures/Color Variations",
                length = 4.25f, width = 1.82f, wheelRadius = 0.33f, wheelWidth = 0.24f, wheelbase = 2.62f, trackWidth = 1.56f, rideLift = 0.04f,
                body = new[] { S(-0.5f, 0.86f, 0.32f, 0.92f), S(-0.45f, 0.97f, 0.26f, 0.98f), S(-0.2f, 1f, 0.24f, 0.98f), S(0.1f, 1f, 0.24f, 0.94f),
                               S(0.3f, 0.98f, 0.25f, 0.88f), S(0.45f, 0.93f, 0.28f, 0.8f), S(0.5f, 0.84f, 0.32f, 0.68f) },
                bodyRoundTop = 3.5f, bodyRoundBottom = 6f,
                cabin = new[] { S(-0.42f, 0.74f, 0.92f, 0.98f), S(-0.33f, 0.8f, 0.92f, 1.36f), S(-0.05f, 0.82f, 0.9f, 1.42f), S(0.12f, 0.8f, 0.88f, 1.34f), S(0.26f, 0.72f, 0.84f, 0.92f) },
                cabinRound = 3.2f,
                colour = new Color(1f, 0.71f, 0f), headlights = Lights.Rect, taillights = Tail.Pair, wing = Wing.Roof,
                topSpeed = 48f, acceleration = 12f, lowSteer = 108f, highSteer = 60f, grip = 10f, mass = 1200f
            },
            new Style
            {
                id = "Sport", category = "Sport", modelName = "Apex GT",
                description = "Sports car, the fastest on the grid",
                icon = "UI/Icon_Bolt",
                // "Sport Car Free" (ALIyerEdon): body with LODs, separate wheel prefab on its wheel anchors.
                importedModel = "Assets/SportCar/Prefabs/SportCar_1.prefab",
                importedBodyLod = "Assets/SportCar/Models/SportCar_1/SportCar_1_LOD.FBX",
                importedWheel = "Assets/SportCar/Prefabs/Wheels/Sport/Sport_Wheel_1.prefab",
                paintMaterials = "^Body1$",
                length = 4.6f, width = 2.04f, wheelRadius = 0.36f, wheelWidth = 0.31f, wheelbase = 2.74f, trackWidth = 1.74f, rideLift = 0f,
                body = new[] { S(-0.5f, 0.82f, 0.3f, 0.8f), S(-0.46f, 0.95f, 0.22f, 0.92f), S(-0.34f, 1f, 0.17f, 0.97f), S(-0.12f, 0.98f, 0.17f, 0.94f),
                               S(0.12f, 0.95f, 0.17f, 0.86f), S(0.32f, 0.97f, 0.17f, 0.76f), S(0.44f, 0.9f, 0.19f, 0.62f), S(0.5f, 0.72f, 0.24f, 0.5f) },
                bodyRoundTop = 3.2f, bodyRoundBottom = 6f,
                cabin = new[] { S(-0.3f, 0.5f, 0.86f, 0.92f), S(-0.2f, 0.68f, 0.85f, 1.1f), S(-0.02f, 0.74f, 0.83f, 1.17f), S(0.12f, 0.7f, 0.79f, 1.11f), S(0.28f, 0.58f, 0.72f, 0.8f) },
                cabinRound = 3.5f,
                colour = new Color(0.72f, 0.03f, 0.04f), headlights = Lights.Slim, taillights = Tail.Bar, wing = Wing.Active, sideAccent = true,
                topSpeed = 53f, acceleration = 15f, lowSteer = 104f, highSteer = 56f, grip = 9f, mass = 1350f
            },
        };

        static Station S(float z, float hw, float bottom, float top) => new Station(z, hw, bottom, top);

        // ------------------------------------------------------------------ Prefab

        public static GameObject BuildPrefab(Style s, CityMaterials m, AudioClip engine, string meshFolder, string prefabFolder, int raceNumber)
        {
            var root = new GameObject(s.id);
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(0f, s.rideLift, 0f);
            var paint = m.Paint[s.id];
            var paintRenderers = new List<Renderer>();
            var paintSlots = new List<int>();
            var pivots = new List<Transform>();
            var spinners = new List<Transform>();
            var flames = new List<Transform>();
            float wheelRadius = s.wheelRadius;
            Texture2D paintPalette = null;
            List<Renderer> tailRenderers = null;
            List<int> tailSlots = null;
            // Collider / headlight extent: the style's nominal size, or the imported model's real one.
            var extent = new Bounds(new Vector3(0f, 0.58f, 0f), new Vector3(s.width, 1.16f, s.length));

            var imported = ImportedModel(s);
            if (imported != null)
            {
                var car = BuildImportedVisual(s, imported, m, root.transform, visual, raceNumber, pivots, spinners, paintRenderers, paintSlots);
                wheelRadius = car.wheelRadius;
                paintPalette = car.palette;
                tailRenderers = car.tailRenderers;
                tailSlots = car.tailSlots;
                float height = Mathf.Min(car.bounds.size.y, 1.4f) * 0.85f;
                extent = new Bounds(new Vector3(0f, height / 2f, car.bounds.center.z), new Vector3(car.bounds.size.x * 0.98f, height, car.bounds.size.z));
            }
            else
                BuildGeneratedVisual(s, m, meshFolder, root.transform, visual, paint, raceNumber, pivots, spinners, paintRenderers, paintSlots, out flames);

            // Headlight beam: one cookie-shaped spot per car (two lamps' low-beam pattern) keeps the
            // light count low for 10 cars.
            var beamGo = new GameObject("Headlight Beam");
            beamGo.transform.SetParent(visual, false);
            beamGo.transform.localPosition = new Vector3(0f, 0.65f, extent.max.z + 0.05f);
            beamGo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
            var beam = beamGo.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.spotAngle = 72f;
            beam.innerSpotAngle = 40f;
            beam.range = 50f;
            beam.intensity = 10f;
            beam.cookie = HeadlightCookie();
            beam.color = new Color(0.95f, 0.96f, 1f);
            beam.shadows = LightShadows.None;

            // Physics + gameplay components.
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = s.mass;
            rb.linearDamping = 0f;
            rb.angularDamping = 1.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.centerOfMass = new Vector3(0f, 0.35f, 0f);
            var box = root.AddComponent<BoxCollider>();
            box.center = extent.center;
            box.size = new Vector3(extent.size.x * 0.96f, extent.size.y, extent.size.z * 0.97f);

            var controller = root.AddComponent<CarController>();
            controller.ConfigurePerformance(s.topSpeed, s.acceleration, s.lowSteer, s.highSteer, s.grip);
            controller.ConfigureVisuals(pivots.ToArray(), spinners.ToArray(), wheelRadius);
            root.AddComponent<RaceParticipant>();
            var ai = root.AddComponent<AIController>();
            ai.enabled = false;
            var input = root.AddComponent<PlayerCarInput>();
            input.enabled = false;
            var customizer = root.AddComponent<CarColourCustomizer>();
            customizer.Configure(paintRenderers.ToArray(), paintSlots.ToArray(), s.colour);
            if (paintPalette != null) customizer.ConfigurePalette(paintPalette);
            if (tailRenderers != null && tailRenderers.Count > 0)
                root.AddComponent<CarLights>().Configure(tailRenderers.ToArray(), tailSlots.ToArray(), new Color(2.2f, 0.08f, 0.05f));
            var source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            root.AddComponent<CarAudio>().Configure(engine);
            if (flames.Count > 0) root.AddComponent<NitroExhaustFx>().Configure(flames.ToArray());

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{prefabFolder}/{s.id}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>The original lofted model: body, canopy, optional stripes, wheels and details.</summary>
        static void BuildGeneratedVisual(Style s, CityMaterials m, string meshFolder, Transform root, Transform visual, Material paint, int raceNumber,
            List<Transform> pivots, List<Transform> spinners, List<Renderer> paintRenderers, List<int> paintSlots, out List<Transform> flames)
        {
            var bodyMesh = ProceduralAssets.SaveOrReplace(Loft($"{s.id}_Body", s.body, s.length, s.width, s.bodyRoundTop, s.bodyRoundBottom, false), $"{meshFolder}/{s.id}_Body.asset");
            var body = MeshObject("Body", visual, bodyMesh, paint, m.CarTrim);
            paintRenderers.Add(body); paintSlots.Add(0);
            var cabinMesh = ProceduralAssets.SaveOrReplace(Loft($"{s.id}_Canopy", s.cabin, s.length, s.width, s.cabinRound, s.cabinRound, true), $"{meshFolder}/{s.id}_Canopy.asset");
            var cabin = MeshObject("Canopy", visual, cabinMesh, m.Glass, paint);
            paintRenderers.Add(cabin); paintSlots.Add(1);

            if (s.stripes)
            {
                // Twin livery stripes lofted just above the body's centre line.
                var stripe = new List<Station>();
                foreach (var st in s.body) stripe.Add(new Station(st.z, 0.16f, st.top - 0.02f, st.top + 0.012f));
                var stripeMesh = ProceduralAssets.SaveOrReplace(Loft($"{s.id}_Stripe", stripe.ToArray(), s.length, s.width, 8f, 8f, false, true), $"{meshFolder}/{s.id}_Stripe.asset");
                MeshObject("Stripes", visual, stripeMesh, s.colour.grayscale > 0.5f ? m.StripeBlack : m.StripeWhite, m.StripeWhite);
            }

            // Wheels: tyre, rim, spokes; front pivots steer.
            float axleShift = 0.02f * s.length;
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                float side = i % 2 == 0 ? -1f : 1f;
                float z = (front ? 0.5f : -0.5f) * s.wheelbase + axleShift;
                var pivot = new GameObject((front ? "Front" : "Rear") + (side < 0 ? "Left" : "Right") + "Wheel").transform;
                pivot.SetParent(root, false);
                pivot.localPosition = new Vector3(side * s.trackWidth * 0.5f, s.wheelRadius, z);
                var spin = new GameObject("Spin").transform;
                spin.SetParent(pivot, false);
                Prim(PrimitiveType.Cylinder, "Tyre", spin, Vector3.zero, Quaternion.Euler(0, 0, 90), new Vector3(s.wheelRadius * 2f, s.wheelWidth * 0.5f, s.wheelRadius * 2f), m.Tyre);
                Prim(PrimitiveType.Cylinder, "Rim", spin, new Vector3(side * 0.012f, 0, 0), Quaternion.Euler(0, 0, 90), new Vector3(s.wheelRadius * 1.32f, s.wheelWidth * 0.5f + 0.004f, s.wheelRadius * 1.32f), m.Rim);
                for (int k = 0; k < 3; k++)
                    Prim(PrimitiveType.Cube, "Spoke", spin, new Vector3(side * 0.03f, 0, 0), Quaternion.Euler(k * 60f, 0, 0), new Vector3(s.wheelWidth + 0.01f, s.wheelRadius * 1.22f, 0.07f), m.Chrome);
                if (front) pivots.Add(pivot);
                spinners.Add(spin);
            }

            AddDetails(s, m, visual, paint, paintRenderers, paintSlots, raceNumber, out flames);
        }

        // ------------------------------------------------------------------ Imported models (client's Asset Store cars)

        const string ImportedFolder = "Assets/Materials/Cars/Imported";

        struct ImportedCar
        {
            public float wheelRadius;
            public Bounds bounds;
            public Texture2D palette;
            public List<Renderer> tailRenderers;
            public List<int> tailSlots;
        }

        static GameObject ImportedModel(Style s) =>
            string.IsNullOrEmpty(s.importedModel) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(s.importedModel);

        /// <summary>
        /// Builds a car's visual from an imported model (prefab or FBX). Optional package pieces are fitted
        /// first: an LOD body replacing the plain one, and a wheel prefab on the model's wheel anchors.
        /// Wheels are then found by name (wheel / tyre / tire) and each is moved onto a steering pivot +
        /// spin transform at its hub; everything else stays as the body. The model is centred, stood on
        /// its wheels, and rescaled only when its length is clearly not in metres. Built-in Standard
        /// materials become URP: clear-coated Complex Lit paint, emissive head and tail lights.
        /// </summary>
        static ImportedCar BuildImportedVisual(Style s, GameObject model, CityMaterials m, Transform root, Transform visual, int raceNumber,
            List<Transform> pivots, List<Transform> spinners, List<Renderer> paintRenderers, List<int> paintSlots)
        {
            ProceduralAssets.EnsureFolder(ImportedFolder);
            var result = new ImportedCar { tailRenderers = new List<Renderer>(), tailSlots = new List<int>() };
            var inst = (GameObject)Object.Instantiate(model);
            inst.name = "Model";
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, s.importedYaw, 0f));
            // The package's own physics and scripts aren't used: CarController drives the car.
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var b in inst.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(b);
            foreach (var b in inst.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(b);
            if (!string.IsNullOrEmpty(s.importedBodyLod)) FitLodBody(inst, s.importedBodyLod);
            if (!string.IsNullOrEmpty(s.importedWheel)) FitWheels(inst, s.importedWheel);

            Bounds Measure(Component c)
            {
                var rs = c.GetComponentsInChildren<Renderer>(true);
                var bb = rs[0].bounds;
                foreach (var r in rs) bb.Encapsulate(r.bounds);
                return bb;
            }
            var bounds = Measure(inst.transform);
            if (bounds.size.z < 3.2f || bounds.size.z > 6f)
            {
                inst.transform.localScale *= s.length / bounds.size.z;
                bounds = Measure(inst.transform);
            }

            // Wheels: the top-most parts named like a wheel (not a steering wheel or arch); a match bigger
            // than a wheel is a group ("Wheels") and is replaced by its children. Rim, tyre and brake parts
            // of one wheel are grouped by hub so they turn together. Exactly 4 hubs, or no animated wheels.
            var wheelName = new Regex("wheel|tyre|tire", RegexOptions.IgnoreCase);
            var notWheel = new Regex("steer|arch|well|cover|collider", RegexOptions.IgnoreCase);
            var candidates = inst.GetComponentsInChildren<Transform>(true)
                .Where(t => t != inst.transform && wheelName.IsMatch(t.name) && !notWheel.IsMatch(t.name) && t.GetComponentInChildren<Renderer>(true) != null).ToList();
            var parts = candidates.Where(t => !candidates.Any(o => o != t && t.IsChildOf(o))).ToList();
            bool TooBig(Transform t) { var size = Measure(t).size; return Mathf.Max(size.x, size.z) > 1.3f; }
            for (int depth = 0; depth < 4 && parts.Any(TooBig); depth++)
                parts = parts.SelectMany(p => TooBig(p) ? p.Cast<Transform>().Where(c => c.GetComponentInChildren<Renderer>(true) != null) : new[] { p }).ToList();
            var wheels = new List<List<Transform>>();
            foreach (var part in parts)
            {
                var centre = Measure(part).center;
                var hub = wheels.FirstOrDefault(g => (Measure(g[0]).center - centre).magnitude < 0.35f);
                if (hub != null) hub.Add(part); else wheels.Add(new List<Transform> { part });
            }
            if (wheels.Count != 4) wheels.Clear();   // can't animate: any wheel meshes stay on the body

            float ground = wheels.Count > 0 ? wheels.SelectMany(g => g).Min(w => Measure(w).min.y) : bounds.min.y;
            inst.transform.position += new Vector3(-bounds.center.x, -ground, -bounds.center.z);
            Physics.SyncTransforms();

            foreach (var hub in wheels)
            {
                var wb = Measure(hub[0]);
                foreach (var part in hub) wb.Encapsulate(Measure(part));
                var pivot = new GameObject((wb.center.z > 0f ? "Front" : "Rear") + (wb.center.x < 0f ? "Left" : "Right") + "Wheel").transform;
                pivot.SetParent(root, false);
                pivot.position = wb.center;
                var spin = new GameObject("Spin").transform;
                spin.SetParent(pivot, false);
                foreach (var part in hub) part.SetParent(spin, true);
                if (wb.center.z > 0f) pivots.Add(pivot);
                spinners.Add(spin);
                result.wheelRadius = Mathf.Max(result.wheelRadius, wb.extents.y);
            }
            if (result.wheelRadius <= 0f) result.wheelRadius = s.wheelRadius;
            inst.transform.SetParent(visual, true);
            result.bounds = Measure(root);

            // Materials, paint and lights.
            var paintName = string.IsNullOrEmpty(s.paintMaterials) ? null : new Regex(s.paintMaterials, RegexOptions.IgnoreCase);
            string paletteBase = null;
            if (!string.IsNullOrEmpty(s.paletteVariants))
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    foreach (var src in r.sharedMaterials)
                        if (src != null && src.mainTexture != null && AssetDatabase.GetAssetPath(src.mainTexture).StartsWith(s.paletteVariants))
                            paletteBase ??= AssetDatabase.GetAssetPath(src.mainTexture);
            Texture2D clearCoat = null;
            if (paletteBase != null) result.palette = PaintPalette(s.id, s.paletteVariants, paletteBase, out clearCoat);

            var converted = new Dictionary<Material, Material>();
            var tail = new Regex(@"rear.?light|back.?light|tail|brake|stop.?light", RegexOptions.IgnoreCase);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    bool paint = paletteBase != null
                        ? src.mainTexture != null && AssetDatabase.GetAssetPath(src.mainTexture) == paletteBase
                        : paintName != null && paintName.IsMatch(src.name);
                    mats[i] = ToUrp(s.id, src, converted, paint, clearCoat);
                    if (paint) { paintRenderers.Add(r); paintSlots.Add(i); }
                    if (tail.IsMatch(src.name)) { result.tailRenderers.Add(r); result.tailSlots.Add(i); }
                }
                r.sharedMaterials = mats;
            }

            // Plates on the real bumper faces, found by casting at the body meshes.
            const float frontY = 0.42f, rearY = 0.5f;
            var probes = new List<MeshCollider>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; probes.Add(mc); }
            Physics.SyncTransforms();
            float zFront = SurfaceZ(probes, frontY, 1f, result.bounds.max.z), zRear = SurfaceZ(probes, rearY, -1f, result.bounds.min.z);
            foreach (var p in probes) Object.DestroyImmediate(p);
            Prim(PrimitiveType.Cube, "Front Plate", visual, new Vector3(0, frontY, zFront + 0.012f), Quaternion.identity, new Vector3(0.56f, 0.13f, 0.02f), m.PlateLight);
            UIFactory.WorldText("Front Plate Text", visual, $"{BrandConfig.Current.wordmark}  {raceNumber:00}", new Vector3(0, frontY, zFront + 0.026f), Quaternion.Euler(0, 180f, 0),
                new Vector2(0.52f, 0.1f), new Color(0.1f, 0.11f, 0.14f));
            Prim(PrimitiveType.Cube, "Rear Plate", visual, new Vector3(0, rearY, zRear - 0.012f), Quaternion.identity, new Vector3(1.0f, 0.2f, 0.025f), m.PlateDark);
            UIFactory.WorldText("Rear Badge", visual, "unisolutions.in", new Vector3(0, rearY, zRear - 0.03f), Quaternion.identity, new Vector2(0.94f, 0.17f), Color.white);
            return result;
        }

        /// <summary>
        /// Swaps the model's plain body mesh for the package's LOD version (an FBX with an LOD group),
        /// aligned to the plain body's bounds and given the same materials by slot name. Skipped, keeping
        /// the plain body, when the LOD file has no LOD group or its material slots don't all match.
        /// </summary>
        static void FitLodBody(GameObject inst, string lodPath)
        {
            var lodModel = AssetDatabase.LoadAssetAtPath<GameObject>(lodPath);
            var body = inst.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh != null)
                .OrderByDescending(r => r.sharedMaterials.Length).FirstOrDefault();
            if (lodModel == null || body == null) return;
            var lod = (GameObject)Object.Instantiate(lodModel, inst.transform);
            var group = lod.GetComponentInChildren<LODGroup>();
            var byName = body.sharedMaterials.Where(x => x != null).GroupBy(x => x.name).ToDictionary(x => x.Key, x => x.First());
            bool allMatch = group != null && lod.GetComponentsInChildren<Renderer>(true)
                .All(r => r.sharedMaterials.All(x => x != null && byName.ContainsKey(x.name)));
            if (!allMatch)
            {
                Debug.LogWarning($"[CarModelBuilder] {lodPath}: no LOD group or unmatched materials; keeping the plain body.");
                Object.DestroyImmediate(lod);
                return;
            }
            lod.name = "Body LODs";
            lod.transform.SetPositionAndRotation(body.transform.position, body.transform.rotation);
            Bounds Lod0()
            {
                var rs = group.GetLODs()[0].renderers.Where(r => r != null).ToArray();
                var bb = rs[0].bounds;
                foreach (var r in rs) bb.Encapsulate(r.bounds);
                return bb;
            }
            var target = body.bounds;
            var b0 = Lod0();
            float k = target.size.z / Mathf.Max(b0.size.z, 0.001f);
            if (Mathf.Abs(k - 1f) > 0.02f) { lod.transform.localScale *= k; b0 = Lod0(); }
            lod.transform.position += target.center - b0.center;
            foreach (var r in lod.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = r.sharedMaterials.Select(x => byName[x.name]).ToArray();
            Object.DestroyImmediate(body.GetComponent<MeshFilter>());
            Object.DestroyImmediate(body);
            Debug.Log($"[CarModelBuilder] {lodPath}: body uses {group.lodCount} LODs");
        }

        /// <summary>
        /// Puts the package's wheel prefab on the model's 4 wheel anchors (FL / FR / RL / RR, or wheel
        /// nodes whose own mesh is missing); left-side wheels are turned round so the rim faces outwards.
        /// </summary>
        static void FitWheels(GameObject inst, string wheelPath)
        {
            var wheel = AssetDatabase.LoadAssetAtPath<GameObject>(wheelPath);
            if (wheel == null) return;
            var anchorName = new Regex(@"^(F|R)(L|R)$|wheel", RegexOptions.IgnoreCase);
            var anchors = inst.GetComponentsInChildren<Transform>(true)
                .Where(t => t != inst.transform && anchorName.IsMatch(t.name) && !(t.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh != null)).ToList();
            if (anchors.Count != 4)
            {
                Debug.LogWarning($"[CarModelBuilder] {wheelPath}: found {anchors.Count} wheel anchors, expected 4; wheels not fitted.");
                return;
            }
            float midX = anchors.Average(t => t.position.x);
            foreach (var a in anchors)
            {
                if (a.TryGetComponent<MeshRenderer>(out var mr)) Object.DestroyImmediate(mr);
                if (a.TryGetComponent<MeshFilter>(out var mf)) Object.DestroyImmediate(mf);
                var w = (GameObject)Object.Instantiate(wheel, a, false);
                w.name = "Wheel " + a.name;
                w.transform.localPosition = Vector3.zero;
                w.transform.localRotation = a.position.x < midX ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;   // rim faces +X
            }
        }

        /// <summary>Z of the nearest body surface along the car's axis at height y (dir +1 = from the front).</summary>
        static float SurfaceZ(List<MeshCollider> colliders, float y, float dir, float fallback)
        {
            var ray = new Ray(new Vector3(0f, y, dir * 20f), new Vector3(0f, 0f, -dir));
            float best = fallback, nearest = float.MaxValue;
            foreach (var c in colliders)
                if (c.Raycast(ray, out var hit, 40f) && hit.distance < nearest) { nearest = hit.distance; best = hit.point.z; }
            return best;
        }

        /// <summary>
        /// URP copy of a Built-in Standard material (URP materials are kept as they are). Paint becomes
        /// Complex Lit with a clear coat (masked to the paint cells for palette models); head and tail
        /// lights become emissive so they bloom at night.
        /// </summary>
        static Material ToUrp(string id, Material src, Dictionary<Material, Material> converted, bool paint, Texture2D clearCoatMap)
        {
            if (src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline")) return src;
            if (converted.TryGetValue(src, out var done)) return done;
            bool glossFromMap = src.HasProperty("_SmoothnessTextureChannel") && src.GetFloat("_SmoothnessTextureChannel") > 0.5f;
            var spec = new ProceduralAssets.MatSpec
            {
                colour = src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white,
                albedo = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null,
                normal = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null,
                normalScale = src.HasProperty("_BumpScale") ? src.GetFloat("_BumpScale") : 1f,
                metallic = src.HasProperty("_Metallic") ? src.GetFloat("_Metallic") : 0f,
                smoothness = glossFromMap && src.HasProperty("_GlossMapScale") ? src.GetFloat("_GlossMapScale") : src.HasProperty("_Glossiness") ? src.GetFloat("_Glossiness") : 0.5f,
            };
            string n = src.name;
            if (src.IsKeywordEnabled("_EMISSION") && src.HasProperty("_EmissionColor"))
            {
                spec.emission = src.GetColor("_EmissionColor") * 0.6f;   // night scene with bloom: Standard emission reads too hot
                spec.emissionMap = src.HasProperty("_EmissionMap") ? src.GetTexture("_EmissionMap") : null;
            }
            else if (Regex.IsMatch(n, @"rear.?light|back.?light|tail|brake|stop.?light", RegexOptions.IgnoreCase))
            {
                spec.emission = new Color(2.2f, 0.08f, 0.05f);
                spec.emissionMap = spec.albedo;
            }
            else if (Regex.IsMatch(n, @"front.?light|head.?light", RegexOptions.IgnoreCase))
            {
                spec.emission = new Color(1.7f, 1.7f, 1.6f);
                spec.emissionMap = spec.albedo;
            }
            var mat = ProceduralAssets.Mat($"{id}_{Regex.Replace(n, "[^A-Za-z0-9_]", "_")}", spec, ImportedFolder);
            if (paint)
            {
                // Car paint: base coat + glossy clear coat on top (reflects the street through the probes).
                mat.shader = Shader.Find("Universal Render Pipeline/Complex Lit");
                mat.SetFloat("_ClearCoat", 1f);
                mat.SetFloat("_ClearCoatMask", 1f);
                mat.SetFloat("_ClearCoatSmoothness", 0.95f);
                mat.SetFloat("_Smoothness", Mathf.Max(spec.smoothness, 0.55f));
                if (clearCoatMap != null)
                {
                    mat.SetTexture("_ClearCoatMap", clearCoatMap);
                    mat.EnableKeyword("_CLEARCOATMAP");
                }
                else mat.EnableKeyword("_CLEARCOAT");
                EditorUtility.SetDirty(mat);
            }
            if (src.HasProperty("_Mode") && src.GetFloat("_Mode") >= 2f)
            {
                // Standard Fade / Transparent (glass): URP transparent surface.
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
                EditorUtility.SetDirty(mat);
            }
            converted[src] = mat;
            return mat;
        }

        /// <summary>Soft low-beam headlight pattern (two hot spots, sharp top cut-off) used as a spot-light cookie.</summary>
        static Texture2D HeadlightCookie()
        {
            const string path = ImportedFolder + "/HeadlightCookie.png";
            if (!File.Exists(Path.GetFullPath(path)))
            {
                const int size = 256;
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (size - 1f) * 2f - 1f, v = y / (size - 1f) * 2f - 1f;
                        float ellipse = (u * u) / (0.95f * 0.95f) + (v * v) / (0.6f * 0.6f);
                        float beam = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.35f, ellipse));
                        float cut = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.34f, 0.1f, v));   // low beam: dark above the horizon
                        float hot = Mathf.Max(Mathf.Exp(-((u - 0.33f) * (u - 0.33f) / 0.06f + (v + 0.05f) * (v + 0.05f) / 0.04f)),
                                              Mathf.Exp(-((u + 0.33f) * (u + 0.33f) / 0.06f + (v + 0.05f) * (v + 0.05f) / 0.04f)));
                        bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                        byte value = edge ? (byte)0 : (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(beam * cut * (0.55f + 0.45f * hot)));
                        px[y * size + x] = new Color32(value, value, value, value);
                    }
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.SetPixels32(px);
                File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Cookie;
                importer.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Readable copy of a palette-textured model's palette for runtime repainting (CarColourCustomizer):
        /// the paint cells, i.e. the pixels that differ between the package's colour variants, hold their
        /// shade (from the least saturated variant, the artist's neutral grey) with alpha 1; trim, glass,
        /// lights and tyres keep the base palette's colour with alpha 0. A single _BaseColor tint can't be
        /// used because it would recolour the whole palette.
        /// </summary>
        static Texture2D PaintPalette(string id, string variantsFolder, string basePalette, out Texture2D clearCoat)
        {
            clearCoat = null;
            Color32[] Read(string assetPath, out int w, out int h)
            {
                var t = new Texture2D(2, 2);
                t.LoadImage(File.ReadAllBytes(Path.GetFullPath(assetPath)));
                w = t.width; h = t.height;
                var px = t.GetPixels32();
                Object.DestroyImmediate(t);
                return px;
            }
            var basePx = Read(basePalette, out int width, out int height);
            var variants = Directory.GetFiles(Path.GetFullPath(variantsFolder), "*.png")
                .Select(f => Read(variantsFolder + "/" + Path.GetFileName(f), out int vw, out int vh) is var px && vw == width && vh == height ? px : null)
                .Where(px => px != null).ToList();
            if (variants.Count < 2) return null;
            var mask = new bool[basePx.Length];
            for (int p = 0; p < basePx.Length; p++)
                foreach (var v in variants)
                {
                    var a = basePx[p]; var b = v[p];
                    if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 24) { mask[p] = true; break; }
                }
            float Saturation(Color32 c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float Luma(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            var grey = variants.OrderBy(v => Enumerable.Range(0, v.Length).Where(p => mask[p]).Sum(p => Saturation(v[p]))).First();
            float maxLuma = 1f;
            for (int p = 0; p < grey.Length; p++) if (mask[p]) maxLuma = Mathf.Max(maxLuma, Luma(grey[p]));
            var outPx = new Color32[basePx.Length];
            for (int p = 0; p < basePx.Length; p++)
            {
                var c = basePx[p];
                if (mask[p])
                {
                    byte shade = (byte)Mathf.Clamp(Mathf.RoundToInt(255f * Luma(grey[p]) / maxLuma), 0, 255);
                    outPx[p] = new Color32(shade, shade, shade, 255);
                }
                else outPx[p] = new Color32(c.r, c.g, c.b, 0);
            }
            // Clear-coat map for Complex Lit (R: coat mask, G: coat smoothness): only the paint cells are lacquered.
            var coat = outPx.Select(c => c.a > 127 ? new Color32(255, 235, 0, 255) : new Color32(0, 0, 0, 255)).ToArray();
            string coatPath = $"{ImportedFolder}/{id}_ClearCoat.png";
            var coatTex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            coatTex.SetPixels32(coat);
            File.WriteAllBytes(Path.GetFullPath(coatPath), coatTex.EncodeToPNG());
            Object.DestroyImmediate(coatTex);
            AssetDatabase.ImportAsset(coatPath, ImportAssetOptions.ForceUpdate);
            var coatImporter = (TextureImporter)AssetImporter.GetAtPath(coatPath);
            coatImporter.sRGBTexture = false;
            coatImporter.filterMode = FilterMode.Point;
            coatImporter.wrapMode = TextureWrapMode.Clamp;
            coatImporter.mipmapEnabled = false;
            coatImporter.SaveAndReimport();
            clearCoat = AssetDatabase.LoadAssetAtPath<Texture2D>(coatPath);

            string path = $"{ImportedFolder}/{id}_PaintPalette.png";
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(outPx);
            File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void AddDetails(Style s, CityMaterials m, Transform v, Material paint, List<Renderer> paintRenderers, List<int> paintSlots, int raceNumber, out List<Transform> flames)
        {
            flames = new List<Transform>();
            float L = s.length, W = s.width;
            float HalfW(float z) => Sample(s.body, z).hw * W * 0.5f;

            // Headlights
            {
                float z = 0.455f;
                var st = Sample(s.body, z);
                float x = HalfW(z) * 0.6f;
                float y = SurfaceY(st, 0.6f, s.bodyRoundTop) - 0.03f;
                for (int side = -1; side <= 1; side += 2)
                {
                    switch (s.headlights)
                    {
                        case Lights.Round:
                            Prim(PrimitiveType.Sphere, "Headlight", v, new Vector3(side * x, y, z * L), Quaternion.Euler(-10f, 0, 0), new Vector3(0.22f, 0.22f, 0.2f), m.HeadLight);
                            Prim(PrimitiveType.Sphere, "Headlight Inner", v, new Vector3(side * (x - 0.26f), y - 0.01f, z * L + 0.02f), Quaternion.identity, new Vector3(0.16f, 0.16f, 0.16f), m.HeadLight);
                            break;
                        case Lights.Slim:
                            Prim(PrimitiveType.Cube, "Headlight", v, new Vector3(side * x, y, z * L), Quaternion.Euler(-12f, side * -14f, 0), new Vector3(0.46f, 0.07f, 0.2f), m.HeadLight);
                            break;
                        case Lights.Quad:
                            Prim(PrimitiveType.Cube, "Headlight", v, new Vector3(side * x, y, z * L), Quaternion.Euler(-10f, 0, 0), new Vector3(0.34f, 0.12f, 0.18f), m.HeadLight);
                            break;
                        default:
                            Prim(PrimitiveType.Cube, "Headlight", v, new Vector3(side * x, y, z * L), Quaternion.Euler(-10f, 0, 0), new Vector3(0.38f, 0.11f, 0.18f), m.HeadLight);
                            break;
                    }
                }
            }

            // Nose: grille, front number plate (thegameio + race number), bumper / splitter.
            {
                var nose = Sample(s.body, 0.5f);
                float zf = 0.5f * L;
                float grilleW = nose.hw * W * (s.sideAccent ? 0.36f : 0.78f);
                if (s.sideAccent)
                    Prim(PrimitiveType.Sphere, "Grille", v, new Vector3(0, nose.bottom + 0.14f, zf), Quaternion.identity, new Vector3(grilleW, 0.22f, 0.1f), m.Grille);
                else
                    Prim(PrimitiveType.Cube, "Grille", v, new Vector3(0, nose.bottom + 0.12f, zf - 0.01f), Quaternion.identity, new Vector3(grilleW, 0.14f, 0.06f), m.Grille);
                float plateY = nose.bottom + (s.sideAccent ? 0.05f : 0.02f);
                Prim(PrimitiveType.Cube, "Front Plate", v, new Vector3(0, plateY, zf + 0.012f), Quaternion.identity, new Vector3(0.56f, 0.13f, 0.02f), m.PlateLight);
                var brand = BrandConfig.Current;
                UIFactory.WorldText("Front Plate Text", v, $"{brand.wordmark}  {raceNumber:00}", new Vector3(0, plateY, zf + 0.026f), Quaternion.Euler(0, 180f, 0),
                    new Vector2(0.52f, 0.1f), new Color(0.1f, 0.11f, 0.14f));
                if (s.chromeBumpers)
                {
                    Prim(PrimitiveType.Cube, "Front Bumper", v, new Vector3(0, nose.bottom + 0.06f, zf + 0.04f), Quaternion.identity, new Vector3(nose.hw * W * 1.02f, 0.1f, 0.1f), m.Chrome);
                }
                if (s.nitro || s.sideSkirts)
                    Prim(PrimitiveType.Cube, "Splitter", v, new Vector3(0, 0.12f, zf - 0.04f), Quaternion.identity, new Vector3(W * 0.92f, 0.04f, 0.28f), m.Carbon);
                if (s.lightPod)
                {
                    Prim(PrimitiveType.Cube, "Light Pod", v, new Vector3(0, Sample(s.body, 0.36f).top + 0.08f, 0.36f * L), Quaternion.identity, new Vector3(1.0f, 0.12f, 0.12f), m.Carbon);
                    for (int i = 0; i < 4; i++)
                        Prim(PrimitiveType.Cylinder, "Pod Lamp", v, new Vector3(-0.36f + i * 0.24f, Sample(s.body, 0.36f).top + 0.08f, 0.36f * L + 0.07f), Quaternion.Euler(90, 0, 0), new Vector3(0.16f, 0.02f, 0.16f), m.HeadLight);
                }
            }

            // Rear: lights, unisolutions.in plate, diffuser, exhausts (nitro flames on the tuner).
            {
                var tail = Sample(s.body, -0.5f);
                float zr = -0.5f * L;
                float tailW = tail.hw * W * 0.5f;
                float lightY = tail.top - 0.1f;
                if (s.taillights == Tail.Bar)
                    Prim(PrimitiveType.Cube, "Taillight", v, new Vector3(0, lightY, zr - 0.005f), Quaternion.identity, new Vector3(tailW * 1.72f, 0.07f, 0.05f), m.TailLight);
                else
                    for (int side = -1; side <= 1; side += 2)
                        Prim(PrimitiveType.Cube, "Taillight", v, new Vector3(side * tailW * 0.64f, lightY, zr - 0.005f), Quaternion.identity, new Vector3(0.38f, 0.1f, 0.05f), m.TailLight);

                float plateY = Mathf.Lerp(tail.bottom, tail.top, 0.42f);
                Prim(PrimitiveType.Cube, "Rear Plate", v, new Vector3(0, plateY, zr - 0.012f), Quaternion.identity, new Vector3(1.08f, 0.22f, 0.025f), m.PlateDark);
                UIFactory.WorldText("Rear Badge", v, "unisolutions.in", new Vector3(0, plateY, zr - 0.03f), Quaternion.identity, new Vector2(1.0f, 0.18f), Color.white);
                if (s.chromeBumpers)
                    Prim(PrimitiveType.Cube, "Rear Bumper", v, new Vector3(0, tail.bottom + 0.06f, zr - 0.04f), Quaternion.identity, new Vector3(tail.hw * W * 1.02f, 0.1f, 0.1f), m.Chrome);
                else
                    Prim(PrimitiveType.Cube, "Diffuser", v, new Vector3(0, tail.bottom + 0.02f, zr + 0.1f), Quaternion.identity, new Vector3(tailW * 1.6f, 0.1f, 0.3f), m.Carbon);

                int pipes = s.quadExhaust ? 4 : s.nitro ? 1 : 2;
                for (int i = 0; i < pipes; i++)
                {
                    float x = s.quadExhaust ? (i < 2 ? -1f : 1f) * (0.42f + (i % 2) * 0.13f) : s.nitro ? 0f : (i == 0 ? -0.35f : 0.35f);
                    float r = s.nitro ? 0.16f : 0.09f;
                    Prim(PrimitiveType.Cylinder, "Exhaust", v, new Vector3(x, tail.bottom + 0.07f, zr - 0.02f), Quaternion.Euler(90, 0, 0), new Vector3(r, 0.06f, r), m.Chrome);
                    if (s.nitro)
                    {
                        // Flame cone pointing backwards; NitroExhaustFx scales its local Z.
                        var pivot = new GameObject("Nitro Flame").transform;
                        pivot.SetParent(v, false);
                        pivot.localPosition = new Vector3(x, tail.bottom + 0.07f, zr - 0.06f);
                        pivot.localRotation = Quaternion.LookRotation(Vector3.back);
                        Prim(PrimitiveType.Sphere, "Flame", pivot, new Vector3(0, 0, 0.5f), Quaternion.identity, new Vector3(0.13f, 0.13f, 1f), m.Nitro);
                        flames.Add(pivot);
                    }
                }
            }

            // Mirrors
            {
                var c = Sample(s.cabin, 0.2f);
                float x = c.hw * W * 0.5f + 0.1f;
                for (int side = -1; side <= 1; side += 2)
                {
                    var mirror = Prim(PrimitiveType.Cube, "Mirror", v, new Vector3(side * x, c.bottom + 0.06f, 0.2f * L), Quaternion.identity, new Vector3(0.14f, 0.08f, 0.1f), paint);
                    paintRenderers.Add(mirror); paintSlots.Add(0);
                }
            }

            if (s.sideSkirts)
                for (int side = -1; side <= 1; side += 2)
                    Prim(PrimitiveType.Cube, "Side Skirt", v, new Vector3(side * (HalfW(0f) - 0.02f), 0.2f, 0f), Quaternion.identity, new Vector3(0.08f, 0.12f, s.wheelbase * 0.72f), m.Carbon);

            if (s.sideAccent)
                for (int side = -1; side <= 1; side += 2)
                    Prim(PrimitiveType.Sphere, "Side Accent", v, new Vector3(side * (HalfW(0.02f) - 0.015f), 0.58f, 0.02f * L), Quaternion.identity, new Vector3(0.06f, 0.4f, 1.3f), m.Grille);

            if (s.hoodScoop)
            {
                var hood = Sample(s.body, 0.25f);
                PaintPart(Prim(PrimitiveType.Cube, "Hood Scoop", v, new Vector3(0, hood.top + 0.05f, 0.25f * L), Quaternion.Euler(-3f, 0, 0), new Vector3(0.6f, 0.12f, 0.9f), paint), paintRenderers, paintSlots);
                Prim(PrimitiveType.Cube, "Scoop Intake", v, new Vector3(0, hood.top + 0.07f, 0.25f * L + 0.45f), Quaternion.identity, new Vector3(0.5f, 0.07f, 0.04f), m.Grille);
            }

            if (s.mudFlaps)
                for (int side = -1; side <= 1; side += 2)
                    foreach (float axle in new[] { 0.5f, -0.5f })
                        Prim(PrimitiveType.Cube, "Mud Flap", v, new Vector3(side * s.trackWidth * 0.5f, 0.22f, axle * s.wheelbase + 0.02f * L - s.wheelRadius - 0.08f), Quaternion.identity, new Vector3(0.28f, 0.3f, 0.02f), m.CarTrim);

            var deck = Sample(s.body, -0.44f);
            switch (s.wing)
            {
                case Wing.Ducktail:
                    PaintPart(Prim(PrimitiveType.Cube, "Ducktail", v, new Vector3(0, deck.top + 0.02f, -0.46f * L), Quaternion.Euler(-12f, 0, 0), new Vector3(W * 0.78f, 0.05f, 0.3f), paint), paintRenderers, paintSlots);
                    break;
                case Wing.Lip:
                    PaintPart(Prim(PrimitiveType.Cube, "Lip Spoiler", v, new Vector3(0, deck.top + 0.02f, -0.47f * L), Quaternion.Euler(-15f, 0, 0), new Vector3(W * 0.72f, 0.04f, 0.14f), paint), paintRenderers, paintSlots);
                    break;
                case Wing.Roof:
                {
                    var c = Sample(s.cabin, s.cabin[0].z + 0.05f);
                    PaintPart(Prim(PrimitiveType.Cube, "Roof Spoiler", v, new Vector3(0, c.top + 0.02f, (s.cabin[0].z + 0.04f) * L), Quaternion.Euler(-14f, 0, 0), new Vector3(c.hw * W * 0.95f, 0.05f, 0.26f), paint), paintRenderers, paintSlots);
                    break;
                }
                case Wing.Active:
                    PaintPart(Prim(PrimitiveType.Cube, "Active Wing", v, new Vector3(0, deck.top + 0.12f, -0.42f * L), Quaternion.Euler(-8f, 0, 0), new Vector3(W * 0.78f, 0.04f, 0.36f), paint), paintRenderers, paintSlots);
                    for (int side = -1; side <= 1; side += 2)
                        Prim(PrimitiveType.Cube, "Wing Strut", v, new Vector3(side * 0.35f, deck.top + 0.05f, -0.42f * L), Quaternion.identity, new Vector3(0.04f, 0.14f, 0.1f), m.Carbon);
                    break;
                case Wing.Large:
                    Prim(PrimitiveType.Cube, "Wing Blade", v, new Vector3(0, deck.top + 0.36f, -0.45f * L), Quaternion.Euler(-10f, 0, 0), new Vector3(W * 0.96f, 0.05f, 0.4f), m.Carbon);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Prim(PrimitiveType.Cube, "Wing Strut", v, new Vector3(side * 0.42f, deck.top + 0.17f, -0.44f * L), Quaternion.identity, new Vector3(0.05f, 0.36f, 0.14f), m.Carbon);
                        PaintPart(Prim(PrimitiveType.Cube, "Wing Endplate", v, new Vector3(side * W * 0.485f, deck.top + 0.34f, -0.45f * L), Quaternion.identity, new Vector3(0.03f, 0.24f, 0.46f), paint), paintRenderers, paintSlots);
                    }
                    break;
            }
        }

        static void PaintPart(Renderer r, List<Renderer> renderers, List<int> slots)
        {
            renderers.Add(r);
            slots.Add(0);
        }

        // ------------------------------------------------------------------ Lofting

        public static Station Sample(Station[] st, float z)
        {
            z = Mathf.Clamp(z, st[0].z, st[st.Length - 1].z);
            int i = 0;
            while (i < st.Length - 2 && st[i + 1].z < z) i++;
            Station p0 = st[Mathf.Max(i - 1, 0)], p1 = st[i], p2 = st[i + 1], p3 = st[Mathf.Min(i + 2, st.Length - 1)];
            float t = (z - p1.z) / Mathf.Max(1e-5f, p2.z - p1.z);
            return new Station(z, CR(p0.hw, p1.hw, p2.hw, p3.hw, t), CR(p0.bottom, p1.bottom, p2.bottom, p3.bottom, t), CR(p0.top, p1.top, p2.top, p3.top, t));
        }

        static float CR(float a, float b, float c, float d, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            float v = 0.5f * (2f * b + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (-a + 3f * b - 3f * c + d) * t3);
            return Mathf.Clamp(v, Mathf.Min(b, c) - 0.05f, Mathf.Max(b, c) + 0.05f);
        }

        static float SurfaceY(Station st, float xFraction, float exponent)
        {
            float mid = (st.top + st.bottom) * 0.5f, hh = (st.top - st.bottom) * 0.5f;
            return mid + hh * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Abs(xFraction), exponent)), 1f / exponent);
        }

        static Vector3 RingPoint(Station st, float length, float width, float theta, float nTop, float nBottom)
        {
            float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
            float e = s >= 0f ? nTop : nBottom;
            float x = st.hw * width * 0.5f * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / e);
            float mid = (st.top + st.bottom) * 0.5f, hh = (st.top - st.bottom) * 0.5f;
            float y = mid + hh * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / e);
            return new Vector3(x, y, st.z * length);
        }

        /// <summary>
        /// Lofted shell with two sub-meshes. Body: 0 = paint, 1 = dark lower trim.
        /// Canopy: 0 = glass, 1 = painted roof. Stripe mode: top band only.
        /// </summary>
        static Mesh Loft(string name, Station[] stations, float length, float width, float nTop, float nBottom, bool canopy, bool topOnly = false)
        {
            const int ring = 28;
            const int slices = 30;
            var kit = new MeshKit(2);
            var rings = new Station[slices + 1];
            float z0 = stations[0].z, z1 = stations[stations.Length - 1].z;
            for (int j = 0; j <= slices; j++)
            {
                float t = j / (float)slices;
                float eased = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI);
                rings[j] = Sample(stations, Mathf.Lerp(z0, z1, Mathf.Lerp(t, eased, 0.6f)));
            }
            for (int j = 0; j <= slices; j++)
                for (int k = 0; k < ring; k++)
                    kit.Add(RingPoint(rings[j], length, width, k * Mathf.PI * 2f / ring, nTop, nBottom), Vector2.zero);

            for (int j = 0; j < slices; j++)
                for (int k = 0; k < ring; k++)
                {
                    int a = j * ring + k, b = j * ring + (k + 1) % ring, c = (j + 1) * ring + (k + 1) % ring, d = (j + 1) * ring + k;
                    Vector3 centroid = (kit.Vertices[a] + kit.Vertices[b] + kit.Vertices[c] + kit.Vertices[d]) * 0.25f;
                    float bottom = (rings[j].bottom + rings[j + 1].bottom) * 0.5f;
                    float top = (rings[j].top + rings[j + 1].top) * 0.5f;
                    if (topOnly && centroid.y < top - 0.005f) continue;
                    int sub;
                    if (topOnly) sub = 0;
                    else if (canopy) sub = centroid.y > top - 0.05f && top - bottom > 0.16f ? 1 : 0;
                    else sub = centroid.y < bottom + 0.15f ? 1 : 0;
                    Vector3 outward = new Vector3(centroid.x, centroid.y - (top + bottom) * 0.5f, 0f);
                    kit.Quad(a, b, c, d, outward, sub);
                }

            if (!topOnly)
                for (int end = 0; end < 2; end++)
                {
                    int j = end == 0 ? 0 : slices;
                    Vector3 facing = end == 0 ? Vector3.back : Vector3.forward;
                    Vector3 centre = new Vector3(0f, (rings[j].top + rings[j].bottom) * 0.5f, rings[j].z * length);
                    int ci = kit.Add(centre, Vector2.zero);
                    int first = kit.Vertices.Count;
                    for (int k = 0; k < ring; k++) kit.Add(kit.Vertices[j * ring + k], Vector2.zero);
                    for (int k = 0; k < ring; k++) kit.Tri(ci, first + k, first + (k + 1) % ring, facing, 0);
                }
            return kit.Build(name);
        }

        // ------------------------------------------------------------------ Helpers

        static MeshRenderer MeshObject(string name, Transform parent, Mesh mesh, params Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            return r;
        }

        public static MeshRenderer Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return r;
        }
    }
}
