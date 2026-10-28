using System;
using System.Collections.Generic;
using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// Owns the lap rules (PPT slide 12). A lap counts only when every intermediate checkpoint has
    /// been crossed in order (1, 2, 3, 4) and then the start/finish line is crossed in the driving
    /// direction. Backwards crossings and out-of-order checkpoints never advance progress
    /// ("no shortcuts or wrong-way driving; missed checkpoint = lap not counted").
    /// </summary>
    public class LapManager : MonoBehaviour
    {
        [Tooltip("Element 0 is the start/finish line, then the intermediate checkpoints in driving order.")]
        [SerializeField] TrackCheckpoint[] checkpoints;
        [SerializeField] RaceTimer timer;

        int totalLaps = RaceRules.DefaultLaps;
        readonly List<RaceParticipant> finishOrder = new List<RaceParticipant>();

        public event Action<RaceParticipant, int> CheckpointPassed;
        public event Action<RaceParticipant, int> CheckpointMissed;
        public event Action<RaceParticipant, int> WrongDirectionCrossing;
        public event Action<RaceParticipant> LapCompleted;
        public event Action<RaceParticipant> ParticipantFinished;

        public int TotalLaps => totalLaps;
        public int IntermediateCount => checkpoints != null ? checkpoints.Length - 1 : 0;
        public bool AcceptingCrossings { get; private set; }
        /// <summary>Order in which cars completed the race distance (not the final ranking).</summary>
        public IReadOnlyList<RaceParticipant> FinishOrder => finishOrder;
        public IReadOnlyList<TrackCheckpoint> Checkpoints => checkpoints;

        /// <summary>Called by the editor scene builder.</summary>
        public void Configure(TrackCheckpoint[] orderedCheckpoints, RaceTimer raceTimer)
        {
            checkpoints = orderedCheckpoints;
            timer = raceTimer;
        }

        public void SetLapCount(int laps) => totalLaps = RaceRules.ClampLaps(laps);

        public void PrepareRace(IEnumerable<RaceParticipant> participants)
        {
            AcceptingCrossings = false;
            finishOrder.Clear();
            foreach (var cp in checkpoints) cp.ClearTracking();
            foreach (var p in participants) p.ResetRaceState();
        }

        public void BeginRace() => AcceptingCrossings = true;

        public void StopAcceptingCrossings() => AcceptingCrossings = false;

        /// <param name="crossingTime">Game time (Time.time) of the crossing; negative = now.</param>
        public void RegisterCrossing(RaceParticipant p, int index, bool forward, float crossingTime = -1f)
        {
            if (!AcceptingCrossings || p == null || p.Finished) return;

            if (!forward)
            {
                p.Log("reverse:" + index);
                WrongDirectionCrossing?.Invoke(p, index);
                return;
            }

            if (index != p.NextCheckpoint)
            {
                // Crossing a gate already passed this lap (or the line just completed) is harmless.
                // Crossing one further ahead means a checkpoint was skipped: ignore it.
                if (SequencePosition(index) > SequencePosition(p.NextCheckpoint) && !(index == 0 && p.NextCheckpoint == 1))
                {
                    p.Log("missed:" + p.NextCheckpoint);
                    CheckpointMissed?.Invoke(p, p.NextCheckpoint);
                }
                return;
            }

            if (index == 0)
            {
                CompleteLap(p, crossingTime);
                return;
            }

            p.NextCheckpoint = index >= IntermediateCount ? 0 : index + 1;
            p.Log("cp:" + index);
            CheckpointPassed?.Invoke(p, index);
        }

        int SequencePosition(int checkpointIndex) => checkpointIndex == 0 ? IntermediateCount + 1 : checkpointIndex;

        void CompleteLap(RaceParticipant p, float crossingTime)
        {
            float now;
            if (timer != null) now = crossingTime >= 0f ? timer.ToRaceTime(crossingTime) : timer.Elapsed;
            else now = crossingTime >= 0f ? crossingTime : Time.time;
            now = Mathf.Max(now, p.CurrentLapStartTime);

            float lapTime = now - p.CurrentLapStartTime;
            p.LastLapTime = lapTime;
            p.LapTimes.Add(lapTime);
            if (p.BestLapTime <= 0f || lapTime < p.BestLapTime) p.BestLapTime = lapTime;
            p.LapsCompleted++;
            p.CurrentLapStartTime = now;
            p.NextCheckpoint = 1;
            p.Log("lap:" + p.LapsCompleted);

            if (p.LapsCompleted >= totalLaps)
            {
                finishOrder.Add(p);
                p.MarkFinished(now, finishOrder.Count);
                ParticipantFinished?.Invoke(p);
            }
            else
            {
                LapCompleted?.Invoke(p);
            }
        }

        /// <summary>Tints the gate the given participant must cross next.</summary>
        public void HighlightNextFor(RaceParticipant p)
        {
            if (checkpoints == null) return;
            int next = p != null && !p.Finished ? p.NextCheckpoint : -1;
            foreach (var cp in checkpoints)
                if (cp != null) cp.SetHighlighted(cp.Index == next);
        }
    }
}
