using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// Command-line hooks for demonstrating and checking the built player (inactive in the editor):
    /// "-demo" starts an autopilot quick race from the main menu ("-route N" 1-4, "-laps N" 4-10,
    /// "-car N" in garage order, "-quitafter S" to quit S seconds into the race for timed benchmarks);
    /// "-capture DIR" also walks the menu screens and one full race, saving real screenshots of
    /// each to DIR, then quits.
    /// </summary>
    public class PlayerAutomation : MonoBehaviour
    {
        string captureDir;
        int route;
        int laps = RaceRules.DefaultLaps;
        int car = -1;
        float quitAfter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            string capture = Arg(args, "-capture");
            if (Array.IndexOf(args, "-demo") < 0 && capture == null) return;
            var go = new GameObject("Player Automation");
            DontDestroyOnLoad(go);
            var a = go.AddComponent<PlayerAutomation>();
            a.captureDir = capture;
            if (int.TryParse(Arg(args, "-route"), out int r)) a.route = r - 1;
            if (int.TryParse(Arg(args, "-laps"), out int l)) a.laps = l;
            if (int.TryParse(Arg(args, "-car"), out int c)) a.car = c - 1;
            if (float.TryParse(Arg(args, "-quitafter"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float q)) a.quitAfter = q;
        }

        static string Arg(string[] args, string flag)
        {
            int i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        IEnumerator Start()
        {
            var menu = FindFirstObjectByType<MenuUI>();
            if (menu == null) yield break;   // launched straight into a route scene: RaceManager reads -demo itself
            bool capture = !string.IsNullOrEmpty(captureDir);
            if (capture)
            {
                Directory.CreateDirectory(captureDir);
                yield return Shot("01_splash", 0.8f);
            }
            while (menu.SplashVisible) yield return null;
            if (capture)
            {
                yield return Shot("02_main_menu");
                menu.Show(MenuScreen.Garage);
                menu.SelectCar(car >= 0 ? car : 1);
                menu.SelectColour(2);
                yield return Shot("03_garage");
                menu.Show(MenuScreen.Pools);
                yield return Shot("05_pools");
                menu.Show(MenuScreen.Leaderboard);
                yield return Shot("06_leaderboard");
                menu.Show(MenuScreen.RaceSetup);
            }
            if (!capture && car >= 0) menu.SelectCar(car);
            menu.SelectRoute(route);
            menu.SetLaps(laps);
            if (capture) yield return Shot("04_route_select");

            RaceSession.PlayerAutopilot = true;
            menu.StartQuickRace();
            if (capture) yield return Shot("07_loader", 0.35f);
            RaceManager race;
            while ((race = RaceManager.Instance) == null || SceneLoader.IsLoading) yield return null;
            Debug.Log($"[Automation] Race started: route {race.Setup.routeIndex + 1}, {race.Setup.laps} laps, {race.Participants.Count} cars");
            if (!capture)
            {
                if (quitAfter > 0f)
                {
                    yield return new WaitForSecondsRealtime(quitAfter);
                    Debug.Log($"[Automation] Benchmark done: {QualityTiers.Current} tier, {quitAfter:0} s");
                    Application.Quit();
                }
                Destroy(gameObject);
                yield break;
            }

            while (race.State == RaceState.Countdown && race.CountdownValue != 2) yield return null;   // "2" on screen, grid still parked
            yield return Shot("08_grid_countdown", 0f);
            while (race.State == RaceState.Countdown) yield return null;
            yield return Shot("09_racing_hud", 25f);
            while (race.State != RaceState.Results) yield return null;
            yield return Shot("10_results", 1.5f);
            int rank = 0;
            for (int i = 0; i < race.Results.Count; i++) if (race.Results[i] == race.Player) rank = i + 1;
            Debug.Log($"[Automation] Race finished: winner {(race.Winner != null ? race.Winner.DisplayName : "none")}, player rank {rank}/{race.Results.Count}, validated {race.ValidatedCount}");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }

        IEnumerator Shot(string name, float settle = 0.6f)
        {
            yield return new WaitForSecondsRealtime(settle);
            ScreenCapture.CaptureScreenshot(Path.Combine(captureDir, name + ".png"));
            yield return null;
            yield return null;
            Debug.Log($"[Automation] Captured {name}");
        }
    }
}
