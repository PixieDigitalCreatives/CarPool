using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RacingPrototype
{
    public enum RaceState { Countdown, Racing, Results }

    /// <summary>
    /// Race flow in a route scene: 10-car grid + countdown -> racing -> results. Spawns the player
    /// and the other racers (AI-driven), starts the clock at GO, ends the race when everyone has
    /// finished or the time limit (2 x par) expires, marks unfinished cars DNF, re-checks every
    /// result, ranks by lowest total time (tie-break best lap, then earliest finish) and stores the
    /// results locally for the leaderboard.
    /// </summary>
    public class RaceManager : MonoBehaviour
    {
        public static RaceManager Instance { get; private set; }

        [Header("Scene references")]
        [SerializeField] TrackPath track;
        [SerializeField] RouteInfo route;
        [SerializeField] LapManager lapManager;
        [SerializeField] RaceTimer timer;
        [SerializeField] LeaderboardManager leaderboard;
        [SerializeField] CarSelection carSelection;
        [SerializeField] CameraFollow cameraFollow;
        [SerializeField] RaceAudio raceAudio;
        [Tooltip("Grid slots, pole position first (2 columns x 5 rows).")]
        [SerializeField] Transform[] gridSlots;

        [Header("Defaults when the scene is opened directly")]
        [SerializeField, Range(RaceRules.MinLaps, RaceRules.MaxLaps)] int defaultLaps = RaceRules.DefaultLaps;
        [SerializeField, Range(0, RaceRules.MaxOpponents)] int defaultOpponents = RaceRules.MaxOpponents;
        [SerializeField] int defaultPlayerSlot = 4;
        [SerializeField] float countdownStepSeconds = 1f;

        [Header("AI")]
        [SerializeField] float[] aiSkills = { 0.94f, 0.92f, 0.9f, 0.89f, 0.88f, 0.87f, 0.86f, 0.85f, 0.84f };
        [SerializeField] float autopilotSkill = 0.9f;
        [SerializeField] float coolDownSkill = 0.72f;

        readonly List<RaceParticipant> participants = new List<RaceParticipant>();
        readonly List<RaceParticipant> results = new List<RaceParticipant>();
        RaceSetup setup;
        bool paused;
        float timeLimit;

        public RaceState State { get; private set; } = RaceState.Countdown;
        public RaceSetup Setup => setup;
        public RaceParticipant Player { get; private set; }
        public IReadOnlyList<RaceParticipant> Participants => participants;
        /// <summary>Final ranking (valid finishers by total time, then DNF). Empty until results.</summary>
        public IReadOnlyList<RaceParticipant> Results => results;
        public RaceParticipant Winner => results.Count > 0 && !results[0].Dnf ? results[0] : null;
        public bool IsPaused => paused;
        public bool PlayerAutopilot { get; private set; }
        public int CountdownValue { get; private set; } = -1;
        public float TimeLimit => timeLimit;
        public float TimeRemaining => Mathf.Max(0f, timeLimit - timer.Elapsed);
        public PoolRecord Pool { get; private set; }
        public int ValidatedCount { get; private set; }

        public TrackPath Track => track;
        public RouteInfo Route => route;
        public LapManager Laps => lapManager;
        public RaceTimer Timer => timer;
        public LeaderboardManager Leaderboard => leaderboard;
        public CarSelection Cars => carSelection;
        public RaceAudio Audio => raceAudio;

        public event Action<RaceState> StateChanged;
        public event Action<int> CountdownTick;
        public event Action<RaceParticipant> FirstAcrossLine;
        public event Action PlayerFinishedRace;
        public event Action<bool> PauseChanged;

        /// <summary>Called by the editor scene builder.</summary>
        public void Configure(TrackPath trackPath, RouteInfo routeInfo, LapManager laps, RaceTimer raceTimer, LeaderboardManager board,
            CarSelection selection, CameraFollow cam, RaceAudio audio, Transform[] slots)
        {
            track = trackPath; route = routeInfo; lapManager = laps; timer = raceTimer; leaderboard = board;
            carSelection = selection; cameraFollow = cam; raceAudio = audio; gridSlots = slots;
        }

        void Awake()
        {
            Instance = this;
            AudioListener.pause = false;
            lapManager.ParticipantFinished += OnParticipantFinished;
            lapManager.CheckpointPassed += OnCheckpointPassed;
            lapManager.LapCompleted += OnLapCompleted;
        }

        void OnDestroy()
        {
            if (lapManager != null)
            {
                lapManager.ParticipantFinished -= OnParticipantFinished;
                lapManager.CheckpointPassed -= OnCheckpointPassed;
                lapManager.LapCompleted -= OnLapCompleted;
            }
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            var defaults = RaceSetup.Default;
            defaults.laps = defaultLaps;
            defaults.opponents = defaultOpponents;
            defaults.paintColour = carSelection.Get(0).defaultColour;
            setup = (RaceSession.HasSetup ? RaceSession.Setup : defaults).Normalised(carSelection.Count);
            setup.routeIndex = route != null ? route.RouteIndex : setup.routeIndex;

            if (HasFlag("-demo")) RaceSession.PlayerAutopilot = true;
            if (HasFlag("-fpslog")) gameObject.AddComponent<FpsLogger>();
            PlayerAutopilot = RaceSession.PlayerAutopilot;

            Pool = string.IsNullOrEmpty(setup.poolId) ? null : LocalDataStore.FindPool(setup.poolId);
            StartRace();
        }

        static bool HasFlag(string flag) => Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && State != RaceState.Results) SetPaused(!paused);
            if (Input.GetKeyDown(KeyCode.P) && State == RaceState.Racing && !paused && !Player.Finished) SetPlayerAutopilot(!PlayerAutopilot);
            if (State == RaceState.Racing && timer.Elapsed >= timeLimit) FinalizeRace();
        }

        // ---------------------------------------------------------------- Field

        void StartRace()
        {
            SpawnField();
            lapManager.SetLapCount(setup.laps);
            lapManager.PrepareRace(participants);
            timer.ResetTimer();
            leaderboard.SetParticipants(participants);
            cameraFollow.Follow(Player.transform);
            lapManager.HighlightNextFor(Player);
            float par = route != null ? route.ParLapSeconds : 45f;
            timeLimit = par * setup.laps * RaceRules.TimeLimitParMultiplier;
            if (Pool != null) LocalDataStore.SetStatus(Pool, PoolStatus.Racing);
            StartCoroutine(CountdownRoutine());
        }

        void SpawnField()
        {
            int racers = Mathf.Min(1 + setup.opponents, gridSlots.Length);
            int playerSlot = Mathf.Clamp(RaceSession.HasSetup && RaceSession.PlayerGridSlot >= 0 ? RaceSession.PlayerGridSlot : defaultPlayerSlot, 0, racers - 1);
            string[] names = OpponentNames(racers - 1);
            var palette = carSelection.Catalog.paintPalette;
            int opponent = 0;
            for (int slot = 0; slot < racers; slot++)
            {
                Transform t = gridSlots[slot];
                RaceParticipant p;
                if (slot == playerSlot)
                {
                    p = carSelection.Spawn(setup.carIndex, t.position, t.rotation, setup.paintColour, RaceSession.PlayerName, true);
                    p.Colour = setup.paintColour;
                    Player = p;
                }
                else
                {
                    int model = (setup.carIndex + 1 + opponent) % carSelection.Count;
                    Color paint = palette[(opponent * 3 + 1) % palette.Length];
                    if (ColourDistance(paint, setup.paintColour) < 0.3f) paint = palette[(opponent * 3 + 2) % palette.Length];
                    p = carSelection.Spawn(model, t.position, t.rotation, paint, names[opponent], false);
                    p.Colour = paint;
                    p.AI.Configure(track, aiSkills[opponent % aiSkills.Length], (opponent % 3 - 1) * 2.2f);
                    opponent++;
                }
                p.GridSlot = slot;
                p.RaceNumber = slot + 1;
                p.BindTrack(track);
                participants.Add(p);
            }
        }

        string[] OpponentNames(int count)
        {
            var names = new List<string>();
            if (RaceSession.HasSetup && RaceSession.OpponentNames != null) names.AddRange(RaceSession.OpponentNames);
            foreach (var n in SampleData.PlayerNames)
            {
                if (names.Count >= count) break;
                if (!names.Contains(n)) names.Add(n);
            }
            while (names.Count < count) names.Add("Racer " + (names.Count + 2));
            return names.Take(count).ToArray();
        }

        static float ColourDistance(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        IEnumerator CountdownRoutine()
        {
            SetState(RaceState.Countdown);
            foreach (var p in participants)
            {
                p.Car.ControlsEnabled = false;
                if (p.AI != null) p.AI.Driving = false;
            }

            yield return new WaitForSeconds(0.9f);
            for (int i = 3; i >= 1; i--)
            {
                CountdownValue = i;
                CountdownTick?.Invoke(i);
                if (raceAudio != null) raceAudio.PlayCountdown();
                yield return new WaitForSeconds(countdownStepSeconds);
            }

            CountdownValue = 0;
            CountdownTick?.Invoke(0);
            if (raceAudio != null) raceAudio.PlayGo();
            timer.Begin();
            lapManager.BeginRace();
            leaderboard.MarkRaceStarted();
            foreach (var p in participants)
            {
                p.Car.ControlsEnabled = true;
                if (!p.IsPlayer) p.AI.Driving = true;
            }
            SetState(RaceState.Racing);
            ApplyPlayerControlMode();

            yield return new WaitForSeconds(1f);
            CountdownValue = -1;
            CountdownTick?.Invoke(-1);
        }

        public void SetPlayerAutopilot(bool enabled)
        {
            PlayerAutopilot = enabled;
            RaceSession.PlayerAutopilot = enabled;
            if (State == RaceState.Racing) ApplyPlayerControlMode();
        }

        void ApplyPlayerControlMode()
        {
            if (Player == null) return;
            bool auto = PlayerAutopilot || Player.Finished;
            var input = Player.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = !auto;
            Player.AI.enabled = auto;
            Player.AI.Driving = auto;
            if (auto) Player.AI.Configure(track, Player.Finished ? coolDownSkill : autopilotSkill, 0f);
        }

        void OnCheckpointPassed(RaceParticipant p, int index)
        {
            if (p.IsPlayer) lapManager.HighlightNextFor(p);
        }

        void OnLapCompleted(RaceParticipant p)
        {
            if (p.IsPlayer) lapManager.HighlightNextFor(p);
        }

        void OnParticipantFinished(RaceParticipant p)
        {
            Debug.Log($"[Race] {p.DisplayName} ({p.CarName}) finished #{p.FinishOrder} in {RaceFormat.Time(p.FinishTime)}, best lap {RaceFormat.Time(p.BestLapTime)}");
            if (p.FinishOrder == 1) FirstAcrossLine?.Invoke(p);

            if (p.IsPlayer)
            {
                lapManager.HighlightNextFor(null);
                if (raceAudio != null) raceAudio.PlayFinish();
                ApplyPlayerControlMode();   // cool-down lap on autopilot so the car never blocks the track
                PlayerFinishedRace?.Invoke();
            }
            else
            {
                p.AI.Configure(track, coolDownSkill, p.GridSlot % 2 == 0 ? -2f : 2f);
            }

            if (participants.TrueForAll(x => x.Finished)) FinalizeRace();
        }

        // ---------------------------------------------------------------- Results

        /// <summary>Ends the race: unfinished cars are DNF, results are re-checked, ranked and stored.</summary>
        public void FinalizeRace()
        {
            if (State == RaceState.Results) return;
            timer.Stop();
            lapManager.StopAcceptingCrossings();

            ValidatedCount = 0;
            foreach (var p in participants)
            {
                var report = RaceResultValidator.Validate(p, setup.laps, lapManager.IntermediateCount, track.Length);
                p.ResultValid = report.valid;
                p.ResultNote = report.reason;
                p.Dnf = !report.valid;
                if (report.valid) ValidatedCount++;
                else if (p.Finished) Debug.LogWarning($"[Race] Result flagged for {p.DisplayName}: {report.reason}");
            }

            var inputs = participants.Select(p => new ResultInput
            {
                finished = !p.Dnf,
                totalTime = p.FinishTime,
                bestLap = p.BestLapTime,
                finishOrder = p.Finished ? p.FinishOrder : int.MaxValue,
                lapsCompleted = p.LapsCompleted,
                progress = p.ComputeProgress()
            }).ToList();
            results.Clear();
            foreach (int i in RaceRanking.Order(inputs)) results.Add(participants[i]);
            for (int i = 0; i < results.Count; i++) results[i].FinalRank = i + 1;

            SaveResults();
            Debug.Log($"[Race] Final: winner {(Winner != null ? Winner.DisplayName + " " + RaceFormat.Time(Winner.FinishTime) : "none")}, " +
                      $"{ValidatedCount}/{participants.Count} valid, {participants.Count(p => p.Dnf)} DNF");
            SetState(RaceState.Results);
        }

        void SaveResults()
        {
            string raceId = Guid.NewGuid().ToString("N").Substring(0, 12);
            string now = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            LocalDataStore.AddResults(results.Select(p => new RaceResultRecord
            {
                raceId = raceId,
                poolId = Pool != null ? Pool.id : "",
                date = now,
                route = setup.routeIndex,
                laps = setup.laps,
                player = p.DisplayName,
                car = p.CarName,
                rank = p.FinalRank,
                totalTime = p.Dnf ? -1f : p.FinishTime,
                bestLap = p.BestLapTime,
                dnf = p.Dnf,
                isLocalPlayer = p.IsPlayer
            }));
            if (Pool != null) LocalDataStore.SetStatus(Pool, PoolStatus.Finished);
        }

        // ---------------------------------------------------------------- Pause / navigation

        public void SetPaused(bool value)
        {
            paused = value;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
            PauseChanged?.Invoke(paused);
        }

        /// <summary>Reloads this route with the same setup (a pool race restarts as a fresh quick race).</summary>
        public void RestartRace()
        {
            if (paused) SetPaused(false);
            RaceSession.Setup = setup;
            if (Pool != null && Pool.status == PoolStatus.Finished) RaceSession.Setup.poolId = "";
            RaceSession.HasSetup = true;
            SceneLoader.Load(SceneManager.GetActiveScene().name, route != null ? $"Route {route.RouteNumber} · {route.RouteName}" : null);
        }

        public void QuitToMenu(MenuScreen screen)
        {
            if (paused) SetPaused(false);
            if (State != RaceState.Results && Pool != null)
            {
                // Abandoned pool race: put the pool back to its seat-based status.
                Pool.status = PoolStatus.Open;
                Pool.RefreshSeatStatus();
                LocalDataStore.Save();
            }
            RaceSession.ReturnScreen = screen;
            SceneLoader.Load(SceneLoader.MenuScene, "Main menu");
        }

        void SetState(RaceState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
