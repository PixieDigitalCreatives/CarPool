using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// Garage statistics derived from the real <see cref="CarController"/> tuning, so the Speed /
    /// Accel / Handling bars (PPT slide 2) always match how the car actually drives.
    /// </summary>
    public static class CarStats
    {
        public const float KmhPerMs = 3.6f;

        /// <summary>0-100 km/h time for the controller's acceleration curve a(v) = A(1 - (v/vmax)^2).</summary>
        public static float ZeroTo100(float acceleration, float maxSpeed)
        {
            const float target = 100f / KmhPerMs;
            if (maxSpeed <= target) return float.PositiveInfinity;
            return maxSpeed / (2f * acceleration) * Mathf.Log((maxSpeed + target) / (maxSpeed - target));
        }

        /// <summary>Average yaw authority scaled by lateral grip: higher = sharper, more planted cornering.</summary>
        public static float HandlingIndex(float lowSteer, float highSteer, float grip) => (lowSteer + highSteer) * 0.5f * (0.6f + 0.04f * grip);

        public static int SpeedBar(float maxSpeed) => Mathf.Clamp(Mathf.RoundToInt((maxSpeed - 39f) / 3f), 1, 5);
        public static int AccelBar(float acceleration) => Mathf.Clamp(Mathf.RoundToInt((acceleration - 6f) / 2f), 1, 5);
        public static int HandlingBar(float handlingIndex) => Mathf.Clamp(Mathf.RoundToInt((handlingIndex - 40f) / 12f), 1, 5);

        public static int SpeedBar(CarController c) => SpeedBar(c.MaxSpeed);
        public static int AccelBar(CarController c) => AccelBar(c.Acceleration);
        public static int HandlingBar(CarController c) => HandlingBar(HandlingIndex(c.LowSpeedSteerRate, c.HighSpeedSteerRate, c.LateralGrip));
    }
}
