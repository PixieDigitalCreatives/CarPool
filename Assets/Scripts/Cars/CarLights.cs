using UnityEngine;

namespace RacingPrototype
{
    /// <summary>Brake lights: the tail-light material slots glow brighter while the car is braking.</summary>
    public class CarLights : MonoBehaviour
    {
        [SerializeField] Renderer[] tailRenderers;
        [SerializeField] int[] tailSlots;
        [SerializeField, ColorUsage(false, true)] Color tailEmission = new Color(2f, 0.1f, 0.06f);
        [SerializeField] float brakeBoost = 3.5f;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        CarController car;
        MaterialPropertyBlock block;
        bool braking, applied;

        /// <summary>Called by the editor car builder.</summary>
        public void Configure(Renderer[] renderers, int[] slots, Color emission)
        {
            tailRenderers = renderers;
            tailSlots = slots;
            tailEmission = emission;
        }

        void Awake() => car = GetComponent<CarController>();

        void LateUpdate()
        {
            bool now = car != null && car.ThrottleInput < -0.1f && car.ForwardSpeed > 0.5f;
            if (applied && now == braking) return;
            braking = now;
            applied = true;
            if (tailRenderers == null) return;
            block ??= new MaterialPropertyBlock();
            for (int i = 0; i < tailRenderers.Length; i++)
            {
                var r = tailRenderers[i];
                if (r == null) continue;
                int slot = tailSlots != null && i < tailSlots.Length ? tailSlots[i] : 0;
                r.GetPropertyBlock(block, slot);
                block.SetColor(EmissionId, tailEmission * (braking ? brakeBoost : 1f));
                r.SetPropertyBlock(block, slot);
            }
        }
    }
}
