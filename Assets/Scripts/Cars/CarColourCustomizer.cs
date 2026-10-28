using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// Applies a paint colour to the body panels of a car. Uses MaterialPropertyBlocks so every car
    /// can share the same paint material while showing its own colour. Palette-textured models
    /// (imported cars) are repainted by recolouring only the paint cells of their palette.
    /// </summary>
    public class CarColourCustomizer : MonoBehaviour
    {
        [SerializeField] Renderer[] paintRenderers;
        [Tooltip("Material slot that holds the paint on each renderer (parallel to Paint Renderers).")]
        [SerializeField] int[] paintSlots;
        [SerializeField] Color colour = new Color(0.8f, 0.1f, 0.1f);
        [Tooltip("Palette-textured models: readable palette whose alpha marks the paint cells (RGB there is the paint shade). Leave empty for tinted paint.")]
        [SerializeField] Texture2D paintPalette;

        static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        MaterialPropertyBlock block;
        Texture2D recoloured;

        public Color Colour => colour;

        /// <summary>Called by the editor car builder.</summary>
        public void Configure(Renderer[] renderers, int[] slots, Color defaultColour)
        {
            paintRenderers = renderers;
            paintSlots = slots;
            colour = defaultColour;
        }

        /// <summary>Called by the editor car builder for palette-textured (imported) models.</summary>
        public void ConfigurePalette(Texture2D palette) => paintPalette = palette;

        void Awake() => Apply();

        void OnDestroy()
        {
            if (recoloured != null) Destroy(recoloured);
        }

        public void SetColour(Color newColour)
        {
            colour = newColour;
            colour.a = 1f;
            Apply();
        }

        void Apply()
        {
            if (paintRenderers == null) return;
            block ??= new MaterialPropertyBlock();
            var palette = paintPalette != null ? Recolour() : null;
            for (int i = 0; i < paintRenderers.Length; i++)
            {
                var r = paintRenderers[i];
                if (r == null) continue;
                int slot = paintSlots != null && i < paintSlots.Length ? paintSlots[i] : 0;
                r.GetPropertyBlock(block, slot);
                if (palette != null)
                {
                    block.SetTexture(BaseMapId, palette);
                    block.SetColor(ColorId, Color.white);
                }
                else block.SetColor(ColorId, colour);
                r.SetPropertyBlock(block, slot);
            }
        }

        /// <summary>Copy of the palette with the paint cells (alpha set) shaded in the current colour.</summary>
        Texture2D Recolour()
        {
            if (!paintPalette.isReadable) return null;
            var px = paintPalette.GetPixels32();
            if (recoloured == null)
                recoloured = new Texture2D(paintPalette.width, paintPalette.height, TextureFormat.RGBA32, true)
                    { name = name + " Paint", filterMode = paintPalette.filterMode, wrapMode = TextureWrapMode.Clamp };
            Color32 c = colour;
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i];
                px[i] = p.a > 127
                    ? new Color32((byte)(c.r * p.r / 255), (byte)(c.g * p.g / 255), (byte)(c.b * p.b / 255), 255)
                    : new Color32(p.r, p.g, p.b, 255);
            }
            recoloured.SetPixels32(px);
            recoloured.Apply(true);
            return recoloured;
        }
    }
}
