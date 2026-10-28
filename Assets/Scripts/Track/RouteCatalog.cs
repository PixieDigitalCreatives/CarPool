using UnityEngine;

namespace RacingPrototype
{
    /// <summary>The 4 approved routes, for the route-select and pool screens (Resources/RouteCatalog).</summary>
    [CreateAssetMenu(menuName = "Racing Prototype/Route Catalog", fileName = "RouteCatalog")]
    public class RouteCatalog : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public int number;
            public string name;
            public string sceneName;
            public string districts;
            [TextArea] public string description;
            public float lengthMetres;
            public float parLapSeconds;
            public Sprite minimap;
        }

        public Entry[] routes;

        public int Count => routes != null ? routes.Length : 0;
        public Entry Get(int index) => routes[Mathf.Clamp(index, 0, Count - 1)];

        static RouteCatalog current;
        public static RouteCatalog Current => current != null ? current : (current = Resources.Load<RouteCatalog>("RouteCatalog"));
    }
}
