using UnityEngine;

namespace RacingPrototype
{
    /// <summary>Holds the car catalog and spawns configured cars (player or AI) from it.</summary>
    public class CarSelection : MonoBehaviour
    {
        [SerializeField] CarCatalog catalog;

        public CarCatalog Catalog => catalog != null ? catalog : (catalog = CarCatalog.Current);
        public int Count => Catalog != null ? Catalog.Count : 0;

        /// <summary>Called by the editor scene builder.</summary>
        public void Configure(CarCatalog carCatalog) => catalog = carCatalog;

        public CarCatalog.Entry Get(int index) => Catalog.Get(index);

        public RaceParticipant Spawn(int index, Vector3 position, Quaternion rotation, Color paint, string driverName, bool isPlayer)
        {
            var entry = Catalog.Get(index);
            var go = Instantiate(entry.prefab, position, rotation);
            go.name = (isPlayer ? "Player_" : "Racer_") + entry.id;

            var participant = go.GetComponent<RaceParticipant>();
            participant.Setup(driverName, entry.DisplayName, isPlayer, index);
            go.GetComponent<CarColourCustomizer>().SetColour(paint);

            var input = go.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = isPlayer;
            var ai = go.GetComponent<AIController>();
            if (ai != null) ai.enabled = !isPlayer;

            go.GetComponent<CarController>().ControlsEnabled = false;
            return participant;
        }
    }
}
