using UnityEngine;

namespace RacingPrototype
{
    /// <summary>The five selectable cars (PPT slide 2) and the paint palette shown in the garage.</summary>
    [CreateAssetMenu(menuName = "Racing Prototype/Car Catalog", fileName = "CarCatalog")]
    public class CarCatalog : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public string id;
            [Tooltip("PPT category label, e.g. Regular, Street Rally.")]
            public string category;
            [Tooltip("Original model name (working names per the demo-details doc).")]
            public string modelName;
            [TextArea] public string description;
            public GameObject prefab;
            public Color defaultColour = Color.red;
            [Tooltip("Garage card icon: a sprite path under Resources, e.g. UI/Icon_Car.")]
            public string icon = "UI/Icon_Car";

            [Header("Derived from the prefab's CarController (refreshed by the builder)")]
            public float topSpeedKmh;
            public float zeroTo100;
            public float handlingIndex;
            [Range(1, 5)] public int speedBar = 3;
            [Range(1, 5)] public int accelBar = 3;
            [Range(1, 5)] public int handlingBar = 3;

            public string DisplayName => modelName;
        }

        public Entry[] cars;
        public Color[] paintPalette;

        public int Count => cars != null ? cars.Length : 0;
        public Entry Get(int index) => cars[Mathf.Clamp(index, 0, Count - 1)];

        static CarCatalog current;
        public static CarCatalog Current => current != null ? current : (current = Resources.Load<CarCatalog>("CarCatalog"));
    }
}
