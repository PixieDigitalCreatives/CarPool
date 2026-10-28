using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// Simple racing AI: follows the centre line with a speed-dependent look-ahead, plans braking
    /// from the curvature ahead, sidesteps cars in front and resets itself if it gets stuck.
    /// It drives through the same <see cref="CarController"/> as the player, so it obeys the same
    /// physics, barriers and lap rules. Also used as the optional player "autopilot".
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class AIController : MonoBehaviour
    {
        [SerializeField, Range(0.5f, 1f)] float skill = 0.92f;
        [SerializeField] float preferredLane = 0f;
        [SerializeField] float lookAheadBase = 9f;
        [SerializeField] float lookAheadPerSpeed = 0.45f;
        [SerializeField] float brakingHorizon = 110f;
        [SerializeField] float cornerSafety = 0.82f;
        [SerializeField] float stuckResetSeconds = 2.5f;

        CarController car;
        RaceParticipant participant;
        TrackPath path;
        float[] cornerSpeed;
        float laneOffset;
        float avoidOffset;
        float avoidTimer;
        float wrongWayTimer;

        public bool Driving { get; set; }
        public float Skill => skill;

        void Awake()
        {
            car = GetComponent<CarController>();
            participant = GetComponent<RaceParticipant>();
        }

        public void Configure(TrackPath trackPath, float aiSkill, float lane)
        {
            path = trackPath;
            skill = aiSkill;
            preferredLane = lane;
            laneOffset = lane;
            BuildSpeedProfile();
        }

        void OnDisable()
        {
            if (car == null) return;
            car.ThrottleInput = 0f;
            car.SteerInput = 0f;
        }

        /// <summary>Highest speed per centre-line sample at which this car can still steer through the bend.</summary>
        void BuildSpeedProfile()
        {
            int n = path.SampleCount;
            cornerSpeed = new float[n];
            float top = car.MaxSpeed * skill;
            // Low-grip (drift) cars carry less speed through corners.
            float safety = cornerSafety * Mathf.Clamp(car.LateralGrip / 10f, 0.72f, 1.05f);
            for (int i = 0; i < n; i++)
            {
                float k = 0f;
                for (int o = -3; o <= 3; o++) k = Mathf.Max(k, Mathf.Abs(path.GetSampleCurvature(i + o)));
                float v = top;
                while (v > 10f && v * k > car.SteerRateAt(v) * Mathf.Deg2Rad * safety) v -= 0.5f;
                cornerSpeed[i] = v;
            }
        }

        void FixedUpdate()
        {
            if (!Driving || path == null || participant == null) return;
            float dt = Time.fixedDeltaTime;
            float d = participant.TrackDistance;
            float speed = car.ForwardSpeed;

            // Target speed: the slowest "allowed" speed over the braking horizon.
            float brake = car.BrakeDeceleration * 0.55f;
            float target = car.MaxSpeed * skill;
            int start = path.SampleIndexAt(d);
            int steps = Mathf.CeilToInt(brakingHorizon / path.Spacing);
            for (int s = 0; s < steps; s++)
            {
                float vc = cornerSpeed[(start + s) % cornerSpeed.Length];
                float allowed = Mathf.Sqrt(vc * vc + 2f * brake * s * path.Spacing);
                if (allowed < target) target = allowed;
            }

            float slowCarAhead = UpdateAvoidance(dt, speed);
            if (slowCarAhead >= 0f) target = Mathf.Min(target, slowCarAhead + 2f);

            float maxLane = path.HalfWidthAt(d) - 2.2f;
            float wantedLane = Mathf.Clamp(preferredLane + avoidOffset, -maxLane, maxLane);
            laneOffset = Mathf.MoveTowards(laneOffset, wantedLane, 3f * dt);

            float look = lookAheadBase + Mathf.Max(0f, speed) * lookAheadPerSpeed;
            Vector3 aim = path.GetPoint(d + look) + path.GetRight(d + look) * laneOffset;
            Vector3 local = transform.InverseTransformPoint(aim);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            car.SteerInput = Mathf.Clamp(angle / 18f, -1f, 1f);
            car.ThrottleInput = Mathf.Clamp((target - speed) * 0.35f, -1f, 1f);

            // Recovery.
            wrongWayTimer = participant.WrongWay || Vector3.Dot(transform.forward, path.GetTangent(d)) < -0.3f ? wrongWayTimer + dt : 0f;
            if (participant.StuckTime > stuckResetSeconds || wrongWayTimer > 2f)
            {
                wrongWayTimer = 0f;
                participant.ResetToLastSafePosition();
            }
        }

        /// <summary>Looks for a car ahead; steers around it. Returns its speed if it is close, else -1.</summary>
        float UpdateAvoidance(float dt, float speed)
        {
            avoidTimer -= dt;
            if (avoidTimer <= 0f) avoidOffset = Mathf.MoveTowards(avoidOffset, 0f, 2f * dt);

            Vector3 origin = transform.position + Vector3.up * 0.6f + transform.forward * 2.8f;
            float range = 10f + Mathf.Max(0f, speed) * 0.5f;
            if (!Physics.SphereCast(origin, 1.1f, transform.forward, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Ignore))
                return -1f;
            var other = hit.rigidbody != null ? hit.rigidbody.GetComponent<RaceParticipant>() : null;
            if (other == null || other == participant) return -1f;

            float otherSpeed = Vector3.Dot(other.Car.Velocity, transform.forward);
            if (otherSpeed > speed + 1f) return -1f;   // pulling away, no action

            // Pass on whichever side has more room.
            float side = other.LateralOffset > participant.LateralOffset ? -1f : 1f;
            if (Mathf.Abs(other.LateralOffset) > path.HalfWidthAt(participant.TrackDistance) - 3f) side = -Mathf.Sign(other.LateralOffset);
            avoidOffset = side * 3.6f;
            avoidTimer = 1.2f;
            return hit.distance < 7f ? Mathf.Max(0f, otherSpeed) : -1f;
        }
    }
}
