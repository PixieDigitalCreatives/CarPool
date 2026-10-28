using System.Collections.Generic;
using UnityEngine;

namespace RacingPrototype
{
    public struct LeaderboardEntry
    {
        public int Position;
        public RaceParticipant Participant;
        public string Name;
        public int CurrentLap;
        public int TotalLaps;
        public string Status;
        public float TotalTime;   // -1 until finished
        public float BestLap;     // -1 until a lap is complete
        public bool IsPlayer;
    }

    /// <summary>
    /// Live in-race standings built from real race data: cars that have finished by total time
    /// (tie: best lap, then earliest finish), everyone else by distance covered. Once the race is
    /// finalised the official ranking (with DNF) from <see cref="RaceManager"/> is shown.
    /// </summary>
    public class LeaderboardManager : MonoBehaviour
    {
        [SerializeField] LapManager lapManager;

        readonly List<RaceParticipant> participants = new List<RaceParticipant>();
        readonly List<LeaderboardEntry> standings = new List<LeaderboardEntry>();
        System.Comparison<RaceParticipant> comparison;
        bool raceStarted;

        public IReadOnlyList<LeaderboardEntry> Standings => standings;

        /// <summary>Called by the editor scene builder.</summary>
        public void Configure(LapManager laps) => lapManager = laps;

        public void SetParticipants(IEnumerable<RaceParticipant> racers)
        {
            participants.Clear();
            participants.AddRange(racers);
            raceStarted = false;
            Refresh();
        }

        public void MarkRaceStarted() => raceStarted = true;

        void LateUpdate() => Refresh();

        public void Refresh()
        {
            participants.RemoveAll(p => p == null);
            var race = RaceManager.Instance;
            bool final = race != null && race.State == RaceState.Results && race.Results.Count == participants.Count;
            if (final)
            {
                participants.Clear();
                participants.AddRange(race.Results);
            }
            else
            {
                comparison ??= Compare;
                participants.Sort(comparison);
            }

            standings.Clear();
            int total = lapManager != null ? lapManager.TotalLaps : RaceRules.DefaultLaps;
            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                p.Position = i + 1;
                standings.Add(new LeaderboardEntry
                {
                    Position = i + 1,
                    Participant = p,
                    Name = p.DisplayName,
                    CurrentLap = p.Finished ? total : Mathf.Min(p.LapsCompleted + 1, total),
                    TotalLaps = total,
                    Status = StatusOf(p, final),
                    TotalTime = p.Finished && !p.Dnf ? p.FinishTime : -1f,
                    BestLap = p.BestLapTime,
                    IsPlayer = p.IsPlayer
                });
            }
        }

        string StatusOf(RaceParticipant p, bool final)
        {
            if (final && p.Dnf) return "DNF";
            if (p.Finished) return "Finished";
            if (!raceStarted) return "On grid";
            if (p.WrongWay) return "Wrong way";
            return "Racing";
        }

        int Compare(RaceParticipant a, RaceParticipant b)
        {
            if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
            if (a.Finished)
                return RaceRanking.Compare(Input(a), Input(b));
            if (!raceStarted) return a.GridSlot.CompareTo(b.GridSlot);
            int byProgress = b.ComputeProgress().CompareTo(a.ComputeProgress());
            return byProgress != 0 ? byProgress : a.GridSlot.CompareTo(b.GridSlot);
        }

        static ResultInput Input(RaceParticipant p) => new ResultInput
        {
            finished = p.Finished, totalTime = p.FinishTime, bestLap = p.BestLapTime, finishOrder = p.FinishOrder,
            lapsCompleted = p.LapsCompleted, progress = 0f
        };
    }
}
