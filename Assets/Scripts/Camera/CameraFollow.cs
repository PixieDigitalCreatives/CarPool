using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// Chase camera that smooths the car's heading (not its position, so there is no speed-dependent
    /// lag), plus a slow showroom orbit used in the garage.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        public enum Mode { Follow, Orbit }

        [Header("Chase")]
        [SerializeField] float distance = 7f;
        [SerializeField] float extraDistanceAtSpeed = 1.5f;
        [SerializeField] float height = 2.7f;
        [SerializeField] float lookHeight = 1.1f;
        [SerializeField] float lookAhead = 4f;
        [SerializeField] float headingSmoothTime = 0.18f;
        [SerializeField] float baseFieldOfView = 60f;
        [SerializeField] float fieldOfViewBoost = 12f;
        [SerializeField] float referenceSpeed = 50f;

        [Header("Garage orbit")]
        [SerializeField] float orbitDistance = 7.2f;
        [SerializeField] float orbitHeight = 2.3f;
        [SerializeField] float orbitDegreesPerSecond = 12f;
        [Tooltip("Shifts the framing so the car sits right of the garage panel.")]
        [SerializeField] float orbitScreenShift = 2.2f;

        Camera cam;
        Transform target;
        Rigidbody targetBody;
        Mode mode = Mode.Orbit;
        float heading;
        float headingVelocity;
        float orbitAngle = 150f;

        public Mode CurrentMode => mode;
        public Transform Target => target;

        void Awake() => cam = GetComponent<Camera>();

        public void Follow(Transform car)
        {
            target = car;
            targetBody = car != null ? car.GetComponent<Rigidbody>() : null;
            mode = Mode.Follow;
            if (car != null) heading = car.eulerAngles.y;
            headingVelocity = 0f;
            LateUpdate();
        }

        public void Orbit(Transform car)
        {
            target = car;
            targetBody = null;
            mode = Mode.Orbit;
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (mode == Mode.Follow) UpdateChase();
            else UpdateOrbit();
        }

        void UpdateChase()
        {
            float dt = Time.deltaTime;
            heading = Mathf.SmoothDampAngle(heading, target.eulerAngles.y, ref headingVelocity, headingSmoothTime);
            float speed01 = targetBody != null ? Mathf.Clamp01(targetBody.linearVelocity.magnitude / referenceSpeed) : 0f;

            Quaternion rot = Quaternion.Euler(0f, heading, 0f);
            Vector3 pos = target.position + rot * new Vector3(0f, height, -(distance + extraDistanceAtSpeed * speed01));
            Vector3 lookAt = target.position + Vector3.up * lookHeight + rot * Vector3.forward * lookAhead;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos, Vector3.up));
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFieldOfView + fieldOfViewBoost * speed01, 1f - Mathf.Exp(-3f * dt));
        }

        void UpdateOrbit()
        {
            orbitAngle += orbitDegreesPerSecond * Time.unscaledDeltaTime;
            Quaternion rot = Quaternion.Euler(0f, target.eulerAngles.y + orbitAngle, 0f);
            Vector3 centre = target.position + Vector3.up * 0.7f;
            Vector3 pos = centre + rot * new Vector3(0f, orbitHeight, -orbitDistance);
            Quaternion look = Quaternion.LookRotation(centre - pos, Vector3.up);
            pos -= look * Vector3.right * orbitScreenShift;
            transform.SetPositionAndRotation(pos, look);
            cam.fieldOfView = baseFieldOfView;
        }
    }
}
