using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RacingPrototype.EditorTools
{
    /// <summary>
    /// Builds the whole thegameio racing sample: URP pipeline, brand config, textures, materials,
    /// cars, props, the main-menu scene and the 4 route scenes, plus the car / route catalogs and
    /// build settings. Menu: Racing Prototype. Batch:
    /// -executeMethod RacingPrototype.EditorTools.ProjectBuilder.BuildAllBatch (no -quit).
    /// </summary>
    public static class ProjectBuilder
    {
        public const string RouteDataFolder = "Assets/Data/Routes";
        public const string MenuScenePath = "Assets/Scenes/MainMenu.unity";

        public class Shared
        {
            public CityMaterials Materials;
            public PropKit Props;
            public CarCatalog Catalog;
            public ProceduralAssets.SoundSet Sounds;
            public VolumeProfile NightProfile, MenuProfile;
            public GameObject ParkedCar;
        }

        [MenuItem("Racing Prototype/Rebuild Everything (menu + 4 routes)", priority = 0)]
        public static void BuildAll()
        {
            RemoveSupersededContent();
            var shared = BuildShared();
            var entries = new List<RouteCatalog.Entry>();
            foreach (var def in RouteDefinitions())
                entries.Add(new RouteBuilder(def, shared).Build());
            SaveRouteCatalog(entries);
            BuildMenuScene(shared);
            SetBuildSettings(entries);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Racing Prototype/Rebuild Selected Route Definition", priority = 1)]
        public static void BuildSelected()
        {
            if (!(Selection.activeObject is TrackDefinition def))
            {
                EditorUtility.DisplayDialog("Racing Prototype", "Select a Track Definition in Assets/Data/Routes first.", "OK");
                return;
            }
            var shared = BuildShared();
            var entry = new RouteBuilder(def, shared).Build();
            var catalog = RouteCatalog.Current;
            if (catalog != null && catalog.routes != null)
            {
                for (int i = 0; i < catalog.routes.Length; i++) if (catalog.routes[i].number == entry.number) catalog.routes[i] = entry;
                EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();
        }

        public static void BuildAllBatch()
        {
            try
            {
                BuildAll();
                Debug.Log("[ProjectBuilder] Build finished.");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        // ================================================================== Shared assets

        static readonly string[] Folders =
        {
            "Assets/Scenes", "Assets/Prefabs/Cars", "Assets/Prefabs/Environment", "Assets/Materials/Textures", "Assets/Materials/Buildings",
            "Assets/Models/Cars", "Assets/Models/Environment", "Assets/Resources/UI/Minimaps", "Assets/Audio", RouteDataFolder, "Assets/Settings/URP"
        };

        static Shared BuildShared()
        {
            foreach (var f in Folders) ProceduralAssets.EnsureFolder(f);
            UrpSetup.EnsurePipeline();
            EnsureBrandConfig();

            var shared = new Shared();
            var m = shared.Materials = BuildMaterials();
            shared.Sounds = ProceduralAssets.Sounds();
            ProceduralAssets.RoundedPanel();
            ProceduralAssets.Circle();
            ProceduralAssets.CarCategoryIcons();
            shared.NightProfile = UrpSetup.NightProfile("Night_PostProcess", 0.55f, 0.85f);
            // Garage: depth of field focused on the turntable car (11.6 m from the showroom camera).
            shared.MenuProfile = UrpSetup.NightProfile("Menu_PostProcess", 0.35f, 0.7f, 11.6f);
            shared.Props = PropKit.Build(m);
            shared.Catalog = BuildCars(shared);
            shared.ParkedCar = BuildParkedCar(m);
            AssetDatabase.SaveAssets();
            return shared;
        }

        static void EnsureBrandConfig()
        {
            ProceduralAssets.EnsureFolder("Assets/Resources");
            const string path = "Assets/Resources/BrandConfig.asset";
            if (AssetDatabase.LoadAssetAtPath<BrandConfig>(path) == null)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<BrandConfig>(), path);
        }

        static CityMaterials BuildMaterials()
        {
            var m = new CityMaterials();
            ProceduralAssets.MatSpec Spec(Color c, float smooth = 0.3f, float metal = 0f) => new ProceduralAssets.MatSpec { colour = c, smoothness = smooth, metallic = metal };
            Material Mat(string n, ProceduralAssets.MatSpec s, string folder = ProceduralAssets.MaterialFolder) => ProceduralAssets.Mat(n, s, folder);

            var asphalt = ProceduralAssets.Asphalt();
            var asphaltN = ProceduralAssets.AsphaltNormal();
            var paving = ProceduralAssets.Paving("Paving", new Color(0.62f, 0.6f, 0.56f));
            var plaza = ProceduralAssets.Paving("PlazaPaving", new Color(0.7f, 0.62f, 0.52f));
            var pavingN = ProceduralAssets.PavingNormal();
            var concrete = ProceduralAssets.Concrete();
            var corrugated = ProceduralAssets.Corrugated();
            var corrugatedN = ProceduralAssets.CorrugatedNormal();
            var rock = ProceduralAssets.Rock();
            var rockN = ProceduralAssets.RockNormal();

            m.Asphalt = Mat("Track_Asphalt", new ProceduralAssets.MatSpec { colour = Color.white, smoothness = 0.22f, albedo = asphalt, normal = asphaltN, normalScale = 0.6f });
            m.LineWhite = Mat("Track_LineWhite", Spec(new Color(0.9f, 0.9f, 0.88f), 0.35f));
            m.LineYellow = Mat("Track_LineYellow", Spec(new Color(0.95f, 0.72f, 0.12f), 0.35f));
            m.Kerb = Mat("Track_Kerb", new ProceduralAssets.MatSpec { albedo = ProceduralAssets.Kerb(), smoothness = 0.3f });
            m.Paving = Mat("Track_Paving", new ProceduralAssets.MatSpec { albedo = paving, normal = pavingN, smoothness = 0.15f });
            m.Plaza = Mat("Env_Plaza", new ProceduralAssets.MatSpec { albedo = plaza, normal = pavingN, smoothness = 0.2f });
            m.Concrete = Mat("Env_Concrete", new ProceduralAssets.MatSpec { albedo = concrete, smoothness = 0.15f, tiling = new Vector2(4, 4) });
            m.KerbStone = Mat("Track_KerbStone", new ProceduralAssets.MatSpec { colour = new Color(0.72f, 0.71f, 0.68f), albedo = concrete, smoothness = 0.15f });
            m.Gutter = Mat("Track_Gutter", new ProceduralAssets.MatSpec { colour = new Color(0.55f, 0.55f, 0.55f), albedo = concrete, smoothness = 0.2f });
            m.Drain = Mat("Track_Drain", Spec(new Color(0.06f, 0.06f, 0.065f), 0.4f, 0.7f));
            m.BarrierYellow = Mat("Track_BarrierYellow", new ProceduralAssets.MatSpec { albedo = ProceduralAssets.BarrierPaint("BarrierYellow", new Color(0.92f, 0.72f, 0.1f), new Color(0.1f, 0.1f, 0.1f)), smoothness = 0.25f });
            m.BarrierRed = Mat("Track_BarrierRed", new ProceduralAssets.MatSpec { albedo = ProceduralAssets.BarrierPaint("BarrierRed", new Color(0.75f, 0.1f, 0.08f), new Color(0.9f, 0.9f, 0.88f)), smoothness = 0.25f });
            m.Guardrail = Mat("Track_Guardrail", new ProceduralAssets.MatSpec { colour = new Color(0.72f, 0.74f, 0.78f), smoothness = 0.6f, metallic = 0.8f, doubleSided = true });
            m.Checker = Mat("Track_Checker", new ProceduralAssets.MatSpec { albedo = ProceduralAssets.Checker(), smoothness = 0.2f });
            m.CheckpointLine = Mat("Track_CheckpointLine", Spec(new Color(1f, 0.71f, 0f), 0.35f));
            m.GateBanner = ProceduralAssets.Mat("Env_GateBanner", new Color(0.12f, 0.13f, 0.16f), 0.4f, 0f, Color.black);
            m.GatePost = Mat("Env_GatePost", Spec(new Color(0.8f, 0.8f, 0.82f), 0.4f, 0.4f));
            m.Truss = Mat("Env_Truss", Spec(new Color(0.18f, 0.19f, 0.22f), 0.45f, 0.6f));

            foreach (var style in CarModelBuilder.Styles())
                m.Paint[style.id] = Mat("Car_Paint_" + style.id, Spec(style.colour, 0.86f, 0.5f));
            m.Paint["Parked"] = Mat("Car_Paint_Parked", Spec(new Color(0.55f, 0.56f, 0.58f), 0.7f, 0.4f));
            m.CarTrim = Mat("Car_Trim", Spec(new Color(0.05f, 0.05f, 0.06f), 0.45f));
            m.Glass = Mat("Car_Glass", Spec(new Color(0.04f, 0.05f, 0.07f), 0.95f, 0.5f));
            m.Tyre = Mat("Car_Tyre", Spec(new Color(0.04f, 0.04f, 0.045f), 0.12f));
            m.Rim = Mat("Car_Rim", Spec(new Color(0.78f, 0.79f, 0.82f), 0.6f, 0.6f));
            m.Chrome = Mat("Car_Chrome", Spec(new Color(0.9f, 0.9f, 0.92f), 0.9f, 1f));
            m.HeadLight = ProceduralAssets.Mat("Car_HeadLight", new Color(1f, 1f, 0.96f), 0.9f, 0f, new Color(2.6f, 2.6f, 2.5f));
            m.TailLight = ProceduralAssets.Mat("Car_TailLight", new Color(0.9f, 0.04f, 0.03f), 0.8f, 0f, new Color(3.2f, 0.1f, 0.06f));
            m.PlateDark = Mat("Car_PlateDark", Spec(new Color(0.04f, 0.04f, 0.05f), 0.3f));
            m.PlateLight = Mat("Car_PlateLight", Spec(new Color(0.95f, 0.95f, 0.93f), 0.3f));
            m.Grille = Mat("Car_Grille", Spec(new Color(0.03f, 0.03f, 0.035f), 0.5f));
            m.Carbon = Mat("Car_Carbon", Spec(new Color(0.07f, 0.07f, 0.08f), 0.7f, 0.3f));
            m.Nitro = ProceduralAssets.Mat("Car_NitroFlame", new Color(0.3f, 0.55f, 1f), 0.5f, 0f, new Color(1.2f, 2.6f, 6f));
            m.StripeWhite = Mat("Car_StripeWhite", Spec(new Color(0.93f, 0.93f, 0.92f), 0.8f, 0.2f));
            m.StripeBlack = Mat("Car_StripeBlack", Spec(new Color(0.05f, 0.05f, 0.06f), 0.8f, 0.2f));

            // Building facades: neutral atlases tinted by plaster colour (cream, sand, terracotta, ...).
            const string bf = "Assets/Materials/Buildings";
            var plaster = ProceduralAssets.Facade(ProceduralAssets.FacadeKind.Plaster, out var plasterE, out var plasterM);
            var apartment = ProceduralAssets.Facade(ProceduralAssets.FacadeKind.Apartment, out var apartmentE, out var apartmentM);
            var office = ProceduralAssets.Facade(ProceduralAssets.FacadeKind.Office, out var officeE, out var officeM);
            var tower = ProceduralAssets.Facade(ProceduralAssets.FacadeKind.Tower, out var towerE, out var towerM);
            var store = ProceduralAssets.Storefront(out var storeE);
            Color[] plasterTints = { new Color(0.93f, 0.88f, 0.78f), new Color(0.88f, 0.76f, 0.6f), new Color(0.82f, 0.58f, 0.45f), new Color(0.95f, 0.92f, 0.86f),
                                     new Color(0.9f, 0.84f, 0.62f), new Color(0.72f, 0.78f, 0.72f), new Color(0.78f, 0.8f, 0.84f) };
            Material Facade(string n, Texture alb, Texture em, Color tint, float emissive) => Mat(n, new ProceduralAssets.MatSpec
                { colour = tint, albedo = alb, emissionMap = em, emission = Color.white * emissive, smoothness = 0.18f }, bf);
            m.Plaster = plasterTints.Select((c, i) => Facade("Facade_Plaster_" + i, plaster, plasterE, c, 1.0f)).ToArray();
            m.Apartment = plasterTints.Take(4).Select((c, i) => Facade("Facade_Apartment_" + i, apartment, apartmentE, c, 1.0f)).ToArray();
            m.Office = new[] { Color.white, new Color(0.85f, 0.9f, 0.95f), new Color(0.92f, 0.9f, 0.86f) }.Select((c, i) => Facade("Facade_Office_" + i, office, officeE, c, 0.75f)).ToArray();
            m.Tower = new[] { Color.white, new Color(0.8f, 0.86f, 0.95f), new Color(0.9f, 0.88f, 0.82f) }.Select((c, i) => Facade("Facade_Tower_" + i, tower, towerE, c, 0.9f)).ToArray();
            m.Storefront = Mat("Facade_Storefront", new ProceduralAssets.MatSpec { albedo = store, emissionMap = storeE, emission = Color.white * 0.8f, smoothness = 0.45f }, bf);
            m.Roof = Mat("Building_Roof", new ProceduralAssets.MatSpec { colour = new Color(0.34f, 0.34f, 0.35f), albedo = concrete, smoothness = 0.1f }, bf);
            m.BuildingTrim = Mat("Building_Trim", new ProceduralAssets.MatSpec { colour = new Color(0.8f, 0.78f, 0.74f), albedo = concrete, smoothness = 0.15f }, bf);
            m.Door = Mat("Building_Door", Spec(new Color(0.25f, 0.17f, 0.11f), 0.3f), bf);
            m.Gate = Mat("Building_Gate", Spec(new Color(0.1f, 0.11f, 0.12f), 0.5f, 0.7f), bf);
            m.RoofMetal = new[] { new Color(0.45f, 0.18f, 0.12f), new Color(0.5f, 0.52f, 0.55f), new Color(0.2f, 0.32f, 0.36f) }
                .Select((c, i) => Mat("Building_RoofMetal_" + i, new ProceduralAssets.MatSpec { colour = c, albedo = corrugated, normal = corrugatedN, smoothness = 0.35f, metallic = 0.4f, doubleSided = true }, bf)).ToArray();
            var awning = ProceduralAssets.Awning();
            m.Awning = new[] { new Color(0.8f, 0.15f, 0.12f), new Color(0.12f, 0.45f, 0.75f), new Color(0.95f, 0.65f, 0.1f), new Color(0.1f, 0.55f, 0.3f), new Color(0.55f, 0.2f, 0.55f) }
                .Select((c, i) => Mat("Building_Awning_" + i, new ProceduralAssets.MatSpec { colour = c, albedo = awning, smoothness = 0.1f, doubleSided = true }, bf)).ToArray();
            m.Sign = new[] { new Color(0.85f, 0.12f, 0.1f), new Color(0.1f, 0.35f, 0.75f), new Color(0.1f, 0.5f, 0.3f), new Color(0.9f, 0.55f, 0.05f), new Color(0.25f, 0.25f, 0.3f) }
                .Select((c, i) => ProceduralAssets.Mat("Building_Sign_" + i, new ProceduralAssets.MatSpec { colour = c, emission = c * 0.9f, smoothness = 0.5f }, bf)).ToArray();
            m.Warehouse = Mat("Building_Warehouse", new ProceduralAssets.MatSpec { colour = new Color(0.62f, 0.64f, 0.66f), albedo = corrugated, normal = corrugatedN, normalScale = 0.35f, smoothness = 0.25f, metallic = 0.2f }, bf);
            m.WarehouseDoor = Mat("Building_WarehouseDoor", new ProceduralAssets.MatSpec { colour = new Color(0.35f, 0.38f, 0.42f), albedo = corrugated, smoothness = 0.3f, metallic = 0.4f }, bf);

            m.Pole = Mat("Env_Pole", Spec(new Color(0.22f, 0.23f, 0.25f), 0.45f, 0.7f));
            m.LampGlow = ProceduralAssets.Mat("Env_LampGlow", new Color(1f, 0.85f, 0.6f), 0.5f, 0f, new Color(4.5f, 3.4f, 2.2f));
            m.PalmTrunk = Mat("Env_PalmTrunk", Spec(new Color(0.36f, 0.29f, 0.21f), 0.15f));
            m.PalmLeaf = Mat("Env_PalmLeaf", new ProceduralAssets.MatSpec { colour = new Color(0.2f, 0.42f, 0.18f), smoothness = 0.3f, doubleSided = true });
            m.Leaves = Mat("Env_Leaves", Spec(new Color(0.13f, 0.28f, 0.12f), 0.15f));
            m.Trunk = Mat("Env_Trunk", Spec(new Color(0.3f, 0.21f, 0.14f), 0.1f));
            m.Wood = Mat("Env_Wood", Spec(new Color(0.42f, 0.29f, 0.18f), 0.3f));
            m.Bin = Mat("Env_Bin", Spec(new Color(0.12f, 0.3f, 0.2f), 0.35f, 0.3f));
            m.Stone = Mat("Env_Stone", new ProceduralAssets.MatSpec { colour = new Color(0.78f, 0.74f, 0.66f), albedo = concrete, smoothness = 0.2f });
            m.Monument = Mat("Env_Monument", new ProceduralAssets.MatSpec { colour = new Color(0.86f, 0.82f, 0.74f), albedo = concrete, smoothness = 0.3f });
            m.Rail = Mat("Env_Rail", Spec(new Color(0.7f, 0.72f, 0.75f), 0.6f, 0.8f));
            m.Container = new[] { new Color(0.6f, 0.15f, 0.1f), new Color(0.12f, 0.3f, 0.6f), new Color(0.15f, 0.45f, 0.25f), new Color(0.85f, 0.85f, 0.82f), new Color(0.85f, 0.45f, 0.1f), new Color(0.4f, 0.42f, 0.45f) }
                .Select((c, i) => Mat("Env_Container_" + i, new ProceduralAssets.MatSpec { colour = c, albedo = corrugated, normal = corrugatedN, smoothness = 0.3f, metallic = 0.3f })).ToArray();
            m.CraneRed = Mat("Env_CraneRed", Spec(new Color(0.75f, 0.12f, 0.08f), 0.4f, 0.4f));
            m.CraneWhite = Mat("Env_CraneWhite", Spec(new Color(0.88f, 0.88f, 0.86f), 0.4f, 0.4f));
            m.ShipHull = Mat("Env_ShipHull", Spec(new Color(0.12f, 0.14f, 0.2f), 0.4f, 0.3f));
            m.ShipDeck = ProceduralAssets.Mat("Env_ShipDeck", new ProceduralAssets.MatSpec { colour = new Color(0.9f, 0.9f, 0.88f), albedo = office, smoothness = 0.3f, emissionMap = officeE, emission = Color.white });
            m.Water = Mat("Env_Water", new ProceduralAssets.MatSpec { colour = Color.white, albedo = ProceduralAssets.Water(), normal = ProceduralAssets.WaterNormal(), normalScale = 0.8f, smoothness = 0.62f, metallic = 0f });
            m.Rock = Mat("Env_Rock", new ProceduralAssets.MatSpec { albedo = rock, normal = rockN, smoothness = 0.12f });
            m.Grass = Mat("Env_Grass", new ProceduralAssets.MatSpec { albedo = ProceduralAssets.Grass(), smoothness = 0.05f });
            m.Ground = Mat("Env_UrbanGround", new ProceduralAssets.MatSpec { colour = new Color(0.42f, 0.41f, 0.4f), albedo = concrete, smoothness = 0.1f });
            m.Spectator = Mat("Env_Spectator", new ProceduralAssets.MatSpec { albedo = ProceduralAssets.PaletteTexture(), smoothness = 0.2f });
            m.BillboardFace = ProceduralAssets.Mat("Env_BillboardFace", new Color(0.08f, 0.09f, 0.12f), 0.4f, 0f, new Color(0.05f, 0.055f, 0.07f));
            m.BillboardFrame = Mat("Env_BillboardFrame", Spec(new Color(0.15f, 0.16f, 0.18f), 0.4f, 0.5f));
            m.Chevron = Mat("Env_Chevron", new ProceduralAssets.MatSpec { albedo = ProceduralAssets.Chevron(), smoothness = 0.3f, emissionMap = ProceduralAssets.Chevron(), emission = Color.white * 0.25f });
            m.Sky = ProceduralAssets.NightSky();
            ApplyRealSurfaces(m, plasterM, apartmentM, officeM, towerM);
            return m;
        }

        /// <summary>
        /// Swaps in the scanned Poly Haven surfaces and night sky where they are present in
        /// Assets/ThirdParty/PolyHaven (the generated versions stay as fallbacks). Road, sidewalks,
        /// plaza and ground take the real materials outright; facades, barriers and roofs keep their
        /// generated layout (windows, paint stripes, tints) with the real surface layered on as detail.
        /// UV scales: road 8 m, sidewalk / plaza 2 m, ground 6 m, facades 12.8 m per UV unit.
        /// </summary>
        static void ApplyRealSurfaces(CityMaterials m, Texture2D plasterMask, Texture2D apartmentMask, Texture2D officeMask, Texture2D towerMask)
        {
            Material Or(Material real, Material fallback) => real != null ? real : fallback;
            // Night after rain: damp asphalt with glossy puddle patches mirrors the street lights, neon and headlights.
            m.Asphalt = Or(PolyHaven.SurfaceMaterial("PH_Road_Asphalt", "clean_asphalt", new Color(0.95f, 0.95f, 0.97f), 8f, 1f, 1f, false, 0.75f), m.Asphalt);
            m.Paving = Or(PolyHaven.SurfaceMaterial("PH_Sidewalk", "concrete_pavement_03", new Color(0.86f, 0.85f, 0.82f), 2f), m.Paving);
            m.KerbStone = Or(PolyHaven.SurfaceMaterial("PH_KerbStone", "rough_concrete", new Color(0.82f, 0.81f, 0.78f), 2f), m.KerbStone);
            m.Plaza = Or(PolyHaven.SurfaceMaterial("PH_Plaza", "patterned_concrete_pavers", new Color(0.95f, 0.9f, 0.84f), 2f), m.Plaza);
            m.Ground = Or(PolyHaven.SurfaceMaterial("PH_UrbanGround", "rough_concrete", new Color(0.52f, 0.51f, 0.49f), 6f), m.Ground);
            m.Gutter = Or(PolyHaven.SurfaceMaterial("PH_Gutter", "rough_concrete", new Color(0.6f, 0.6f, 0.6f), 2f), m.Gutter);
            m.Concrete = Or(PolyHaven.SurfaceMaterial("PH_Concrete", "rough_concrete", new Color(0.76f, 0.75f, 0.73f), 3f), m.Concrete);

            PolyHaven.AddDetail(m.BarrierRed, "rough_concrete", 4f, null, 1f);
            PolyHaven.AddDetail(m.BarrierYellow, "rough_concrete", 4f, null, 1f);
            foreach (var f in m.Plaster) PolyHaven.AddDetail(f, "beige_wall_001", 12.8f, plasterMask);
            for (int i = 0; i < m.Apartment.Length; i++) PolyHaven.AddDetail(m.Apartment[i], i % 2 == 0 ? "white_stucco" : "painted_brick", 12.8f, apartmentMask);
            foreach (var f in m.Office) PolyHaven.AddDetail(f, "concrete_tile_facade", 12.8f, officeMask, 0.7f);
            foreach (var f in m.Tower) PolyHaven.AddDetail(f, "concrete_tile_facade", 12.8f, towerMask, 0.6f);
            PolyHaven.AddDetail(m.Roof, "rough_concrete", 4f);
            foreach (var r in m.RoofMetal) PolyHaven.AddDetail(r, "corrugated_iron", 4f, null, 1f);
            m.Warehouse = Or(PolyHaven.SurfaceMaterial("PH_Warehouse", "factory_wall", new Color(0.78f, 0.8f, 0.82f), 12.8f), m.Warehouse);
            // The pure-sky HDRI is a light-polluted night; scaled down and cooled so it reads as a dark city sky.
            m.Sky = Or(PolyHaven.NightSky(0.09f, new Color(0.4f, 0.45f, 0.6f)), m.Sky);
        }

        static CarCatalog BuildCars(Shared shared)
        {
            const string path = "Assets/Resources/CarCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<CarCatalog>(path);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CarCatalog>();
                AssetDatabase.CreateAsset(catalog, path);
            }
            var entries = new List<CarCatalog.Entry>();
            int number = 7;
            foreach (var style in CarModelBuilder.Styles())
            {
                var prefab = CarModelBuilder.BuildPrefab(style, shared.Materials, shared.Sounds.Engine, "Assets/Models/Cars", "Assets/Prefabs/Cars", number);
                number += 11;
                var c = prefab.GetComponent<CarController>();
                float handling = CarStats.HandlingIndex(c.LowSpeedSteerRate, c.HighSpeedSteerRate, c.LateralGrip);
                entries.Add(new CarCatalog.Entry
                {
                    id = style.id, category = style.category, modelName = style.modelName, description = style.description,
                    prefab = prefab, defaultColour = style.colour, icon = style.icon,
                    topSpeedKmh = c.MaxSpeed * CarStats.KmhPerMs, zeroTo100 = CarStats.ZeroTo100(c.Acceleration, c.MaxSpeed), handlingIndex = handling,
                    speedBar = CarStats.SpeedBar(c), accelBar = CarStats.AccelBar(c), handlingBar = CarStats.HandlingBar(c)
                });
            }
            catalog.cars = entries.ToArray();
            RemoveRetiredCars(entries.Select(e => e.id).ToArray());
            // Brand amber first, then the palette.
            catalog.paintPalette = new[]
            {
                new Color(1f, 0.71f, 0f), new Color(0.82f, 0.05f, 0.05f), new Color(0.12f, 0.42f, 0.78f), new Color(0.07f, 0.45f, 0.26f), new Color(0.95f, 0.45f, 0.08f),
                new Color(0.45f, 0.18f, 0.7f), new Color(0.08f, 0.62f, 0.72f), new Color(0.78f, 0.8f, 0.84f), new Color(0.94f, 0.94f, 0.95f), new Color(0.06f, 0.06f, 0.07f)
            };
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        /// <summary>Deletes generated prefabs, meshes and paint of cars no longer in the line-up.</summary>
        static void RemoveRetiredCars(string[] ids)
        {
            foreach (var folder in new[] { "Assets/Prefabs/Cars", "Assets/Models/Cars" })
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (var guid in AssetDatabase.FindAssets("", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    string id = name.Split('_')[0];
                    if (!ids.Contains(id)) AssetDatabase.DeleteAsset(path);
                }
            }
            foreach (var guid in AssetDatabase.FindAssets("Car_Paint_", new[] { ProceduralAssets.MaterialFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string id = System.IO.Path.GetFileNameWithoutExtension(path).Substring("Car_Paint_".Length);
                if (id != "Parked" && !ids.Contains(id)) AssetDatabase.DeleteAsset(path);
            }
        }

        static GameObject BuildParkedCar(CityMaterials m)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cars/Regular.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "ParkedCar";
            foreach (var c in go.GetComponents<Component>())
                if (!(c is Transform)) Object.DestroyImmediate(c);
            foreach (var l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l.gameObject);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == m.Paint["Regular"]) mats[i] = m.Paint["Parked"];
                    if (mats[i] == m.HeadLight || mats[i] == m.TailLight) mats[i] = m.CarTrim;
                }
                r.sharedMaterials = mats;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefabs/Environment/ParkedCar.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ================================================================== Routes

        static TrackDefinition Route(string file, Action<TrackDefinition> configure)
        {
            string path = $"{RouteDataFolder}/{file}.asset";
            var def = AssetDatabase.LoadAssetAtPath<TrackDefinition>(path);
            bool isNew = def == null;
            if (isNew) def = ScriptableObject.CreateInstance<TrackDefinition>();
            configure(def);
            if (isNew) AssetDatabase.CreateAsset(def, path);
            else EditorUtility.SetDirty(def);
            return def;
        }

        static Vector2[] V(params float[] xz)
        {
            var v = new Vector2[xz.Length / 2];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector2(xz[i * 2], xz[i * 2 + 1]);
            return v;
        }

        static TrackDefinition.WidthKey[] W(params float[] fw)
        {
            var k = new TrackDefinition.WidthKey[fw.Length / 2];
            for (int i = 0; i < k.Length; i++) k[i] = new TrackDefinition.WidthKey(fw[i * 2], fw[i * 2 + 1]);
            return k;
        }

        /// <summary>The 4 approved routes (PPT slides 2, 5, 14). Layouts were checked for corner radius and clearance.</summary>
        public static TrackDefinition[] RouteDefinitions()
        {
            var r1 = Route("Route01_DowntownCircuit", d =>
            {
                d.routeNumber = 1; d.routeName = "Downtown Circuit"; d.sceneName = "Route01_DowntownCircuit"; d.theme = RouteTheme.CityLoop;
                d.description = "Clockwise city loop from the concept map: wide downtown avenue, commercial junction, tight market corner, residential street and civic square.";
                d.controlPoints = V(-100, 136, 0, 136, 84, 136, 150, 118, 185, 96, 232, 71, 232, 0, 232, -68, 170, -100, 102, -136, 0, -136, -110, -136, -205, -136, -220, -85, -232, -31, -232, 60, -232, 136, -170, 136);
                d.smoothingIterations = 220;
                d.widthKeys = W(0f, 18f, 0.12f, 18f, 0.2f, 14f, 0.31f, 13f, 0.47f, 13f, 0.52f, 14f, 0.86f, 14f, 0.93f, 18f);
                d.sectors = new[]
                {
                    new TrackDefinition.Sector("Downtown avenue", District.DowntownAvenue, 0.9f, 0.16f),
                    new TrackDefinition.Sector("Commercial junction", District.CommercialJunction, 0.16f, 0.3f),
                    new TrackDefinition.Sector("Market corner", District.MarketStreet, 0.3f, 0.48f),
                    new TrackDefinition.Sector("Residential street", District.ResidentialStreet, 0.48f, 0.72f),
                    new TrackDefinition.Sector("Civic square", District.CivicSquare, 0.72f, 0.9f),
                };
                d.junctions = new[] { 0.23f, 0.29f, 0.53f, 0.66f, 0.85f };
                d.sidewalkWidth = 4.5f; d.environmentSeed = 101;
            });
            var r2 = Route("Route02_WaterfrontRun", d =>
            {
                d.routeNumber = 2; d.routeName = "Waterfront Run"; d.sceneName = "Route02_WaterfrontRun"; d.theme = RouteTheme.Waterfront;
                d.description = "Anticlockwise coastal boulevard with sweeping bends along the sea, returning through the hotel district.";
                d.controlPoints = V(-200, -70, -80, -55, 40, -90, 160, -70, 260, -100, 340, -60, 355, 20, 300, 70, 190, 60, 80, 95, -30, 70, -150, 100, -260, 80, -330, 30, -330, -40);
                d.smoothingIterations = 160;
                d.widthKeys = W(0f, 16f, 0.1f, 16f, 0.18f, 15f, 0.85f, 15f, 0.95f, 16f);
                d.sectors = new[]
                {
                    new TrackDefinition.Sector("Waterfront promenade", District.WaterfrontPromenade, 0.9f, 0.48f),
                    new TrackDefinition.Sector("Hotel boulevard", District.HotelBoulevard, 0.48f, 0.9f),
                };
                d.junctions = new[] { 0.57f, 0.69f, 0.83f };
                d.sidewalkWidth = 5f; d.environmentSeed = 202;
            });
            var r3 = Route("Route03_IndustrialPort", d =>
            {
                d.routeNumber = 3; d.routeName = "Industrial Port"; d.sceneName = "Route03_IndustrialPort"; d.theme = RouteTheme.IndustrialPort;
                d.description = "Clockwise run along the quay and through the container yards: broad roads and wide drift corners.";
                d.controlPoints = V(-180, 140, -40, 140, 100, 140, 220, 135, 290, 85, 300, 0, 280, -70, 210, -125, 110, -140, 30, -105, -50, -140, -150, -140, -250, -115, -300, -40, -290, 60, -250, 120);
                d.smoothingIterations = 140;
                d.widthKeys = W(0f, 20f, 0.15f, 20f, 0.22f, 18f, 0.85f, 18f, 0.93f, 20f);
                d.sectors = new[]
                {
                    new TrackDefinition.Sector("Port quay", District.PortQuay, 0.9f, 0.24f),
                    new TrackDefinition.Sector("Container yard", District.ContainerYard, 0.24f, 0.52f),
                    new TrackDefinition.Sector("Warehouses", District.Warehouses, 0.52f, 0.9f),
                };
                d.junctions = new[] { 0.63f, 0.74f };
                d.sidewalkWidth = 4f; d.environmentSeed = 303;
            });
            var r4 = Route("Route04_HillsideExpress", d =>
            {
                d.routeNumber = 4; d.routeName = "Hillside Express"; d.sceneName = "Route04_HillsideExpress"; d.theme = RouteTheme.Hillside;
                d.description = "Anticlockwise climb on an elevated plateau above the city: two hairpin switchbacks and a fast ridge road with the sea below.";
                d.controlPoints = HillsidePoints();
                d.smoothingIterations = 20;
                d.widthKeys = W(0f, 13f, 0.12f, 13f, 0.18f, 12f, 0.92f, 12f, 0.97f, 13f);
                d.sectors = new[]
                {
                    new TrackDefinition.Sector("Switchbacks", District.HillsideSwitchbacks, 0f, 0.36f),
                    new TrackDefinition.Sector("Ridge road", District.HillsideRidge, 0.36f, 1f),
                };
                d.junctions = new float[0];
                d.sidewalkWidth = 3f; d.environmentSeed = 404;
            });
            return new[] { r1, r2, r3, r4 };
        }

        /// <summary>Hillside layout from explicit hairpin arcs (radius 30 m) so smoothing keeps them.</summary>
        static Vector2[] HillsidePoints()
        {
            var pts = new List<Vector2>();
            void Seg(Vector2 a, Vector2 b, int n) { for (int i = 0; i < n; i++) pts.Add(Vector2.Lerp(a, b, i / (float)n)); }
            void Arc(float cx, float cz, float r, float a0, float a1)
            {
                int n = Mathf.Max(2, (int)(Mathf.Abs(a1 - a0) / 14f));
                for (int i = 1; i < n; i++)
                {
                    float a = Mathf.Lerp(a0, a1, i / (float)n) * Mathf.Deg2Rad;
                    pts.Add(new Vector2(cx + r * Mathf.Cos(a), cz + r * Mathf.Sin(a)));
                }
            }
            const float R = 30f;
            Seg(new Vector2(-110, -150), new Vector2(110, -150), 4);
            pts.Add(new Vector2(110, -150)); Arc(110, -150 + R, R, -90, 90);
            Seg(new Vector2(110, -150 + 2 * R), new Vector2(-50, -150 + 2 * R), 3);
            pts.Add(new Vector2(-50, -90)); Arc(-50, -90 + R, R, 270, 90);
            Seg(new Vector2(-50, -30), new Vector2(140, -30), 3);
            pts.AddRange(V(140, -30, 195, 5, 212, 70, 185, 130, 120, 165, 40, 160, -30, 178, -110, 170, -180, 125, -210, 50, -205, -40, -175, -115));
            return pts.ToArray();
        }

        static void SaveRouteCatalog(List<RouteCatalog.Entry> entries)
        {
            const string path = "Assets/Resources/RouteCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<RouteCatalog>(path);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<RouteCatalog>();
                AssetDatabase.CreateAsset(catalog, path);
            }
            catalog.routes = entries.OrderBy(e => e.number).ToArray();
            EditorUtility.SetDirty(catalog);
        }

        // ================================================================== Menu scene

        static void BuildMenuScene(Shared shared)
        {
            var m = shared.Materials;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = m.Sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.1f, 0.11f, 0.15f);
            RenderSettings.ambientEquatorColor = new Color(0.07f, 0.07f, 0.09f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.035f);
            RenderSettings.fog = false;
            Lightmapping.lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>("Assets/Settings/NightLighting.lighting");

            var showroom = new GameObject("Garage Showroom").transform;
            var kit = new MeshKit(4);
            kit.Box(new Vector3(0, -0.05f, 4f), new Vector3(40f, 0.1f, 30f), Quaternion.identity, 0, 4f);           // floor
            kit.Box(new Vector3(0, 6f, 10f), new Vector3(40f, 12f, 0.4f), Quaternion.identity, 1, 6f);            // back wall
            kit.Box(new Vector3(-12f, 6f, 2f), new Vector3(0.4f, 12f, 20f), Quaternion.Euler(0, -12f, 0), 1, 6f);
            kit.Box(new Vector3(12f, 6f, 2f), new Vector3(0.4f, 12f, 20f), Quaternion.Euler(0, 12f, 0), 1, 6f);
            kit.Box(new Vector3(0, 0.7f, 9.7f), new Vector3(40f, 0.08f, 0.1f), Quaternion.identity, 2);           // red accent strip
            kit.Box(new Vector3(0, 11.2f, 9.7f), new Vector3(40f, 0.08f, 0.1f), Quaternion.identity, 3);          // amber strip
            var floorMats = new[]
            {
                // Polished dark showroom floor: mirrors the softboxes and the car through the box-projected probe.
                ProceduralAssets.Mat("Menu_Floor", new ProceduralAssets.MatSpec { colour = new Color(0.035f, 0.036f, 0.042f), smoothness = 0.84f, metallic = 0.1f }),
                ProceduralAssets.Mat("Menu_Wall", new Color(0.07f, 0.075f, 0.09f), 0.3f),
                ProceduralAssets.Mat("Menu_AccentRed", BrandConfig.Current.accentRed, 0.5f, 0f, BrandConfig.Current.accentRed * 3f),
                ProceduralAssets.Mat("Menu_AccentAmber", BrandConfig.Current.primary, 0.5f, 0f, BrandConfig.Current.primary * 2.4f)
            };
            PolyHaven.AddDetail(floorMats[0], "rough_concrete", 4f, null, 0.08f);
            var go = new GameObject("Showroom Shell");
            go.transform.SetParent(showroom, false);
            go.AddComponent<MeshFilter>().sharedMesh = kit.Build("Showroom");
            go.AddComponent<MeshRenderer>().sharedMaterials = floorMats;

            // Studio softboxes: three emissive panels over the turntable and a light strip behind it.
            var softbox = new MeshKit(1);
            foreach (float x in new[] { -3.2f, 0f, 3.2f })
                softbox.Box(new Vector3(x, 8.6f, 0.4f), new Vector3(2.4f, 0.06f, 5.2f), Quaternion.Euler(0f, 0f, x * -2.2f), 0);
            softbox.Box(new Vector3(0f, 2.9f, 9.6f), new Vector3(16f, 0.05f, 0.08f), Quaternion.identity, 0);
            var softGo = new GameObject("Softboxes");
            softGo.transform.SetParent(showroom, false);
            softGo.AddComponent<MeshFilter>().sharedMesh = softbox.Build("Softboxes");
            softGo.AddComponent<MeshRenderer>().sharedMaterial = ProceduralAssets.Mat("Menu_Softbox", new Color(0.95f, 0.96f, 1f), 0.2f, 0f, new Color(3.2f, 3.25f, 3.4f));
            var brand = BrandConfig.Current;
            // Framed for the menu camera: between the header bar and the car roof, right of the menu
            // side panel, and fully on screen from 16:9 down to 4:3.
            UIFactory.WorldText("Wall Wordmark", showroom, brand.wordmark, new Vector3(-3.4f, 3.5f, 9.75f), Quaternion.identity, new Vector2(5.6f, 1.2f), brand.primary, FontStyle.Bold, 400);
            UIFactory.WorldText("Wall Tagline", showroom, brand.productLine.ToUpperInvariant(), new Vector3(-3.4f, 2.4f, 9.75f), Quaternion.identity, new Vector2(5.6f, 0.36f), new Color(0.75f, 0.78f, 0.85f), FontStyle.Bold, 160);

            var turntable = new GameObject("Turntable").transform;
            turntable.SetParent(showroom, false);
            var disc = new MeshKit(2);
            disc.Cylinder(new Vector3(0, -0.02f, 0), 3.6f, 0.06f, 48, 0);
            disc.Cylinder(new Vector3(0, -0.03f, 0), 3.75f, 0.05f, 48, 1);
            var discGo = new GameObject("Disc");
            discGo.transform.SetParent(showroom, false);
            discGo.AddComponent<MeshFilter>().sharedMesh = disc.Build("Turntable");
            discGo.AddComponent<MeshRenderer>().sharedMaterials = new[] { ProceduralAssets.Mat("Menu_Disc", new Color(0.16f, 0.17f, 0.19f), 0.85f, 0.6f), floorMats[3] };

            void SpotAt(string n, Vector3 pos, Vector3 target, float intensity, Color c, float angle)
            {
                var l = new GameObject(n);
                l.transform.SetParent(showroom, false);
                l.transform.position = pos;
                l.transform.LookAt(target);
                var light = l.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = angle;
                light.range = 30f;
                light.intensity = intensity;
                light.color = c;
                light.shadows = LightShadows.Soft;
            }
            // Studio lighting: warm key, cool rim, broad top light from the softboxes, two low fills.
            SpotAt("Key Light", new Vector3(-5f, 7f, -5f), new Vector3(0, 0.5f, 0), 24f, new Color(1f, 0.94f, 0.86f), 50f);
            SpotAt("Rim Light", new Vector3(6f, 6f, 6f), new Vector3(0, 0.6f, 0), 20f, new Color(0.75f, 0.85f, 1f), 50f);
            SpotAt("Top Light", new Vector3(0, 8.4f, 0.4f), new Vector3(0, 0, 0), 22f, Color.white, 60f);
            SpotAt("Fill Left", new Vector3(-7f, 1.6f, 2f), new Vector3(0, 0.6f, 0), 6f, new Color(0.85f, 0.9f, 1f), 60f);
            SpotAt("Fill Right", new Vector3(7f, 1.6f, -3f), new Vector3(0, 0.6f, 0), 6f, new Color(1f, 0.92f, 0.85f), 60f);
            var probeGo = new GameObject("Reflection Probe");
            probeGo.transform.SetParent(showroom, false);
            probeGo.transform.position = new Vector3(0, 2f, 0);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;   // MenuUI re-renders it whenever the car changes
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(40, 14, 30);
            probe.boxProjection = true;
            probe.resolution = 256;
            probe.hdr = true;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 200f;
            camGo.transform.position = new Vector3(0.6f, 2.3f, -11.5f);
            camGo.transform.LookAt(new Vector3(-2.3f, 0.55f, 0f));
            camGo.AddComponent<AudioListener>();
            UrpSetup.SetupCamera(cam, shared.MenuProfile, showroom);

            var ui = new GameObject("Menu UI");
            var audioGo = new GameObject("Menu Audio");
            audioGo.AddComponent<AudioSource>();
            var audio = audioGo.AddComponent<RaceAudio>();
            var s = shared.Sounds;
            audio.Configure(s.Countdown, s.Go, s.Checkpoint, s.Lap, s.Finish, s.Warning, s.Click);
            var menu = ui.AddComponent<MenuUI>();
            menu.Configure(turntable, audio);
            menu.Build(shared.Catalog.Count, RaceRules.RouteCount, shared.Catalog.paintPalette.Length);
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        // ================================================================== Project settings

        static void SetBuildSettings(List<RouteCatalog.Entry> routes)
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MenuScenePath, true) };
            scenes.AddRange(routes.OrderBy(r => r.number).Select(r => new EditorBuildSettingsScene($"Assets/Scenes/{r.sceneName}.unity", true)));
            EditorBuildSettings.scenes = scenes.ToArray();
            PlayerSettings.companyName = "thegameio";
            PlayerSettings.productName = "thegameio Racing";
        }

        /// <summary>Removes the phase-1 daytime track and assets that no longer match the approved design.</summary>
        static void RemoveSupersededContent()
        {
            // One-time migration from the phase-1 prototype (Built-in RP, daytime track): its generated
            // materials, meshes and prefabs are all regenerated for URP, so remove them wholesale.
            if (AssetDatabase.LoadMainAssetAtPath("Assets/Scenes/Track01_GreenValley.unity") != null)
                foreach (var folder in new[] { "Assets/Materials", "Assets/Models", "Assets/Prefabs" })
                    if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            string[] old =
            {
                "Assets/Scenes/Track01_GreenValley.unity", "Assets/Data/Tracks", "Assets/Data/CarCatalog.asset", "Assets/Models/Tracks",
                "Assets/UI", "Assets/Prefabs/Cars/VortexHX.prefab", "Assets/Prefabs/Cars/RaptorV12.prefab", "Assets/Prefabs/Cars/KestrelGT.prefab",
                "Assets/Prefabs/Cars/TitanRS.prefab", "Assets/Prefabs/Cars/FulmineGT.prefab", "Assets/Prefabs/Environment/Tree_Pine.prefab",
                "Assets/Prefabs/Environment/Tree_Round.prefab", "Assets/Prefabs/Environment/LampPost.prefab", "Assets/Settings/RacingPrototype.lighting"
            };
            foreach (var path in old)
                if (AssetDatabase.IsValidFolder(path) || AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets/Models/Cars", "Assets/Materials" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.Contains("VortexHX") || p.Contains("RaptorV12") || p.Contains("KestrelGT") || p.Contains("TitanRS") || p.Contains("FulmineGT")
                    || p.EndsWith("Sky_Procedural.mat") || p.EndsWith("LowPolyCone.asset") || p.EndsWith("MountainCone.asset"))
                    AssetDatabase.DeleteAsset(p);
            }
        }
    }
}
