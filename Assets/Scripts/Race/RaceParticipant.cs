using System.Collections.Generic;
using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// Race identity and live race state for one car (player or AI). Lap/checkpoint fields are
    /// written only by <see cref="LapManager"/>; final results by <see cref="RaceManager"/>;
    /// tracking fields are updated here every physics step.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class RaceParticipant : MonoBehaviour
    {
        [SerializeField] string displayName = "Driver";
        [SerializeField] string carName = "Car";

        TrackPath path;
        float lastSafeDistance;
        float lastSafeLateral;
        float wrongWayTimer;
        float stuckTimer;

        public string DisplayName => displayName;
        public string CarName => carName;
        public bool IsPlayer { get; private set; }
        public int CarIndex { get; private set; }
        public int GridSlot { get; set; }
        public int RaceNumber { get; set; }
        public Color Colour { get; set; } = Color.white;
        public CarController Car { get; private set; }
        public AIController AI { get; private set; }

        // Lap state (LapManager)
        public int LapsCompleted { get; set; }
        public int NextCheckpoint { get; set; } = 1;
        public float CurrentLapStartTime { get; set; }
        public float LastLapTime { get; set; } = -1f;
        public float BestLapTime { get; set; } = -1f;
        public readonly List<float> LapTimes = new List<float>();
        public bool Finished { get; private set; }
        /// <summary>Total race time (start to final crossing), seconds.</summary>
        public float FinishTime { get; private set; } = -1f;
        /// <summary>Order in which cars crossed the line (earliest finish tie-break).</summary>
        public int FinishOrder { get; private set; }

        // Final result (RaceManager)
        public bool Dnf { get; set; }
        public int FinalRank { get; set; }
        public bool ResultValid { get; set; } = true;
        public string ResultNote { get; set; } = "";

        // Tracking
        public float TrackDistance { get; private set; }
        public float LateralOffset { get; private set; }
        public bool WrongWay { get; private set; }
        public float StuckTime => stuckTimer;
        public int Position { get; set; }
        public int ResetCount { get; private set; }

        /// <summary>Chronological record of lap events ("cp:1", "lap:1", "reverse:0", ...) for HUD, validation and tests.</summary>
        public readonly List<string> EventLog = new List<string>();

        void Awake()
        {
            Car = GetComponent<CarController>();
            AI = GetComponent<AIController>();
        }

        public void Setup(string driverName, string modelName, bool isPlayer, int carIndex)
        {
            displayName = driverName;
            carName = modelName;
            IsPlayer = isPlayer;
            CarIndex = carIndex;
        }

        public void BindTrack(TrackPath trackPath)
        {
            path = trackPath;
            TrackDistance = path.FindClosestDistance(transform.position);
            LateralOffset = path.GetLateralOffset(transform.position, TrackDistance);
            lastSafeDistance = TrackDistance;
            lastSafeLateral = LateralOffset;
        }

        public void ResetRaceState()
        {
            LapsCompleted = 0;
            NextCheckpoint = 1;
            CurrentLapStartTime = 0f;
            LastLapTime = -1f;
            BestLapTime = -1f;
            LapTimes.Clear();
            Finished = false;
            FinishTime = -1f;
            FinishOrder = 0;
            Dnf = false;
            FinalRank = 0;
            ResultValid = true;
            ResultNote = "";
            wrongWayTimer = 0f;
            stuckTimer = 0f;
            EventLog.Clear();
        }

        public void MarkFinished(float time, int finishOrder)
        {
            Finished = true;
            FinishTime = time;
            FinishOrder = finishOrder;
            Log("finish:" + finishOrder);
        }

        public void Log(string entry) => EventLog.Add(entry);

        /// <summary>
        /// Monotonic race progress in metres: laps * length + distance into the current lap.
        /// Handles the short stretches either side of the line before the crossing registers.
        /// </summary>
        public float ComputeProgress()
        {
            if (path == null) return 0f;
            float length = path.Length;
            float d = TrackDistance;
            if (NextCheckpoint == 0 && d < length * 0.5f) d += length;
            else if (NextCheckpoint == 1 && d > length * 0.5f) d -= length;
            return LapsCompleted * length + d;
        }

        void FixedUpdate()
        {
            if (path == null || Car == null) return;
            float dt = Time.fixedDeltaTime;

            TrackDistance = path.FindClosestDistance(transform.position, TrackDistance);
            LateralOffset = path.GetLateralOffset(transform.position, TrackDistance);
            Vector3 tangent = path.GetTangent(TrackDistance);
            float facing = Vector3.Dot(transform.forward, tangent);
            float alongTrackSpeed = Vector3.Dot(Car.Velocity, tangent);

            if (Car.IsGrounded && Mathf.Abs(LateralOffset) < path.HalfWidthAt(TrackDistance) - 1f && facing > 0.6f && Car.ForwardSpeed > 2f)
            {
                lastSafeDistance = TrackDistance;
                lastSafeLateral = LateralOffset;
            }

            bool goingWrongWay = Car.ControlsEnabled && alongTrackSpeed < -3f && facing < -0.2f;
            wrongWayTimer = goingWrongWay ? wrongWayTimer + dt : 0f;
            WrongWay = wrongWayTimer > 1f;

            bool stalled = Car.ControlsEnabled && !Finished && Mathf.Abs(Car.ForwardSpeed) < 1.5f;
            stuckTimer = stalled ? stuckTimer + dt : 0f;

            if (transform.position.y < -10f) ResetToLastSafePosition();
        }

        /// <summary>
        /// Recovery: puts the car back on the road a few metres behind the last safe point, facing
        /// the driving direction. Resets never move a car forward past a gate it hasn't crossed.
        /// </summary>
        public void ResetToLastSafePosition()
        {
            if (path == null) return;
            float baseDistance = lastSafeDistance - 4f;
            float maxLateral = path.HalfWidthAt(baseDistance) - 2.5f;
            float[] lateralCandidates = { Mathf.Clamp(lastSafeLateral, -maxLateral, maxLateral), 0f, -3.5f, 3.5f };
            float[] backCandidates = { 0f, -8f, -16f };

            Vector3 chosenPos = Vector3.zero;
            Quaternion chosenRot = Quaternion.identity;
            float chosenDistance = baseDistance;
            bool found = false;
            foreach (float back in backCandidates)
            {
                foreach (float lateral in lateralCandidates)
                {
                    float d = baseDistance + back;
                    Vector3 pos = path.GetPoint(d) + path.GetRight(d) * lateral + Vector3.up * 0.05f;
                    Quaternion rot = Quaternion.LookRotation(path.GetTangent(d), Vector3.up);
                    if (IsSpaceFree(pos, rot))
                    {
                        chosenPos = pos; chosenRot = rot; chosenDistance = d; found = true;
                        break;
                    }
                }
                if (found) break;
            }
            if (!found)
            {
                chosenPos = path.GetPoint(baseDistance) + Vector3.up * 0.05f;
                chosenRot = Quaternion.LookRotation(path.GetTangent(baseDistance), Vector3.up);
            }

            Car.Teleport(chosenPos, chosenRot);
            TrackDistance = path.Wrap(chosenDistance);
            stuckTimer = 0f;
            wrongWayTimer = 0f;
            WrongWay = false;
            ResetCount++;
            Log("reset");
        }

        bool IsSpaceFree(Vector3 pos, Quaternion rot)
        {
            var hits = Physics.OverlapBox(pos + Vector3.up * 0.9f, new Vector3(1.3f, 0.6f, 2.6f), rot, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.attachedRigidbody != null && h.attachedRigidbody.gameObject == gameObject) continue;
                return false;
            }
            return true;
        }
    }
}
