using UnityEngine;
using UnityEngine.Rendering;

namespace RacingPrototype
{
    /// <summary>The URP asset of each quality tier, Low / High / Ultra (Resources/QualityTiers, written by the builder).</summary>
    public class QualityTierSet : ScriptableObject
    {
        public RenderPipelineAsset[] pipelines = new RenderPipelineAsset[3];
    }
}
