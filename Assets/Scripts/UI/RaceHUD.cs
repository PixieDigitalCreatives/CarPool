using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RacingPrototype
{
    /// <summary>
    /// thegameio-branded race UI: in-race HUD (logo, position, lap/total, race time, best lap,
    /// speed, checkpoint feedback, standings, minimap), pause menu and the victory / results
    /// screen with gold, silver and bronze treatment. Display only - all state comes from
    /// RaceManager / LapManager / LeaderboardManager. Built by <see cref="Build"/> (the editor
    /// builder saves it in the scene; rebuilt at runtime only if missing).
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        const int Rows = RaceRules.MaxRacers;

        [SerializeField] Canvas canvas;

        [Header("Race HUD")]
        [SerializeField] GameObject hudRoot;
        [SerializeField] Text routeText, positionText, lapText, timerText, lapTimesText, limitText, speedText, checkpointText;
        [SerializeField] Text countdownText, toastText, wrongWayText, hintText, autopilotText, finishedText;
        [SerializeField] Image toastBackground, finishedBackground;
        [SerializeField] GameObject standingsPanel;
        [SerializeField] Text[] rowPos, rowName, rowLap, rowTime;
        [SerializeField] Image[] rowBg;

        [Header("Results")]
        [SerializeField] GameObject resultsRoot;
        [SerializeField] Text resultsSubtitle, winnerText, playerResultText, validationText;
        [SerializeField] Text[] podiumName, podiumCar, podiumTime;
        [SerializeField] GameObject[] podiumCards;
        [SerializeField] Text[] resRank, resName, resCar, resBest, resTotal, resStatus;
        [SerializeField] Image[] resRowBg, resTrophy;
        [SerializeField] Button againButton, leaderboardButton, menuButton;

        [Header("Pause")]
        [SerializeField] GameObject pauseRoot;
        [SerializeField] Button resumeButton, restartButton, quitButton;

        static readonly Color Good = new Color(0.3f, 0.86f, 0.48f);
        static readonly Color Bad = new Color(1f, 0.33f, 0.28f);

        RaceManager race;
        float toastTimer;
        float countdownAge = 10f;

        // ================================================================== Runtime

        void Start()
        {
            race = RaceManager.Instance;
            if (canvas == null) Build();
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            if (race == null) return;

            againButton.onClick.AddListener(() => { Click(); race.RestartRace(); });
            leaderboardButton.onClick.AddListener(() => { Click(); race.QuitToMenu(MenuScreen.Leaderboard); });
            menuButton.onClick.AddListener(() => { Click(); race.QuitToMenu(race.Pool != null ? MenuScreen.Pools : MenuScreen.Main); });
            resumeButton.onClick.AddListener(() => { Click(); race.SetPaused(false); });
            restartButton.onClick.AddListener(() => { Click(); race.RestartRace(); });
            quitButton.onClick.AddListener(() => { Click(); race.QuitToMenu(MenuScreen.Main); });

            race.StateChanged += OnStateChanged;
            race.CountdownTick += OnCountdown;
            race.PauseChanged += paused => pauseRoot.SetActive(paused);
            race.FirstAcrossLine += OnFirstAcrossLine;
            race.PlayerFinishedRace += () => ShowToast("FINISHED  -  waiting for the other racers", UIFactory.Accent, 3f);
            race.Laps.CheckpointPassed += OnCheckpointPassed;
            race.Laps.LapCompleted += OnLapCompleted;
            race.Laps.CheckpointMissed += OnCheckpointMissed;
            race.Laps.WrongDirectionCrossing += OnWrongDirection;

            var route = race.Route;
            routeText.text = route != null ? $"ROUTE {route.RouteNumber}  ·  {route.RouteName.ToUpperInvariant()}" : "";
            if (race.Pool != null) routeText.text += $"   <color=#{BrandConfig.Hex(UIFactory.MutedText)}>{race.Pool.name}</color>";
            toastBackground.gameObject.SetActive(false);
            OnStateChanged(race.State);
        }

        void OnDestroy()
        {
            if (race == null) return;
            race.StateChanged -= OnStateChanged;
            race.CountdownTick -= OnCountdown;
            race.FirstAcrossLine -= OnFirstAcrossLine;
            if (race.Laps == null) return;
            race.Laps.CheckpointPassed -= OnCheckpointPassed;
            race.Laps.LapCompleted -= OnLapCompleted;
            race.Laps.CheckpointMissed -= OnCheckpointMissed;
            race.Laps.WrongDirectionCrossing -= OnWrongDirection;
        }

        void Click()
        {
            if (race != null && race.Audio != null) race.Audio.PlayClick();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void OnStateChanged(RaceState state)
        {
            bool results = state == RaceState.Results;
            resultsRoot.SetActive(results);
            // The results / victory screen replaces the in-race HUD (it would otherwise peek out around the panel).
            hudRoot.SetActive(!results);
            standingsPanel.SetActive(!results);
            pauseRoot.SetActive(false);
            if (results)
            {
                toastTimer = 0f;
                toastBackground.gameObject.SetActive(false);
                FillResults();
            }
        }

        void Update()
        {
            if (race == null) return;
            UpdateToast();
            UpdateCountdown();
            UpdateHud();
        }

        void UpdateHud()
        {
            var player = race.Player;
            if (player == null) return;
            var board = race.Leaderboard.Standings;
            int total = race.Laps.TotalLaps;
            string muted = BrandConfig.Hex(UIFactory.MutedText);
            string amber = BrandConfig.Hex(UIFactory.Accent);

            positionText.text = $"{player.Position}<size=40><color=#{muted}> / {board.Count}</color></size>";
            int lap = player.Finished ? total : Mathf.Min(player.LapsCompleted + 1, total);
            lapText.text = player.Finished ? $"FINISHED  <color=#{muted}>{total} laps</color>" : $"LAP  {lap} <color=#{muted}>/ {total}</color>";
            float shown = player.Finished ? player.FinishTime : race.Timer.Elapsed;
            timerText.text = RaceFormat.Time(race.Timer.HasStarted ? shown : 0f);
            lapTimesText.text = $"LAST  {RaceFormat.Time(player.LastLapTime)}     BEST  {RaceFormat.Time(player.BestLapTime)}";
            limitText.text = race.State == RaceState.Racing ? $"Race closes in {RaceFormat.Time(race.TimeRemaining)}" : "";
            speedText.text = Mathf.RoundToInt(player.Car.SpeedKmh).ToString();

            int n = race.Laps.IntermediateCount;
            if (player.Finished) checkpointText.text = $"<color=#{BrandConfig.Hex(Good)}>ALL LAPS COMPLETE</color>";
            else if (player.NextCheckpoint == 0) checkpointText.text = $"ALL {n} CHECKPOINTS  <color=#{amber}>>  FINISH LINE</color>";
            else checkpointText.text = $"NEXT CHECKPOINT  <color=#{amber}>{player.NextCheckpoint}</color> <color=#{muted}>/ {n}</color>";

            bool racing = race.State == RaceState.Racing;
            wrongWayText.gameObject.SetActive(racing && player.WrongWay && !race.PlayerAutopilot);
            hintText.gameObject.SetActive(racing && player.StuckTime > 2f && !race.PlayerAutopilot && !player.Finished);
            autopilotText.gameObject.SetActive(race.PlayerAutopilot && racing && !player.Finished);
            bool waiting = racing && player.Finished;
            finishedBackground.gameObject.SetActive(waiting);
            if (waiting)
                finishedText.text = $"FINISHED  {RaceFormat.Ordinal(player.FinishOrder)} ACROSS THE LINE  ·  {RaceFormat.Time(player.FinishTime)}" +
                                    $"   <color=#{muted}>waiting for other racers - race closes in {RaceFormat.Time(race.TimeRemaining)}</color>";

            for (int i = 0; i < Rows; i++)
            {
                bool used = i < board.Count;
                rowBg[i].gameObject.SetActive(used);
                if (!used) continue;
                var e = board[i];
                rowBg[i].color = e.IsPlayer ? UIFactory.WithAlpha(UIFactory.Accent, 0.3f) : UIFactory.WithAlpha(Color.white, i % 2 == 0 ? 0.05f : 0.02f);
                rowPos[i].text = e.Position.ToString();
                rowName[i].text = e.IsPlayer ? $"<b>{e.Name}</b>" : e.Name;
                rowLap[i].text = e.Status == "Finished" ? $"<color=#{BrandConfig.Hex(Good)}>FIN</color>" : $"{e.CurrentLap}/{e.TotalLaps}";
                rowTime[i].text = e.TotalTime >= 0f ? RaceFormat.Time(e.TotalTime) : e.Status == "Wrong way" ? $"<color=#{BrandConfig.Hex(Bad)}>WRONG</color>" : "";
            }
        }

        void FillResults()
        {
            var results = race.Results;
            var route = race.Route;
            var brand = BrandConfig.Current;
            resultsSubtitle.text = $"{(route != null ? $"Route {route.RouteNumber} · {route.RouteName}" : "")}   ·   {race.Setup.laps} laps   ·   " +
                                   (race.Pool != null ? race.Pool.name : "Quick race");
            var winner = race.Winner;
            winnerText.text = winner != null
                ? $"WINNER  <color=#{BrandConfig.Hex(brand.primary)}>{winner.DisplayName}</color>   {winner.CarName}   ·   {RaceFormat.Time(winner.FinishTime)}"
                : "NO VALID FINISHER";
            var player = race.Player;
            playerResultText.text = player.Dnf
                ? "YOUR RESULT   DNF  -  " + (player.Finished ? player.ResultNote : "race closed before you finished")
                : $"YOUR RESULT   {RaceFormat.Ordinal(player.FinalRank)} of {results.Count}   ·   Total {RaceFormat.Time(player.FinishTime)}   ·   Best lap {RaceFormat.Time(player.BestLapTime)}";
            validationText.text = $"Results re-checked locally before finalising: {race.ValidatedCount}/{results.Count} valid " +
                                  "(laps, checkpoint order, lap-time totals). Server-side validation and thegameio sync are not part of this offline sample.";

            for (int i = 0; i < 3; i++)
            {
                bool has = i < results.Count && !results[i].Dnf;
                podiumCards[i].SetActive(has);
                if (!has) continue;
                podiumName[i].text = results[i].DisplayName;
                podiumCar[i].text = results[i].CarName;
                podiumTime[i].text = $"{RaceFormat.Time(results[i].FinishTime)}   <size=18><color=#{BrandConfig.Hex(UIFactory.MutedText)}>best {RaceFormat.Time(results[i].BestLapTime)}</color></size>";
            }

            for (int i = 0; i < Rows; i++)
            {
                bool used = i < results.Count;
                resRowBg[i].gameObject.SetActive(used);
                if (!used) continue;
                var p = results[i];
                resRowBg[i].color = p.IsPlayer ? UIFactory.WithAlpha(brand.primary, 0.22f) : UIFactory.WithAlpha(Color.white, i % 2 == 0 ? 0.05f : 0.02f);
                bool podium = !p.Dnf && p.FinalRank <= 3;
                resTrophy[i].gameObject.SetActive(podium);
                if (podium) resTrophy[i].color = UIFactory.TrophyColour(p.FinalRank);
                resRank[i].text = podium ? "" : p.Dnf ? "-" : p.FinalRank.ToString();
                resName[i].text = p.IsPlayer ? $"<b>{p.DisplayName}</b>" : p.DisplayName + $" <color=#{BrandConfig.Hex(UIFactory.MutedText)}><size=15>AI</size></color>";
                resCar[i].text = p.CarName;
                resBest[i].text = RaceFormat.Time(p.BestLapTime);
                resTotal[i].text = p.Dnf ? "DNF" : RaceFormat.Time(p.FinishTime);
                resTotal[i].color = p.Dnf ? Bad : !p.Dnf && p.FinalRank == 1 ? brand.primary : UIFactory.TextColour;
                resStatus[i].text = p.Dnf ? (p.Finished ? "Flagged" : "DNF") : "Finished";
                resStatus[i].color = p.Dnf ? Bad : Good;
            }
        }

        // ------------------------------------------------------------------ Feedback

        void OnCountdown(int value)
        {
            countdownAge = 0f;
            countdownText.text = value > 0 ? value.ToString() : value == 0 ? "GO!" : "";
            countdownText.color = value == 0 ? Good : UIFactory.Accent;
        }

        void UpdateCountdown()
        {
            countdownAge += Time.unscaledDeltaTime;
            bool show = race.CountdownValue >= 0;
            countdownText.gameObject.SetActive(show);
            if (!show) return;
            countdownText.rectTransform.localScale = Vector3.one * (1f + 0.45f * Mathf.Exp(-countdownAge * 7f));
        }

        void OnCheckpointPassed(RaceParticipant p, int index)
        {
            if (!p.IsPlayer) return;
            ShowToast($"CHECKPOINT  {index} / {race.Laps.IntermediateCount}", Good, 1.6f);
            if (race.Audio != null) race.Audio.PlayCheckpoint();
        }

        void OnLapCompleted(RaceParticipant p)
        {
            if (!p.IsPlayer) return;
            int total = race.Laps.TotalLaps;
            string suffix = p.LapsCompleted == total - 1 ? "   ·   FINAL LAP" : "";
            ShowToast($"LAP {p.LapsCompleted} / {total}   {RaceFormat.Time(p.LastLapTime)}{suffix}", UIFactory.Accent, 2.4f);
            if (race.Audio != null) race.Audio.PlayLap();
        }

        void OnCheckpointMissed(RaceParticipant p, int expected)
        {
            if (!p.IsPlayer) return;
            ShowToast($"MISSED CHECKPOINT {expected}  -  this lap will not count until you cross it", Bad, 2.4f);
            if (race.Audio != null) race.Audio.PlayWarning();
        }

        void OnWrongDirection(RaceParticipant p, int index)
        {
            if (!p.IsPlayer) return;
            string what = index == 0 ? "FINISH LINE" : $"CHECKPOINT {index}";
            ShowToast($"{what} CROSSED THE WRONG WAY  -  not counted", Bad, 2.4f);
            if (race.Audio != null) race.Audio.PlayWarning();
        }

        void OnFirstAcrossLine(RaceParticipant p)
        {
            if (p.IsPlayer) return;
            ShowToast($"{p.DisplayName.ToUpperInvariant()} TAKES THE CHEQUERED FLAG   {RaceFormat.Time(p.FinishTime)}", UIFactory.Accent, 3f);
        }

        public void ShowToast(string message, Color colour, float seconds)
        {
            toastText.text = message;
            toastText.color = colour;
            toastTimer = seconds;
            toastBackground.gameObject.SetActive(true);
        }

        void UpdateToast()
        {
            if (toastTimer <= 0f) return;
            toastTimer -= Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(toastTimer / 0.35f);
            var c = toastText.color; c.a = alpha; toastText.color = c;
            var b = toastBackground.color; b.a = 0.8f * alpha; toastBackground.color = b;
            if (toastTimer <= 0f) toastBackground.gameObject.SetActive(false);
        }

        // ================================================================== Construction

        /// <summary>Creates the whole canvas hierarchy. Safe to call again; it replaces the previous one.</summary>
        public void Build()
        {
            if (canvas != null)
            {
                if (Application.isPlaying) Destroy(canvas.gameObject);
                else DestroyImmediate(canvas.gameObject);
            }
            var canvasGo = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;   // never smaller than 1920x1080 units: fixed layouts fit 16:10 and 4:3 too

            BuildHud(canvasGo.transform);
            BuildResults(canvasGo.transform);
            BuildPause(canvasGo.transform);
        }

        static Image Box(string name, Transform parent, float alpha = 0.86f) => UIFactory.Card(name, parent, UIFactory.WithAlpha(BrandConfig.Current.background, alpha));

        void BuildHud(Transform root)
        {
            var brand = BrandConfig.Current;
            hudRoot = UIFactory.Rect("Race HUD", root).Stretch().gameObject;
            var hud = hudRoot.transform;
            Color text = brand.text, muted = brand.mutedText;

            // Brand + position (top left)
            var posPanel = Box("Position Panel", hud).rectTransform.TopLeft(30, 30, 340, 196);
            var wordmark = UIFactory.Wordmark(posPanel, 34);
            wordmark.rectTransform.TopLeft(22, 12, 220, 40);
            if (wordmark is Text wm) wm.alignment = TextAnchor.MiddleLeft;
            routeText = UIFactory.Label("Route", posPanel, "ROUTE", 14, TextAnchor.MiddleLeft, muted, FontStyle.Bold);
            routeText.rectTransform.TopLeft(22, 50, 310, 20);
            UIFactory.Label("Caption", posPanel, "POSITION", 16, TextAnchor.MiddleLeft, muted, FontStyle.Bold).rectTransform.TopLeft(22, 78, 200, 20);
            positionText = UIFactory.Label("Position", posPanel, "1 / 10", 70, TextAnchor.MiddleLeft, text, FontStyle.Bold, true);
            positionText.rectTransform.TopLeft(20, 94, 310, 66);
            lapText = UIFactory.Label("Lap", posPanel, "LAP 1 / 4", 26, TextAnchor.MiddleLeft, text, FontStyle.Bold);
            lapText.rectTransform.TopLeft(22, 158, 310, 30);

            // Timer (top centre)
            var timePanel = Box("Timer Panel", hud).rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(440, 138));
            UIFactory.Label("Caption", timePanel, "RACE TIME", 16, TextAnchor.UpperCenter, muted, FontStyle.Bold).rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(400, 22));
            timerText = UIFactory.Label("Timer", timePanel, "0:00.00", 54, TextAnchor.MiddleCenter, text, FontStyle.Bold, true);
            timerText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(420, 58));
            lapTimesText = UIFactory.Label("Lap Times", timePanel, "LAST -:--.--     BEST -:--.--", 17, TextAnchor.MiddleCenter, muted);
            lapTimesText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -92), new Vector2(420, 22));
            limitText = UIFactory.Label("Limit", timePanel, "", 13, TextAnchor.MiddleCenter, muted);
            limitText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -114), new Vector2(420, 18));

            // Standings (top right)
            const float rowH = 29f;
            var board = Box("Standings", hud).rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -30), new Vector2(420, 58 + Rows * (rowH + 2)));
            standingsPanel = board.gameObject;
            UIFactory.Label("Title", board, "STANDINGS", 16, TextAnchor.MiddleLeft, brand.primary, FontStyle.Bold).rectTransform.TopLeft(18, 12, 200, 24);
            UIFactory.Label("Hint", board, "LAP / TIME", 13, TextAnchor.MiddleRight, muted, FontStyle.Bold).rectTransform.TopLeft(200, 12, 200, 24);
            rowPos = new Text[Rows]; rowName = new Text[Rows]; rowLap = new Text[Rows]; rowTime = new Text[Rows]; rowBg = new Image[Rows];
            for (int r = 0; r < Rows; r++)
            {
                rowBg[r] = UIFactory.Card("Row " + (r + 1), board, UIFactory.WithAlpha(Color.white, 0.04f));
                rowBg[r].rectTransform.TopLeft(10, 46 + r * (rowH + 2), 400, rowH);
                var row = rowBg[r].transform;
                rowPos[r] = UIFactory.Label("Pos", row, "1", 16, TextAnchor.MiddleCenter, text, FontStyle.Bold); rowPos[r].rectTransform.TopLeft(4, 0, 30, rowH);
                rowName[r] = UIFactory.Label("Name", row, "Driver", 16, TextAnchor.MiddleLeft, text); rowName[r].rectTransform.TopLeft(42, 0, 210, rowH);
                rowLap[r] = UIFactory.Label("Lap", row, "1/4", 15, TextAnchor.MiddleRight, muted); rowLap[r].rectTransform.TopLeft(250, 0, 50, rowH);
                rowTime[r] = UIFactory.Label("Time", row, "", 15, TextAnchor.MiddleRight, text); rowTime[r].rectTransform.TopLeft(304, 0, 86, rowH);
            }

            // Minimap + speed (bottom right)
            var mapPanel = Box("Minimap Panel", hud).rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 30), new Vector2(300, 300));
            Minimap.Create(mapPanel);
            var speedPanel = Box("Speed Panel", hud).rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-346, 30), new Vector2(250, 120));
            speedText = UIFactory.Label("Speed", speedPanel, "0", 72, TextAnchor.MiddleRight, text, FontStyle.Bold, true);
            speedText.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-92, 2), new Vector2(170, 92));
            UIFactory.Label("Unit", speedPanel, "km/h", 22, TextAnchor.MiddleLeft, muted, FontStyle.Bold).rectTransform.Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, -14), new Vector2(72, 36));

            // Checkpoint + controls (bottom left)
            var infoPanel = Box("Info Panel", hud).rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(30, 30), new Vector2(700, 100));
            checkpointText = UIFactory.Label("Checkpoint", infoPanel, "NEXT CHECKPOINT 1 / 4", 26, TextAnchor.MiddleLeft, text, FontStyle.Bold);
            checkpointText.rectTransform.TopLeft(20, 10, 660, 38);
            UIFactory.Label("Controls", infoPanel, "W / Up accelerate    S / Down brake, reverse    A D / Left Right steer\nR reset to the road    P autopilot (demo)    Esc pause", 16, TextAnchor.UpperLeft, muted)
                .rectTransform.TopLeft(20, 52, 660, 44);

            // Centre messages
            countdownText = UIFactory.Label("Countdown", hud, "3", 220, TextAnchor.MiddleCenter, brand.primary, FontStyle.Bold, true);
            countdownText.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(700, 300));
            countdownText.gameObject.SetActive(false);

            toastBackground = Box("Toast", hud, 0.8f);
            toastBackground.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -400), new Vector2(1000, 60));
            toastText = UIFactory.Label("Text", toastBackground.transform, "", 28, TextAnchor.MiddleCenter, text, FontStyle.Bold);
            toastText.rectTransform.Stretch();

            finishedBackground = Box("Finished Banner", hud, 0.85f);
            finishedBackground.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-120, 150), new Vector2(1100, 54));
            finishedText = UIFactory.Label("Text", finishedBackground.transform, "", 20, TextAnchor.MiddleCenter, brand.primary, FontStyle.Bold);
            finishedText.rectTransform.Stretch(12, 12, 0, 0);
            finishedBackground.gameObject.SetActive(false);

            wrongWayText = UIFactory.Label("Wrong Way", hud, "WRONG WAY  -  TURN AROUND", 56, TextAnchor.MiddleCenter, Bad, FontStyle.Bold, true);
            wrongWayText.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1200, 80));
            wrongWayText.gameObject.SetActive(false);

            hintText = UIFactory.Label("Stuck Hint", hud, "Stuck?  Press  R  to return to the road", 28, TextAnchor.MiddleCenter, brand.primary, FontStyle.Bold, true);
            hintText.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -170), new Vector2(1000, 46));
            hintText.gameObject.SetActive(false);

            autopilotText = UIFactory.Label("Autopilot", hud, "AUTOPILOT ON  -  press P to drive", 20, TextAnchor.MiddleCenter, Good, FontStyle.Bold, true);
            autopilotText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -176), new Vector2(600, 30));
            autopilotText.gameObject.SetActive(false);
        }

        void BuildResults(Transform root)
        {
            var brand = BrandConfig.Current;
            resultsRoot = UIFactory.Panel("Results", root, UIFactory.WithAlpha(brand.background, 0.62f)).gameObject;
            ((RectTransform)resultsRoot.transform).Stretch();
            resultsRoot.GetComponent<Image>().raycastTarget = true;
            var panel = UIFactory.Card("Results Panel", resultsRoot.transform, UIFactory.WithAlpha(brand.background, 1f)).rectTransform
                .Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1440, 960));

            var wm = UIFactory.Wordmark(panel, 30);
            wm.rectTransform.TopLeft(40, 26, 200, 36);
            if (wm is Text wt) wt.alignment = TextAnchor.MiddleLeft;
            UIFactory.Label("Title", panel, "RACE RESULTS", 46, TextAnchor.MiddleCenter, brand.text, FontStyle.Bold).rectTransform
                .Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(900, 56));
            resultsSubtitle = UIFactory.Label("Subtitle", panel, "", 19, TextAnchor.MiddleCenter, brand.mutedText);
            resultsSubtitle.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(1200, 26));

            // Podium: 2nd - 1st - 3rd
            podiumCards = new GameObject[3]; podiumName = new Text[3]; podiumCar = new Text[3]; podiumTime = new Text[3];
            float[] px = { 0f, -390f, 390f };
            float[] py = { -120f, -150f, -150f };
            for (int i = 0; i < 3; i++)
            {
                var card = UIFactory.Card("Podium " + (i + 1), panel, brand.panel).rectTransform
                    .Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(px[i], py[i]), new Vector2(360, i == 0 ? 200 : 170));
                podiumCards[i] = card.gameObject;
                var disc = UIFactory.TrophyDisc(card, i + 1, i == 0 ? 78 : 64);
                disc.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 8), disc.rectTransform.sizeDelta);
                float tx = i == 0 ? 116 : 102;
                UIFactory.Label("Place", card, RaceFormat.Ordinal(i + 1).ToUpperInvariant(), 15, TextAnchor.MiddleLeft, UIFactory.TrophyColour(i + 1), FontStyle.Bold).rectTransform.TopLeft(tx, 26, 230, 20);
                podiumName[i] = UIFactory.Label("Name", card, "Player", i == 0 ? 30 : 26, TextAnchor.MiddleLeft, brand.text, FontStyle.Bold);
                podiumName[i].rectTransform.TopLeft(tx, 48, 240, 40);
                podiumCar[i] = UIFactory.Label("Car", card, "Car", 17, TextAnchor.MiddleLeft, brand.mutedText);
                podiumCar[i].rectTransform.TopLeft(tx, 88, 240, 24);
                podiumTime[i] = UIFactory.Label("Time", card, "0:00.00", i == 0 ? 30 : 24, TextAnchor.MiddleLeft, i == 0 ? brand.primary : brand.text, FontStyle.Bold);
                podiumTime[i].rectTransform.TopLeft(tx, 116, 250, 36);
            }

            winnerText = UIFactory.Label("Winner", panel, "", 24, TextAnchor.MiddleCenter, brand.text, FontStyle.Bold);
            winnerText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -336), new Vector2(1300, 32));
            playerResultText = UIFactory.Label("Player", panel, "", 19, TextAnchor.MiddleCenter, brand.mutedText);
            playerResultText.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -370), new Vector2(1300, 26));

            // Full table
            float[] x = { 20f, 90f, 420f, 650f, 830f, 1010f };
            float[] w = { 60f, 320f, 220f, 170f, 170f, 150f };
            string[] heads = { "RANK", "PLAYER", "CAR", "BEST LAP", "TOTAL TIME", "STATUS" };
            var table = UIFactory.Rect("Table", panel).Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -408), new Vector2(1200, 420));
            for (int c = 0; c < heads.Length; c++)
                UIFactory.Label("Head " + heads[c], table, heads[c], 14, TextAnchor.MiddleLeft, brand.mutedText, FontStyle.Bold).rectTransform.TopLeft(x[c] + 6, 0, w[c], 22);
            resRank = new Text[Rows]; resName = new Text[Rows]; resCar = new Text[Rows]; resBest = new Text[Rows]; resTotal = new Text[Rows]; resStatus = new Text[Rows];
            resRowBg = new Image[Rows]; resTrophy = new Image[Rows];
            const float rh = 36f;
            for (int r = 0; r < Rows; r++)
            {
                resRowBg[r] = UIFactory.Card("Row " + (r + 1), table, UIFactory.WithAlpha(Color.white, 0.04f));
                resRowBg[r].rectTransform.TopLeft(0, 26 + r * (rh + 3), 1200, rh);
                var row = resRowBg[r].transform;
                resTrophy[r] = UIFactory.TrophyDisc(row, 1, 28);
                resTrophy[r].rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(28, 28));
                Text Cell(int c, string content, FontStyle style = FontStyle.Normal)
                {
                    var t = UIFactory.Label(heads[c], row, content, 18, TextAnchor.MiddleLeft, brand.text, style);
                    t.rectTransform.TopLeft(x[c], 0, w[c], rh);
                    return t;
                }
                resRank[r] = Cell(0, "1", FontStyle.Bold);
                resRank[r].alignment = TextAnchor.MiddleCenter;
                resName[r] = Cell(1, "Player");
                resCar[r] = Cell(2, "Car");
                resCar[r].color = brand.mutedText;
                resBest[r] = Cell(3, "0:00.00");
                resTotal[r] = Cell(4, "0:00.00", FontStyle.Bold);
                resStatus[r] = Cell(5, "Finished");
            }

            validationText = UIFactory.Paragraph("Validation", panel, "", 14, brand.mutedText);
            validationText.alignment = TextAnchor.MiddleCenter;
            validationText.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 112), new Vector2(1300, 40));

            Vector2 bottom = new Vector2(0.5f, 0);
            againButton = UIFactory.Button("Race Again", panel, "RACE AGAIN", 24, UIFactory.ButtonStyle.Primary);
            ((RectTransform)againButton.transform).Place(bottom, bottom, new Vector2(-370, 36), new Vector2(340, 64));
            leaderboardButton = UIFactory.Button("Leaderboard", panel, "LEADERBOARD", 24);
            ((RectTransform)leaderboardButton.transform).Place(bottom, bottom, new Vector2(0, 36), new Vector2(340, 64));
            menuButton = UIFactory.Button("Menu", panel, "MAIN MENU", 24);
            ((RectTransform)menuButton.transform).Place(bottom, bottom, new Vector2(370, 36), new Vector2(340, 64));
            resultsRoot.SetActive(false);
        }

        void BuildPause(Transform root)
        {
            var brand = BrandConfig.Current;
            pauseRoot = UIFactory.Panel("Pause", root, UIFactory.WithAlpha(brand.background, 0.6f)).gameObject;
            ((RectTransform)pauseRoot.transform).Stretch();
            pauseRoot.GetComponent<Image>().raycastTarget = true;
            var panel = UIFactory.Card("Pause Panel", pauseRoot.transform, UIFactory.WithAlpha(brand.background, 0.97f)).rectTransform
                .Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 430));
            Vector2 top = new Vector2(0.5f, 1);
            UIFactory.Wordmark(panel, 28).rectTransform.Place(top, top, new Vector2(0, -24), new Vector2(300, 34));
            UIFactory.Label("Title", panel, "PAUSED", 44, TextAnchor.MiddleCenter, brand.text, FontStyle.Bold).rectTransform.Place(top, top, new Vector2(0, -62), new Vector2(480, 56));
            resumeButton = UIFactory.Button("Resume", panel, "RESUME", 24, UIFactory.ButtonStyle.Primary);
            ((RectTransform)resumeButton.transform).Place(top, top, new Vector2(0, -144), new Vector2(420, 62));
            restartButton = UIFactory.Button("Restart", panel, "RESTART RACE", 24);
            ((RectTransform)restartButton.transform).Place(top, top, new Vector2(0, -224), new Vector2(420, 62));
            quitButton = UIFactory.Button("Quit", panel, "MAIN MENU", 24);
            ((RectTransform)quitButton.transform).Place(top, top, new Vector2(0, -304), new Vector2(420, 62));
            pauseRoot.SetActive(false);
        }
    }
}
