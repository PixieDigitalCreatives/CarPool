using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// Arcade driving model on a single Rigidbody: throttle/brake forces along the car, a lateral
    /// grip force that bleeds off sideways slip, and yaw driven directly from steering. Pitch and
    /// roll are frozen so the car always stays upright; barriers and other cars are ordinary
    /// colliders, so collisions are handled by PhysX.
    /// Inputs come from <see cref="PlayerCarInput"/> or <see cref="AIController"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        [Header("Performance")]
        [SerializeField] float maxSpeed = 50f;               // m/s (~180 km/h)
        [SerializeField] float acceleration = 13f;           // m/s^2 from standstill
        [SerializeField] float brakeDeceleration = 26f;
        [SerializeField] float maxReverseSpeed = 11f;
        [SerializeField] float reverseAcceleration = 7f;
        [SerializeField] float coastDeceleration = 3f;

        [Header("Handling")]
        [SerializeField] float lowSpeedSteerRate = 105f;     // deg/s yaw at low speed
        [SerializeField] float highSpeedSteerRate = 58f;     // deg/s yaw at top speed
        [SerializeField] float fullSteerSpeed = 7f;          // speed needed for full yaw authority
        [SerializeField] float lateralGrip = 9f;             // 1/s, how fast sideways slip is removed
        [SerializeField] float steerSmoothing = 6f;          // input units per second
        [SerializeField] float yawResponse = 12f;
        [SerializeField] float downforce = 8f;

        [Header("Visuals")]
        [SerializeField] Transform[] steeringPivots;
        [SerializeField] Transform[] wheelSpinners;
        [SerializeField] float wheelRadius = 0.35f;
        [SerializeField] float maxVisualSteerAngle = 28f;

        static PhysicsMaterial bodyMaterial;

        Rigidbody body;
        float smoothedSteer;
        float wheelSpin;

        public float ThrottleInput { get; set; }
        public float SteerInput { get; set; }
        /// <summary>False holds the car still (countdown, garage).</summary>
        public bool ControlsEnabled { get; set; }

        public bool IsGrounded { get; private set; }
        public float ForwardSpeed { get; private set; }
        public float SpeedKmh => Mathf.Abs(ForwardSpeed) * 3.6f;
        public float MaxSpeed => maxSpeed;
        public float BrakeDeceleration => brakeDeceleration;
        public float NormalisedSpeed => Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / maxSpeed);
        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;
        public Rigidbody Body => body;
        public float SmoothedSteer => smoothedSteer;

        /// <summary>Called by the editor car builder.</summary>
        public void ConfigureVisuals(Transform[] pivots, Transform[] spinners, float radius)
        {
            steeringPivots = pivots;
            wheelSpinners = spinners;
            wheelRadius = radius;
        }

        /// <summary>Called by the editor car builder. These values drive the garage stat bars (see <see cref="CarStats"/>).</summary>
        public void ConfigurePerformance(float topSpeed, float accel, float lowSteer, float highSteer, float grip)
        {
            maxSpeed = topSpeed;
            acceleration = accel;
            lowSpeedSteerRate = lowSteer;
            highSpeedSteerRate = highSteer;
            lateralGrip = grip;
        }

        public float Acceleration => acceleration;
        public float LowSpeedSteerRate => lowSpeedSteerRate;
        public float HighSpeedSteerRate => highSpeedSteerRate;
        public float LateralGrip => lateralGrip;

        /// <summary>Yaw rate (deg/s) the car can reach at a given speed; AI uses it to plan corner speeds.</summary>
        public float SteerRateAt(float speed) => Mathf.Lerp(lowSpeedSteerRate, highSpeedSteerRate, Mathf.Clamp01(Mathf.Abs(speed) / maxSpeed));

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (bodyMaterial == null)
            {
                // Frictionless shell: the car's own grip model handles traction, and cars slide
                // along barriers instead of snagging on them.
                bodyMaterial = new PhysicsMaterial("CarBody")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    bounciness = 0.05f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Average
                };
            }
            foreach (var c in GetComponentsInChildren<Collider>())
                if (!c.isTrigger) c.sharedMaterial = bodyMaterial;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 v = body.linearVelocity;
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;
            ForwardSpeed = Vector3.Dot(v, forward);
            float lateralSpeed = Vector3.Dot(v, right);
            IsGrounded = Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, 0.9f, ~0, QueryTriggerInteraction.Ignore);

            if (!ControlsEnabled)
            {
                body.linearVelocity = new Vector3(0f, Mathf.Min(v.y, 0f), 0f);
                body.angularVelocity = Vector3.zero;
                smoothedSteer = 0f;
                ForwardSpeed = 0f;
                return;
            }

            float throttle = Mathf.Clamp(ThrottleInput, -1f, 1f);
            smoothedSteer = Mathf.MoveTowards(smoothedSteer, Mathf.Clamp(SteerInput, -1f, 1f), steerSmoothing * dt);
            if (!IsGrounded) return;

            // Longitudinal: accelerate, brake, reverse or coast.
            float accel;
            if (throttle > 0.01f)
            {
                if (ForwardSpeed < -0.5f) accel = brakeDeceleration * throttle;
                else
                {
                    float speedRatio = Mathf.Clamp01(ForwardSpeed / maxSpeed);
                    accel = acceleration * throttle * (1f - speedRatio * speedRatio);
                }
            }
            else if (throttle < -0.01f)
            {
                if (ForwardSpeed > 0.5f) accel = -brakeDeceleration * -throttle;
                else accel = ForwardSpeed > -maxReverseSpeed ? reverseAcceleration * throttle : 0f;
            }
            else
            {
                accel = -Mathf.Clamp(ForwardSpeed / dt, -coastDeceleration, coastDeceleration);
            }
            body.AddForce(forward * accel, ForceMode.Acceleration);

            // Lateral grip: remove a fraction of the sideways velocity each step.
            float gripFraction = Mathf.Min(1f, lateralGrip * dt);
            body.AddForce(right * (-lateralSpeed * gripFraction), ForceMode.VelocityChange);

            // Yaw from steering, scaled so a stationary car cannot spin on the spot.
            float speedAbs = Mathf.Abs(ForwardSpeed);
            float steerRate = SteerRateAt(speedAbs) * Mathf.Clamp01(speedAbs / fullSteerSpeed);
            float direction = ForwardSpeed >= 0f ? 1f : -1f;
            float targetYaw = smoothedSteer * steerRate * Mathf.Deg2Rad * direction;
            float yaw = Mathf.Lerp(body.angularVelocity.y, targetYaw, Mathf.Clamp01(yawResponse * dt));
            body.angularVelocity = new Vector3(0f, yaw, 0f);

            body.AddForce(Vector3.down * (downforce * NormalisedSpeed), ForceMode.Acceleration);

            // Hard cap (e.g. after being shunted from behind).
            Vector3 flat = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
            float cap = maxSpeed * 1.1f;
            if (flat.sqrMagnitude > cap * cap)
            {
                flat = flat.normalized * cap;
                body.linearVelocity = new Vector3(flat.x, body.linearVelocity.y, flat.z);
            }
        }

        void Update()
        {
            wheelSpin = (wheelSpin + ForwardSpeed / Mathf.Max(wheelRadius, 0.05f) * Mathf.Rad2Deg * Time.deltaTime) % 360f;
            if (wheelSpinners != null)
                foreach (var w in wheelSpinners)
                    if (w != null) w.localRotation = Quaternion.Euler(wheelSpin, 0f, 0f);
            if (steeringPivots != null)
                foreach (var p in steeringPivots)
                    if (p != null) p.localRotation = Quaternion.Euler(0f, smoothedSteer * maxVisualSteerAngle, 0f);
        }

        /// <summary>Instantly moves the car and clears its motion (grid placement and resets).</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            smoothedSteer = 0f;
            ForwardSpeed = 0f;
        }
    }
}
