using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RacingPrototype
{
    public enum PoolStatus { Open, Full, Racing, Finished }

    /// <summary>A pool created by the admin (PPT slide 11). Entry fee is display-only in this sample.</summary>
    [Serializable]
    public class PoolRecord
    {
        public string id;
        public string name;
        public int entryFee;
        public int laps = RaceRules.DefaultLaps;
        public int route;
        public string startTime;                 // ISO 8601 local time
        public int maxPlayers = RaceRules.MaxRacers;
        public List<string> players = new List<string>();
        public PoolStatus status;
        public bool sample;                      // seeded demonstration data

        public int Joined => players.Count;
        public bool HasSeat => Joined < maxPlayers && status == PoolStatus.Open;

        public DateTime StartDateTime =>
            DateTime.TryParse(startTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTime.Now;

        /// <summary>Open/Full follow the seat count until the race starts.</summary>
        public void RefreshSeatStatus()
        {
            if (status == PoolStatus.Racing || status == PoolStatus.Finished) return;
            status = Joined >= maxPlayers ? PoolStatus.Full : PoolStatus.Open;
        }
    }

    /// <summary>One racer's result in one race.</summary>
    [Serializable]
    public class RaceResultRecord
    {
        public string raceId;
        public string poolId;                    // empty for quick races
        public string date;                      // ISO 8601 local time
        public int route;
        public int laps;
        public string player;
        public string car;
        public int rank;
        public float totalTime = -1f;
        public float bestLap = -1f;
        public bool dnf;
        public bool isLocalPlayer;
        public bool sample;

        public DateTime When => DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTime.MinValue;
    }

    [Serializable]
    class StoreData
    {
        public int version = 1;
        public int nextPoolNumber = 4;
        public List<PoolRecord> pools = new List<PoolRecord>();
        public List<RaceResultRecord> results = new List<RaceResultRecord>();
    }

    /// <summary>
    /// Local JSON storage for pools and race results (Application.persistentDataPath). This replaces
    /// the thegameio backend for the offline sample: nothing is synchronised and no money moves.
    /// First run seeds the Pool 1-3 examples and sample leaderboard rows from the presentation,
    /// all flagged <c>sample</c>.
    /// </summary>
    public static class LocalDataStore
    {
        public const string FileName = "thegameio_racing_sample.json";
        /// <summary>Tests point this at a temporary file.</summary>
        public static string PathOverride;

        static StoreData data;

        public static string FilePath => !string.IsNullOrEmpty(PathOverride) ? PathOverride : Path.Combine(Application.persistentDataPath, FileName);

        static StoreData Data
        {
            get
            {
                if (data == null) Load();
                return data;
            }
        }

        public static IReadOnlyList<PoolRecord> Pools => Data.pools;
        public static IReadOnlyList<RaceResultRecord> Results => Data.results;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            data = null;
            PathOverride = null;
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    data = JsonUtility.FromJson<StoreData>(File.ReadAllText(FilePath));
                    if (data != null && data.pools != null) return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalDataStore] Could not read {FilePath}: {e.Message}. Starting from sample data.");
            }
            data = SampleData.Create();
            Save();
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalDataStore] Could not save {FilePath}: {e.Message}");
            }
        }

        /// <summary>Discards local changes and restores the presentation's sample pools and results.</summary>
        public static void ResetToSample()
        {
            data = SampleData.Create();
            Save();
        }

        public static PoolRecord FindPool(string id) => Data.pools.FirstOrDefault(p => p.id == id);

        public static string NextPoolName => "Pool " + Data.nextPoolNumber;

        /// <summary>Admin action (PPT slide 11): name, entry fee, laps, route, start time; max 10 players.</summary>
        public static PoolRecord CreatePool(string name, int entryFee, int laps, int route, DateTime start)
        {
            var pool = new PoolRecord
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 10),
                name = string.IsNullOrWhiteSpace(name) ? NextPoolName : name.Trim(),
                entryFee = Mathf.Max(0, entryFee),
                laps = RaceRules.ClampLaps(laps),
                route = RaceRules.ClampRoute(route),
                startTime = start.ToString("s", CultureInfo.InvariantCulture),
                maxPlayers = RaceRules.MaxRacers,
                status = PoolStatus.Open
            };
            Data.pools.Add(pool);
            Data.nextPoolNumber++;
            Save();
            return pool;
        }

        /// <summary>Takes the next free seat. Returns false when the pool is full or not open.</summary>
        public static bool Join(PoolRecord pool, string playerName)
        {
            if (pool == null || !pool.HasSeat) return false;
            if (!pool.players.Contains(playerName)) pool.players.Add(playerName);
            pool.RefreshSeatStatus();
            Save();
            return true;
        }

        public static void SetStatus(PoolRecord pool, PoolStatus status)
        {
            if (pool == null) return;
            pool.status = status;
            Save();
        }

        public static void AddResults(IEnumerable<RaceResultRecord> results)
        {
            Data.results.AddRange(results);
            Save();
        }

        /// <summary>Results of the most recent race of a pool, ordered by final rank.</summary>
        public static List<RaceResultRecord> PoolStandings(string poolId)
        {
            var forPool = Data.results.Where(r => r.poolId == poolId).ToList();
            if (forPool.Count == 0) return forPool;
            string lastRace = forPool.OrderBy(r => r.When).Last().raceId;
            return forPool.Where(r => r.raceId == lastRace).OrderBy(r => r.rank).ToList();
        }

        public enum Period { Daily, Weekly, AllTime }

        public struct PlayerTotals
        {
            public string player;
            public int wins, seconds, thirds, races;
            public float bestLap;
            public bool isLocalPlayer, sample;
        }

        /// <summary>
        /// Aggregate ranking for the daily / weekly / all-time views: winner trophies first, then 2nd
        /// and 3rd places, then best single lap, then fewer races (rule from the demo-details doc;
        /// the PPT does not define aggregate ranking).
        /// </summary>
        public static List<PlayerTotals> Aggregate(Period period, DateTime now)
        {
            DateTime from = period == Period.Daily ? now.Date : period == Period.Weekly ? now.Date.AddDays(-6) : DateTime.MinValue;
            return Data.results
                .Where(r => r.When >= from && r.When <= now.AddMinutes(1))
                .GroupBy(r => r.player)
                .Select(g => new PlayerTotals
                {
                    player = g.Key,
                    wins = g.Count(r => !r.dnf && r.rank == 1),
                    seconds = g.Count(r => !r.dnf && r.rank == 2),
                    thirds = g.Count(r => !r.dnf && r.rank == 3),
                    races = g.Count(),
                    bestLap = g.Where(r => r.bestLap > 0f).Select(r => r.bestLap).DefaultIfEmpty(-1f).Min(),
                    isLocalPlayer = g.Any(r => r.isLocalPlayer),
                    sample = g.All(r => r.sample)
                })
                .OrderByDescending(t => t.wins).ThenByDescending(t => t.seconds).ThenByDescending(t => t.thirds)
                .ThenBy(t => t.bestLap > 0f ? t.bestLap : float.MaxValue).ThenBy(t => t.races)
                .ToList();
        }
    }

    /// <summary>Sample pools and leaderboard rows taken from the presentation (slides 11 and 12).</summary>
    public static class SampleData
    {
        public static readonly string[] PlayerNames =
        {
            "RaceKing_21", "NitroNeha", "DriftAce", "SpeedoSam", "TurboTara", "LapHunter",
            "KwameRush", "ZuriDrift", "AmaraApex", "TundeTurbo", "NiaSprint", "KofiLaps", "AdaVelocity", "ChidiGrip"
        };

        internal static StoreData Create()
        {
            var d = new StoreData();
            DateTime now = DateTime.Now;
            PoolRecord Pool(string id, string name, int joined, PoolStatus status, int route, int laps, int fee, DateTime start, int nameOffset)
            {
                var p = new PoolRecord
                {
                    id = id, name = name, entryFee = fee, laps = laps, route = route,
                    startTime = start.ToString("s", CultureInfo.InvariantCulture), status = status, sample = true
                };
                for (int i = 0; i < joined; i++) p.players.Add(PlayerNames[(i + nameOffset) % PlayerNames.Length]);
                return p;
            }
            // Slide 11: Pool 1 Open 7/10, Pool 2 Full 10/10, Pool 3 Racing 10/10.
            d.pools.Add(Pool("pool-1", "Pool 1", 7, PoolStatus.Open, 0, 4, 50, now.AddMinutes(30), 6));
            d.pools.Add(Pool("pool-2", "Pool 2", 10, PoolStatus.Full, 1, 5, 100, now.AddHours(1), 3));
            d.pools.Add(Pool("pool-3", "Pool 3", 10, PoolStatus.Racing, 2, 4, 50, now.AddMinutes(-4), 0));
            d.nextPoolNumber = 4;

            // Slide 12: "Leaderboard - Pool 3" sample rows (6 shown, "all pool players listed").
            string[] lb = { "RaceKing_21", "NitroNeha", "DriftAce", "SpeedoSam", "TurboTara", "LapHunter", "KwameRush", "ZuriDrift", "AmaraApex", "TundeTurbo" };
            float[] best = { 41.08f, 41.55f, 41.90f, 42.31f, 42.88f, 43.40f, 43.95f, 44.30f, 44.82f, 45.60f };
            float[] total = { 125.62f, 126.10f, 127.34f, 128.77f, 130.02f, 131.45f, 133.10f, 134.62f, 136.05f, -1f };
            string[] cars = { "Speedster X", "Turbo Fury", "Turbo Fury", "Speedster X", "Bulldog V8", "thegameio Street", "Trailblazer R", "Turbo Fury", "Trailblazer R", "Bulldog V8" };
            for (int i = 0; i < lb.Length; i++)
                d.results.Add(new RaceResultRecord
                {
                    raceId = "sample-pool-3", poolId = "pool-3", date = now.AddMinutes(-40).ToString("s", CultureInfo.InvariantCulture),
                    route = 2, laps = 3, player = lb[i], car = cars[i], rank = i + 1, bestLap = best[i], totalTime = total[i],
                    dnf = total[i] < 0f, sample = true
                });

            // Earlier sample races so the daily / weekly / all-time views have history.
            var rng = new System.Random(2026);
            for (int race = 0; race < 9; race++)
            {
                DateTime when = now.AddDays(-race * 2 - 1).AddHours(-rng.Next(1, 8));
                var field = PlayerNames.OrderBy(_ => rng.Next()).Take(10).ToList();
                float lapBase = 40f + rng.Next(0, 6);
                for (int i = 0; i < field.Count; i++)
                {
                    float bestLap = lapBase + i * 0.35f + (float)rng.NextDouble() * 0.3f;
                    d.results.Add(new RaceResultRecord
                    {
                        raceId = "sample-race-" + race, poolId = "", date = when.ToString("s", CultureInfo.InvariantCulture),
                        route = race % 4, laps = 4, player = field[i], car = cars[rng.Next(cars.Length)], rank = i + 1,
                        bestLap = bestLap, totalTime = bestLap * 4f + 3f + i * 0.9f, sample = true
                    });
                }
            }
            return d;
        }
    }
}
