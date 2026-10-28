using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RacingPrototype
{
    /// <summary>
    /// Branded loading screen (thegameio loader touchpoint) shown while a scene loads
    /// asynchronously. Survives the scene change and fades out once the new scene is ready.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        public const string MenuScene = "MainMenu";

        static SceneLoader instance;

        Canvas canvas;
        CanvasGroup group;
        Image bar;
        Text caption;
        Text tip;
        bool busy;

        public static bool IsLoading => instance != null && instance.busy;

        static readonly string[] Tips =
        {
            "Cross every checkpoint in order - a missed checkpoint means the lap does not count.",
            "The lowest total time over all laps wins. Best single lap breaks a tie.",
            "Pools hold up to 10 racers. The admin sets laps, route and start time.",
            "Press R to return to the road if you get stuck against a barrier."
        };

        public static void Load(string sceneName, string title = null)
        {
            if (instance == null)
            {
                var go = new GameObject("thegameio Loader");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<SceneLoader>();
                instance.Build();
            }
            if (instance.busy) return;
            instance.StartCoroutine(instance.LoadRoutine(sceneName, title));
        }

        void Build()
        {
            var brand = BrandConfig.Current;
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;   // never smaller than 1920x1080 units: fixed layouts fit 16:10 and 4:3 too
            gameObject.AddComponent<GraphicRaycaster>();
            group = gameObject.AddComponent<CanvasGroup>();

            var root = transform;
            UIFactory.Panel("Background", root, brand.background).rectTransform.Stretch();
            UIFactory.Wordmark(root, 96).rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(1200, 130));
            caption = UIFactory.Label("Caption", root, "Loading", 30, TextAnchor.MiddleCenter, brand.text, FontStyle.Bold);
            caption.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(1200, 44));
            var track = UIFactory.Panel("Bar Track", root, brand.panelLight).rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(640, 8));
            bar = UIFactory.Panel("Bar", track, brand.primary);
            bar.rectTransform.anchorMin = Vector2.zero;
            bar.rectTransform.anchorMax = new Vector2(0f, 1f);
            bar.rectTransform.offsetMin = bar.rectTransform.offsetMax = Vector2.zero;
            tip = UIFactory.Label("Tip", root, "", 20, TextAnchor.MiddleCenter, brand.mutedText);
            tip.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 80), new Vector2(1400, 30));
            group.alpha = 0f;
            canvas.enabled = false;
        }

        IEnumerator LoadRoutine(string sceneName, string title)
        {
            busy = true;
            canvas.enabled = true;
            caption.text = string.IsNullOrEmpty(title) ? "Loading" : title;
            tip.text = Tips[Random.Range(0, Tips.Length)];
            SetProgress(0f);
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.2f) { group.alpha = t; yield return null; }
            group.alpha = 1f;
            Time.timeScale = 1f;
            AudioListener.pause = false;

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (op == null)
            {
                Debug.LogError($"[SceneLoader] Scene '{sceneName}' is not in Build Settings.");
                busy = false;
                canvas.enabled = false;
                yield break;
            }
            float shown = 0f, started = Time.unscaledTime;
            while (!op.isDone)
            {
                shown = Mathf.MoveTowards(shown, Mathf.Clamp01(op.progress / 0.9f), Time.unscaledDeltaTime * 2.5f);
                SetProgress(shown);
                yield return null;
            }
            // Keep the brand visible for a moment so the loader never flashes.
            while (Time.unscaledTime - started < 0.6f) { SetProgress(Mathf.MoveTowards(shown, 1f, Time.unscaledDeltaTime * 3f)); shown = bar.rectTransform.anchorMax.x; yield return null; }
            SetProgress(1f);
            yield return null;
            for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.3f) { group.alpha = t; yield return null; }
            group.alpha = 0f;
            canvas.enabled = false;
            busy = false;
        }

        void SetProgress(float p) => bar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(p), 1f);
    }
}
