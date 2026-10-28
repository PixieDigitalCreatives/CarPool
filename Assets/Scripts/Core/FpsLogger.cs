using UnityEngine;

namespace RacingPrototype
{
    /// <summary>"-fpslog": logs average frame rate every 5 seconds (used for performance checks).</summary>
    public class FpsLogger : MonoBehaviour
    {
        float elapsed;
        int frames;

        void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            frames++;
            if (elapsed < 5f) return;
            Debug.Log($"[FPS] {frames / elapsed:F1} fps average over {elapsed:F1} s at {Screen.width}x{Screen.height}, {QualityTiers.Current} tier");
            elapsed = 0f;
            frames = 0;
        }
    }
}
