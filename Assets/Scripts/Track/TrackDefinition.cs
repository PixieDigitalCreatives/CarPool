using UnityEngine;

namespace RacingPrototype
{
    /// <summary>City district types used to dress each sector of a route (PPT slides 5-8, 14).</summary>
    public enum District
    {
        DowntownAvenue,
        CommercialJunction,
        MarketStreet,
        ResidentialStreet,
        CivicSquare,
        WaterfrontPromenade,
        HotelBoulevard,
        PortQuay,
        ContainerYard,
        Warehouses,
        HillsideSwitchbacks,
        HillsideRidge
    }

    public enum RouteTheme { CityLoop, Waterfront, IndustrialPort, Hillside }

    /// <summary>
    /// Data for one of the 4 approved routes. The editor builder (menu: Racing Prototype) turns a
    /// definition into a complete scene: road, sidewalks, barriers, checkpoints, 10-car grid,
    /// city districts, lighting and minimap.
    /// </summary>
    [CreateAssetMenu(menuName = "Racing Prototype/Track Definition", fileName = "TrackDefinition")]
    public class TrackDefinition : ScriptableObject
    {
        [System.Serializable]
        public struct WidthKey
        {
            [Range(0f, 1f)] public float fraction;
            public float width;
            public WidthKey(float fraction, float width) { this.fraction = fraction; this.width = width; }
        }

        [System.Serializable]
        public struct Sector
        {
            public string name;
            public District district;
            [Range(0f, 1f)] public float start;
            [Range(0f, 1f)] public float end;
            public Sector(string name, District district, float start, float end) { this.name = name; this.district = district; this.start = start; this.end = end; }
            public bool Contains(float f) => start <= end ? f >= start && f < end : f >= start || f < end;
        }

        [Header("Identity")]
        public int routeNumber = 1;
        public string routeName = "Route";
        [Tooltip("Scene file name created under Assets/Scenes.")]
        public string sceneName = "Route01";
        public RouteTheme theme;
        [TextArea] public string description;

        [Header("Layout")]
        [Tooltip("Centre-line control points (X, Z) in metres, in driving order. Point 0 is the start/finish line on a straight.")]
        public Vector2[] controlPoints;
        public float sampleSpacing = 2f;
        [Tooltip("Smoothing passes applied to the spline; higher = gentler corners.")]
        public int smoothingIterations = 200;

        [Header("Cross-section")]
        [Tooltip("Road width along the lap (fraction 0-1). Wider opening straight etc.")]
        public WidthKey[] widthKeys = { new WidthKey(0f, 14f) };
        [Tooltip("Gap between the road edge and the inner face of the barrier (kerb + run-off).")]
        public float barrierGap = 1.2f;
        public float barrierHeight = 1.05f;
        public float barrierThickness = 0.7f;
        public float sidewalkWidth = 4.5f;

        [Header("Race layout")]
        [Tooltip("Intermediate checkpoints as fractions of the lap, in driving order.")]
        public float[] checkpointFractions = { 0.2f, 0.4f, 0.6f, 0.8f };
        [Tooltip("Distance after the start/finish line of grid row 1 (pole).")]
        public float gridPoleDistance = 52f;
        public float gridRowSpacing = 8f;
        public float gridLateralOffset = 3.1f;
        [Tooltip("Reference pace used for the par lap time (time limit = 2 x par).")]
        public float parSpeed = 30f;

        [Header("City")]
        public Sector[] sectors;
        [Tooltip("Fractions of the lap where side streets meet the circuit (visual junctions, blocked by barriers).")]
        public float[] junctions = { };
        public int environmentSeed = 2026;

        public float WidthAtFraction(float f)
        {
            if (widthKeys == null || widthKeys.Length == 0) return 14f;
            if (widthKeys.Length == 1) return widthKeys[0].width;
            f = Mathf.Repeat(f, 1f);
            for (int i = 0; i < widthKeys.Length; i++)
            {
                var a = widthKeys[i];
                var b = widthKeys[(i + 1) % widthKeys.Length];
                float bf = b.fraction <= a.fraction ? b.fraction + 1f : b.fraction;
                float ff = f < a.fraction ? f + 1f : f;
                if (ff >= a.fraction && ff <= bf)
                {
                    float t = Mathf.InverseLerp(a.fraction, bf, ff);
                    return Mathf.Lerp(a.width, b.width, 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI));
                }
            }
            return widthKeys[0].width;
        }

        public District DistrictAt(float f)
        {
            if (sectors != null)
                foreach (var s in sectors)
                    if (s.Contains(Mathf.Repeat(f, 1f))) return s.district;
            return District.DowntownAvenue;
        }

        public string SectorNameAt(float f)
        {
            if (sectors != null)
                foreach (var s in sectors)
                    if (s.Contains(Mathf.Repeat(f, 1f))) return s.name;
            return routeName;
        }
    }
}
