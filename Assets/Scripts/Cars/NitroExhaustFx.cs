using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// Visual-only "nitro feel" for the tuner car (PPT: "Tuner / drift racer with nitro feel"):
    /// blue exhaust flames that grow with throttle. It does not change performance; no boost or
    /// power-up is part of the approved design.
    /// </summary>
    public class NitroExhaustFx : MonoBehaviour
    {
        [SerializeField] Transform[] flames;
        [SerializeField] float maxLength = 0.55f;

        CarController car;
        Vector3[] baseScales;
        float current;

        public void Configure(Transform[] flameTransforms) => flames = flameTransforms;

        void Awake()
        {
            car = GetComponent<CarController>();
            baseScales = new Vector3[flames != null ? flames.Length : 0];
            for (int i = 0; i < baseScales.Length; i++) baseScales[i] = flames[i].localScale;
        }

        void Update()
        {
            if (car == null || flames == null) return;
            bool hard = car.ControlsEnabled && car.ThrottleInput > 0.5f && car.ForwardSpeed > 4f && car.NormalisedSpeed < 0.97f;
            current = Mathf.MoveTowards(current, hard ? 1f : 0f, Time.deltaTime * (hard ? 6f : 3f));
            float flicker = 0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 18f, 0.3f);
            for (int i = 0; i < flames.Length; i++)
            {
                if (flames[i] == null) continue;
                flames[i].gameObject.SetActive(current > 0.02f);
                var s = baseScales[i];
                flames[i].localScale = new Vector3(s.x, s.y, Mathf.Max(0.001f, maxLength * current * flicker));
            }
        }
    }
}
