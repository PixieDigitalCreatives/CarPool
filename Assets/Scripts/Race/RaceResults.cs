using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RacingPrototype
{
    /// <summary>Inputs to the ranking, kept as plain data so the rules are unit-testable.</summary>
    public struct ResultInput
    {
        public bool finished;      // completed all laps with a valid result
        public float totalTime;
        public float bestLap;
        public int finishOrder;    // 1 = first across the line
        public int lapsCompleted;
        public float progress;     // metres covered (DNF ordering)
    }

    /// <summary>
    /// Final ranking (PPT slide 12): lowest total race time wins; tie-break best single lap, then
    /// earliest finish; unfinished = DNF. DNF rows are listed after finishers by laps completed,
    /// then distance (demo-details doc).
    /// </summary>
    public static class RaceRanking
    {
        const float TimeEpsilon = 0.0005f;

        /// <summary>Indices of <paramref name="inputs"/> in final rank order.</summary>
        public static List<int> Order(IList<ResultInput> inputs)
        {
            var idx = Enumerable.Range(0, inputs.Count).ToList();
            idx.Sort((a, b) => Compare(inputs[a], inputs[b]));
            return idx;
        }

        public static int Compare(ResultInput a, ResultInput b)
        {
            if (a.finished != b.finished) return a.finished ? -1 : 1;
            if (a.finished)
            {
                if (Mathf.Abs(a.totalTime - b.totalTime) > TimeEpsilon) return a.totalTime.CompareTo(b.totalTime);
                float la = a.bestLap > 0f ? a.bestLap : float.MaxValue, lb = b.bestLap > 0f ? b.bestLap : float.MaxValue;
                if (Mathf.Abs(la - lb) > TimeEpsilon) return la.CompareTo(lb);
                return a.finishOrder.CompareTo(b.finishOrder);
            }
            if (a.lapsCompleted != b.lapsCompleted) return b.lapsCompleted.CompareTo(a.lapsCompleted);
            return b.progress.CompareTo(a.progress);
        }
    }

    /// <summary>
    /// Re-checks every finisher before results are final (PPT: "race times re-checked before results
    /// are final"). This offline sample validates locally; it is not a server-side anti-cheat.
    /// Checks: required laps, every lap's checkpoints in order, lap times sum to the total time,
    /// and no lap faster than physically possible for the car.
    /// </summary>
    public static class RaceResultValidator
    {
        public struct Report
        {
            public bool valid;
            public string reason;
        }

        public static Report Validate(RaceParticipant p, int laps, int intermediateCheckpoints, float trackLength)
        {
            if (!p.Finished) return new Report { valid = false, reason = "Did not finish" };
            if (p.LapsCompleted != laps || p.LapTimes.Count != laps)
                return new Report { valid = false, reason = $"Lap count {p.LapsCompleted}/{laps}" };

            // Checkpoint sequence: cp:1..cp:N then lap:k, for every lap.
            var seq = p.EventLog.Where(e => e.StartsWith("cp:") || e.StartsWith("lap:")).ToList();
            int expectedLength = laps * (intermediateCheckpoints + 1);
            if (seq.Count != expectedLength) return new Report { valid = false, reason = "Checkpoint sequence incomplete" };
            int k = 0;
            for (int lap = 1; lap <= laps; lap++)
            {
                for (int cp = 1; cp <= intermediateCheckpoints; cp++)
                    if (seq[k++] != "cp:" + cp) return new Report { valid = false, reason = $"Checkpoint order broken on lap {lap}" };
                if (seq[k++] != "lap:" + lap) return new Report { valid = false, reason = $"Lap {lap} not closed correctly" };
            }

            float sum = p.LapTimes.Sum();
            if (Mathf.Abs(sum - p.FinishTime) > 0.05f) return new Report { valid = false, reason = "Lap times do not add up to the total" };

            float fastestPossibleLap = trackLength / (p.Car.MaxSpeed * 1.15f);
            if (p.LapTimes.Any(t => t < fastestPossibleLap)) return new Report { valid = false, reason = "Impossible lap time" };

            return new Report { valid = true, reason = "OK" };
        }
    }
}
