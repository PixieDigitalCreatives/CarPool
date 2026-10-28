using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RacingPrototype.EditorTools
{
    /// <summary>
    /// Creates the URP pipeline assets, one per quality tier (Forward+ so the many street lights of
    /// the night city are not limited per object; HDR; SSAO on High and Ultra), switches the project
    /// to Linear colour space and builds the post-processing profiles (ACES tonemapping, night-tuned
    /// bloom, camera motion blur, optional depth of field). QualityTiers picks the tier at runtime.
    /// </summary>
    public static class UrpSetup
    {
        public const string Folder = "Assets/Settings/URP";

        class TierSpec
        {
            public string name;
            public float renderScale, shadowDistance;
            public int cascades, shadowResolution, softShadowQuality;   // 1 Low, 2 Medium, 3 High
            public bool ssao, ssaoDownsample, boxProjection;
            public float ssaoIntensity, ssaoRadius;
            public int ssaoSamples;                                       // 0 High (12), 1 Medium (8), 2 Low (4)
        }

        // Indexed by QualityTiers.Tier (Low, High, Ultra).
        static readonly TierSpec[] Tiers =
        {
            new TierSpec { name = "Low", renderScale = 0.85f, shadowDistance = 80f, cascades = 2, shadowResolution = 1024, softShadowQuality = 1 },
            new TierSpec { name = "High", renderScale = 1f, shadowDistance = 120f, cascades = 4, shadowResolution = 2048, softShadowQuality = 2,
                           ssao = true, ssaoDownsample = true, ssaoIntensity = 1.4f, ssaoRadius = 0.25f, ssaoSamples = 2, boxProjection = true },
            new TierSpec { name = "Ultra", renderScale = 1f, shadowDistance = 160f, cascades = 4, shadowResolution = 4096, softShadowQuality = 3,
                           ssao = true, ssaoDownsample = false, ssaoIntensity = 1.6f, ssaoRadius = 0.3f, ssaoSamples = 1, boxProjection = true },
        };

        /// <summary>Builds every tier's URP asset, registers them for QualityTiers and makes High the project default.</summary>
        public static UniversalRenderPipelineAsset EnsurePipeline()
        {
            ProceduralAssets.EnsureFolder(Folder);
            var assets = Tiers.Select(BuildTier).ToArray();

            ProceduralAssets.EnsureFolder("Assets/Resources");
            const string setPath = "Assets/Resources/QualityTiers.asset";
            var set = AssetDatabase.LoadAssetAtPath<QualityTierSet>(setPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<QualityTierSet>();
                AssetDatabase.CreateAsset(set, setPath);
            }
            set.pipelines = assets.Cast<RenderPipelineAsset>().ToArray();
            EditorUtility.SetDirty(set);

            var high = assets[(int)QualityTiers.Tier.High];
            GraphicsSettings.defaultRenderPipeline = high;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = null;   // every level uses the default; QualityTiers overrides at runtime
            }
            QualitySettings.SetQualityLevel(current, false);

            // The single pre-tier pipeline asset is replaced by the tier assets.
            foreach (var old in new[] { $"{Folder}/RacingURP.asset", $"{Folder}/RacingRenderer.asset" })
                if (AssetDatabase.LoadMainAssetAtPath(old) != null) AssetDatabase.DeleteAsset(old);

            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            return high;
        }

        static UniversalRenderPipelineAsset BuildTier(TierSpec t)
        {
            string rendererPath = $"{Folder}/RacingRenderer_{t.name}.asset";
            string assetPath = $"{Folder}/RacingURP_{t.name}.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            renderer.renderingMode = RenderingMode.ForwardPlus;
            if (renderer.postProcessData == null)
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            ConfigureSsao(renderer, t);
            EditorUtility.SetDirty(renderer);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            asset.supportsHDR = true;
            asset.msaaSampleCount = 1;          // SMAA / FXAA on the camera instead (cheaper on integrated GPUs)
            asset.renderScale = t.renderScale;
            asset.shadowDistance = t.shadowDistance;
            asset.shadowCascadeCount = t.cascades;
            if (t.cascades == 4) asset.cascade4Split = new Vector3(0.05f, 0.16f, 0.4f);
            asset.mainLightShadowmapResolution = t.shadowResolution;
            asset.useSRPBatcher = true;
            asset.supportsCameraDepthTexture = t.ssao;   // SSAO and depth of field read depth
            var so = new SerializedObject(asset);
            Set(so, "m_MainLightShadowsSupported", true);
            Set(so, "m_SoftShadowsSupported", true);
            Set(so, "m_SoftShadowQuality", t.softShadowQuality);
            Set(so, "m_AdditionalLightsRenderingMode", 1);          // per pixel
            Set(so, "m_AdditionalLightShadowsSupported", false);    // street lights cast no shadows (cost)
            Set(so, "m_SupportsLightCookies", true);                 // headlight beams
            Set(so, "m_SupportsDynamicBatching", false);
            Set(so, "m_ReflectionProbeBlending", true);
            Set(so, "m_ReflectionProbeBoxProjection", t.boxProjection);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>Adds, tunes or removes the SSAO renderer feature for a tier.</summary>
        static void ConfigureSsao(UniversalRendererData renderer, TierSpec t)
        {
            var ssao = renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
            if (!t.ssao)
            {
                if (ssao != null)
                {
                    renderer.rendererFeatures.Remove(ssao);
                    Object.DestroyImmediate(ssao, true);
                }
                SyncFeatureMap(renderer);
                return;
            }
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "SSAO";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                renderer.rendererFeatures.Add(ssao);
            }
            var so = new SerializedObject(ssao);
            so.FindProperty("m_Settings.Intensity").floatValue = t.ssaoIntensity;
            so.FindProperty("m_Settings.Radius").floatValue = t.ssaoRadius;
            so.FindProperty("m_Settings.Downsample").boolValue = t.ssaoDownsample;
            so.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.2f;
            so.FindProperty("m_Settings.Falloff").floatValue = 60f;
            so.FindProperty("m_Settings.Source").enumValueIndex = t.ssaoDownsample ? 0 : 1;   // Depth (cheaper) / DepthNormals
            so.FindProperty("m_Settings.Samples").enumValueIndex = t.ssaoSamples;
            so.FindProperty("m_Settings.BlurQuality").enumValueIndex = t.ssaoDownsample ? 1 : 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            ssao.SetActive(true);
            SyncFeatureMap(renderer);
        }

        /// <summary>Keeps the renderer's feature map (local file ids) in step with its feature list.</summary>
        static void SyncFeatureMap(ScriptableRendererData data)
        {
            var so = new SerializedObject(data);
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize = data.rendererFeatures.Count;
            for (int i = 0; i < data.rendererFeatures.Count; i++)
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(data.rendererFeatures[i], out _, out long id))
                    map.GetArrayElementAtIndex(i).longValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }

        static void Set(SerializedObject so, string field, bool value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.boolValue = value;
        }

        static void Set(SerializedObject so, string field, int value)
        {
            var p = so.FindProperty(field);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = value; else p.intValue = value;
        }

        /// <summary>
        /// Night-city post-processing profile shared by the route and menu scenes: ACES, bloom tuned so
        /// lamp heads, neon, windows and tail lights glow without washing the scene, subtle camera
        /// motion blur at speed, and (when <paramref name="focusDistance"/> &gt; 0) depth of field. The
        /// motion blur, depth of field and bloom filtering quality follow the quality tier at runtime.
        /// </summary>
        public static VolumeProfile NightProfile(string name, float exposure, float bloomIntensity, float focusDistance = 0f)
        {
            string path = $"{Folder}/{name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.92f);
            bloom.intensity.Override(bloomIntensity);
            bloom.scatter.Override(0.72f);
            bloom.clamp.Override(40f);
            bloom.tint.Override(new Color(1f, 0.95f, 0.9f));
            bloom.highQualityFiltering.Override(false);
            var colour = profile.Add<ColorAdjustments>(true);
            colour.postExposure.Override(exposure);
            colour.contrast.Override(10f);
            colour.saturation.Override(-2f);
            colour.colorFilter.Override(Color.white);
            colour.hueShift.Override(0f);
            var white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(-6f);
            white.tint.Override(0f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
            vignette.center.Override(new Vector2(0.5f, 0.5f));
            vignette.rounded.Override(false);
            var blur = profile.Add<MotionBlur>(true);
            blur.mode.Override(MotionBlurMode.CameraOnly);
            blur.quality.Override(MotionBlurQuality.Low);
            blur.intensity.Override(0.22f);
            blur.clamp.Override(0.04f);
            if (focusDistance > 0f)
            {
                var dof = profile.Add<DepthOfField>(true);
                dof.mode.Override(DepthOfFieldMode.Bokeh);
                dof.focusDistance.Override(focusDistance);
                dof.focalLength.Override(70f);
                dof.aperture.Override(4f);
                dof.bladeCount.Override(7);
                dof.bladeCurvature.Override(1f);
                dof.bladeRotation.Override(0f);
                // Gaussian fallback (High tier): sharp to just past the car, then soft.
                dof.gaussianStart.Override(focusDistance + 2f);
                dof.gaussianEnd.Override(focusDistance + 14f);
                dof.gaussianMaxRadius.Override(1f);
                dof.highQualitySampling.Override(false);
            }

            foreach (var component in profile.components)
            {
                component.name = component.GetType().Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>Global volume + URP camera settings (post-processing on; anti-aliasing follows the quality tier).</summary>
        public static void SetupCamera(Camera cam, VolumeProfile profile, Transform volumeParent)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;

            var go = new GameObject("Post Processing");
            if (volumeParent != null) go.transform.SetParent(volumeParent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }
    }
}
