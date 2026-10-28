using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RacingPrototype
{
    /// <summary>
    /// Graphics quality tiers. Each tier is its own URP asset (render scale, shadow cascades and
    /// resolution, soft-shadow quality, SSAO) plus camera anti-aliasing and the heavier post effects
    /// (motion blur, depth of field, high-quality bloom). The tier is chosen in the menu and
    /// remembered per machine, or forced with "-quality Low|High|Ultra"; the first launch picks a
    /// default from the GPU.
    /// </summary>
    public static class QualityTiers
    {
        public enum Tier { Low, High, Ultra }

        const string PrefKey = "thegameio.quality";
        static QualityTierSet set;

        public static Tier Current { get; private set; } = Tier.High;
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            var tier = PlayerPrefs.HasKey(PrefKey) ? (Tier)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey), 0, 2) : DefaultForThisPc();
            string forced = Arg("-quality");
            if (forced != null && Enum.TryParse(forced, true, out Tier t)) tier = t;
            Apply(tier, false);
            SceneManager.sceneLoaded += (_, __) => ApplyToScene();
        }

        /// <summary>
        /// Integrated GPUs (Intel UHD / Iris, or under ~2.5 GB of video memory) start on High, where the
        /// Intel UHD test machine holds a playable frame rate; discrete GPUs start on Ultra.
        /// </summary>
        public static Tier DefaultForThisPc()
        {
            string gpu = SystemInfo.graphicsDeviceName ?? "";
            bool integrated = gpu.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0
                              || gpu.IndexOf("UHD", StringComparison.OrdinalIgnoreCase) >= 0
                              || gpu.IndexOf("Iris", StringComparison.OrdinalIgnoreCase) >= 0
                              || SystemInfo.graphicsMemorySize < 2500;
            return integrated ? Tier.High : Tier.Ultra;
        }

        public static void Set(Tier tier) => Apply(tier, true);

        public static Tier Next(Tier tier) => (Tier)(((int)tier + 1) % 3);

        static void Apply(Tier tier, bool save)
        {
            Current = tier;
            set ??= Resources.Load<QualityTierSet>("QualityTiers");
            if (set != null && set.pipelines != null && (int)tier < set.pipelines.Length && set.pipelines[(int)tier] != null)
                QualitySettings.renderPipeline = set.pipelines[(int)tier];
            QualitySettings.lodBias = tier == Tier.Ultra ? 2f : tier == Tier.High ? 1.3f : 0.8f;
            QualitySettings.anisotropicFiltering = tier == Tier.Low ? AnisotropicFiltering.Enable : AnisotropicFiltering.ForceEnable;
            QualitySettings.globalTextureMipmapLimit = tier == Tier.Low ? 1 : 0;
            // Frame-rate measurements (-fpslog) run uncapped; normal play is v-synced.
            QualitySettings.vSyncCount = Array.IndexOf(Environment.GetCommandLineArgs(), "-fpslog") >= 0 ? 0 : 1;
            if (save)
            {
                PlayerPrefs.SetInt(PrefKey, (int)tier);
                PlayerPrefs.Save();
            }
            ApplyToScene();
            Changed?.Invoke();
        }

        /// <summary>Camera anti-aliasing and per-tier post effects for the cameras and volumes in the open scenes.</summary>
        public static void ApplyToScene()
        {
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (!cam.TryGetComponent<UniversalAdditionalCameraData>(out var data) || !data.renderPostProcessing) continue;
                data.antialiasing = Current == Tier.Low ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
            foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (volume.sharedProfile == null) continue;
                var profile = volume.profile;   // runtime instance: tier changes never touch the asset
                if (profile.TryGet<MotionBlur>(out var blur))
                {
                    blur.active = Current != Tier.Low;
                    blur.quality.value = Current == Tier.Ultra ? MotionBlurQuality.Medium : MotionBlurQuality.Low;
                }
                if (profile.TryGet<DepthOfField>(out var dof))
                {
                    dof.active = Current != Tier.Low;
                    dof.mode.value = Current == Tier.Ultra ? DepthOfFieldMode.Bokeh : DepthOfFieldMode.Gaussian;
                }
                if (profile.TryGet<Bloom>(out var bloom))
                    bloom.highQualityFiltering.value = Current == Tier.Ultra;
            }
        }

        static string Arg(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
