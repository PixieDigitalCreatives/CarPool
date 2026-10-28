using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RacingPrototype.Tests
{
    static class RaceTestUtil
    {
        public static readonly int[] RouteIndices = { 0, 1, 2, 3 };

        public static RaceSetup Setup(int car = 0, int laps = RaceRules.DefaultLaps, int opponents = RaceRules.MaxOpponents, int route = 0) =>
            new RaceSetup { carIndex = car, paintColour = new Color(1f, 0.71f, 0f), laps = laps, opponents = opponents, routeIndex = route, poolId = "" };

        /// <summary>Isolated local data file per test run so the user's saved pools are untouched.</summary>
        public static void IsolateData()
        {
            string path = Path.Combine(Application.temporaryCachePath, "thegameio_test_data.json");
            if (File.Exists(path)) File.Delete(path);
            LocalDataStore.PathOverride = path;
            LocalDataStore.Load();
        }

        public static string SceneOf(int routeIndex) => RouteCatalog.Current.Get(routeIndex).sceneName;

        /// <summary>Loads a route scene with the given setup (the race starts with the countdown).</summary>
        public static IEnumerator LoadRoute(RaceSetup setup, bool autopilot = false)
        {
            RaceSession.Clear();
            RaceSession.Setup = setup;
            RaceSession.HasSetup = true;
            RaceSession.PlayerAutopilot = autopilot;
            RaceSession.SplashShown = true;
            yield return SceneManager.LoadSceneAsync(SceneOf(setup.routeIndex), LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        public static IEnumerator LoadMenu(bool showSplash = false)
        {
            RaceSession.Clear();
            RaceSession.SplashShown = !showSplash;
            yield return SceneManager.LoadSceneAsync(SceneLoader.MenuScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        public static IEnumerator WaitFor(Func<bool> condition, float gameSeconds)
        {
            float t = 0f;
            while (!condition() && t < gameSeconds)
            {
                t += Time.unscaledDeltaTime * Mathf.Max(1f, Time.timeScale);
                yield return null;
            }
        }

        public static IEnumerator WaitForFixedSeconds(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
            }
        }

        public static IEnumerator WaitUntilRacing() =>
            WaitFor(() => RaceManager.Instance != null && RaceManager.Instance.State == RaceState.Racing, 30f);

        public static IEnumerator WaitForScene(string name) =>
            WaitFor(() => SceneManager.GetActiveScene().name == name && !SceneLoader.IsLoading, 60f);

        public static CarController TakeControl(RaceParticipant player)
        {
            var input = player.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            if (player.AI != null) { player.AI.Driving = false; player.AI.enabled = false; }
            player.Car.ControlsEnabled = true;
            return player.Car;
        }

        public static void Place(RaceParticipant p, float distance, float lateral = 0f, bool backwards = false)
        {
            var path = RaceManager.Instance.Track;
            Vector3 tangent = path.GetTangent(distance);
            Vector3 pos = path.GetPoint(distance) + path.GetRight(distance) * lateral + Vector3.up * 0.05f;
            p.Car.Teleport(pos, Quaternion.LookRotation(backwards ? -tangent : tangent, Vector3.up));
            p.BindTrack(path);
        }

        /// <summary>
        /// Optional screenshot including the overlay UI (set RACING_CAPTURE_DIR). The overlay canvases
        /// are moved to camera space for one frame so URP renders them into the texture.
        /// </summary>
        public static IEnumerator Capture(string name)
        {
            string dir = Environment.GetEnvironmentVariable("RACING_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) yield break;
            Directory.CreateDirectory(dir);
            var cam = Camera.main;
            // Target the capture texture first so camera-space canvases lay out at 1920x1080
            // (batch mode's own screen is 4:3). Note: UI in these captures goes through the camera's
            // post-processing; the player's -capture mode gives the exact on-screen look.
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var changed = new System.Collections.Generic.List<Canvas>();
            foreach (var c in canvases)
            {
                if (c.renderMode != RenderMode.ScreenSpaceOverlay || !c.isActiveAndEnabled || c.sortingOrder >= 1000) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 0.5f;
                changed.Add(c);
            }
            float scale = Time.timeScale;
            Time.timeScale = 0f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            foreach (var c in changed) c.renderMode = RenderMode.ScreenSpaceOverlay;
            cam.targetTexture = previousTarget;
            UnityEngine.Object.Destroy(tex);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            Time.timeScale = scale;
        }
    }
}
