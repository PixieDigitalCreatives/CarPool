using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// Race clock started at "GO" (all cars start together). Uses scaled game time, so pausing
    /// (timeScale 0) pauses it and times recorded in physics callbacks line up with the simulation.
    /// </summary>
    public class RaceTimer : MonoBehaviour
    {
        float startTime;
        float frozenElapsed;

        public bool IsRunning { get; private set; }
        public bool HasStarted { get; private set; }

        public float Elapsed => IsRunning ? Time.time - startTime : frozenElapsed;

        /// <summary>Converts a game time stamp (Time.time) to race time.</summary>
        public float ToRaceTime(float gameTime) => gameTime - startTime;

        public void Begin()
        {
            startTime = Time.time;
            frozenElapsed = 0f;
            IsRunning = true;
            HasStarted = true;
        }

        public void Stop()
        {
            if (!IsRunning) return;
            frozenElapsed = Elapsed;
            IsRunning = false;
        }

        public void ResetTimer()
        {
            IsRunning = false;
            HasStarted = false;
            frozenElapsed = 0f;
        }
    }
}
