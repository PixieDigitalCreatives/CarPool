using UnityEngine;

namespace RacingPrototype
{
    /// <summary>Per-scene route identity: number, name, districts, par time and minimap mapping.</summary>
    public class RouteInfo : MonoBehaviour
    {
        [SerializeField] int routeIndex;
        [SerializeField] string routeName;
        [SerializeField] string districts;
        [SerializeField] Sprite minimap;
        [SerializeField] Vector2 mapMin;
        [SerializeField] Vector2 mapMax;
        [SerializeField] float parLapSeconds = 45f;
        [SerializeField] float lengthMetres;

        public static RouteInfo Current { get; private set; }

        public int RouteIndex => routeIndex;
        public int RouteNumber => routeIndex + 1;
        public string RouteName => routeName;
        public string Districts => districts;
        public Sprite Minimap => minimap;
        public float ParLapSeconds => parLapSeconds;
        public float LengthMetres => lengthMetres;

        /// <summary>Called by the editor scene builder.</summary>
        public void Configure(int index, string name, string districtSummary, Sprite map, Vector2 min, Vector2 max, float parLap, float length)
        {
            routeIndex = index; routeName = name; districts = districtSummary; minimap = map;
            mapMin = min; mapMax = max; parLapSeconds = parLap; lengthMetres = length;
        }

        void Awake() => Current = this;

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <summary>World XZ position to normalised minimap coordinates (0-1).</summary>
        public Vector2 ToMap(Vector3 world) => new Vector2(
            Mathf.InverseLerp(mapMin.x, mapMax.x, world.x),
            Mathf.InverseLerp(mapMin.y, mapMax.y, world.z));
    }
}
