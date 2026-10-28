using UnityEngine;

namespace RacingPrototype
{
    /// <summary>Keyboard driving: W/Up accelerate, S/Down brake/reverse, A/D or Left/Right steer, R reset.</summary>
    [RequireComponent(typeof(CarController))]
    public class PlayerCarInput : MonoBehaviour
    {
        CarController car;
        RaceParticipant participant;

        void Awake()
        {
            car = GetComponent<CarController>();
            participant = GetComponent<RaceParticipant>();
        }

        void OnDisable()
        {
            if (car == null) return;
            car.ThrottleInput = 0f;
            car.SteerInput = 0f;
        }

        void Update()
        {
            float throttle = 0f, steer = 0f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) throttle += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) throttle -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) steer -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) steer += 1f;
            car.ThrottleInput = throttle;
            car.SteerInput = steer;

            if (Input.GetKeyDown(KeyCode.R) && participant != null && CanReset())
                participant.ResetToLastSafePosition();
        }

        static bool CanReset()
        {
            var race = RaceManager.Instance;
            return race == null || race.State == RaceState.Racing;
        }
    }
}
