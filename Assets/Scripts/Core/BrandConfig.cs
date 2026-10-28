using UnityEngine;

namespace RacingPrototype
{
    /// <summary>
    /// thegameio brand settings used by every branded touchpoint (splash, loader, menus, HUD,
    /// billboards, number plates, victory screen). The values are sampled from the client PPT.
    /// The logo is a TEMPORARY PLACEHOLDER wordmark until thegameio supplies logo files, colours
    /// and fonts: assign <see cref="logo"/> / <see cref="font"/> here and every screen picks them up.
    /// Lives in Resources so runtime code can load it without scene references.
    /// </summary>
    [CreateAssetMenu(menuName = "Racing Prototype/Brand Config", fileName = "BrandConfig")]
    public class BrandConfig : ScriptableObject
    {
        [Header("Identity")]
        public string wordmark = "thegameio";
        public string productLine = "Multiplayer Racing Game";
        [Tooltip("Client logo. Leave empty to use the placeholder wordmark text.")]
        public Sprite logo;
        [Tooltip("Client font. Leave empty to use Unity's built-in font as a placeholder.")]
        public Font font;
        [TextArea] public string placeholderNote = "Placeholder wordmark - replace with thegameio logo files (SVG/PNG), colours and fonts.";

        [Header("Colours (sampled from thegameio_Racing_Game.ppt)")]
        public Color primary = new Color32(255, 180, 0, 255);      // amber wordmark / Full badge
        public Color accentRed = new Color32(225, 6, 0, 255);      // stat bars, Racing badge
        public Color background = new Color32(20, 22, 27, 255);    // slide 1 background
        public Color panel = new Color32(35, 40, 51, 255);         // leaderboard / admin panel
        public Color panelLight = new Color32(48, 54, 68, 255);
        public Color text = new Color32(245, 246, 248, 255);
        public Color mutedText = new Color32(150, 157, 172, 255);
        public Color open = new Color32(22, 163, 74, 255);         // Pool "Open" badge
        public Color full = new Color32(255, 180, 0, 255);         // Pool "Full" badge
        public Color racing = new Color32(225, 6, 0, 255);         // Pool "Racing" badge
        public Color finished = new Color32(100, 108, 124, 255);   // Pool "Finished" badge
        public Color gold = new Color32(255, 180, 0, 255);
        public Color silver = new Color32(196, 201, 210, 255);
        public Color bronze = new Color32(205, 127, 50, 255);

        static BrandConfig current;

        /// <summary>The project brand (Resources/BrandConfig), or built-in defaults.</summary>
        public static BrandConfig Current
        {
            get
            {
                if (current == null) current = Resources.Load<BrandConfig>("BrandConfig");
                if (current == null) current = CreateInstance<BrandConfig>();
                return current;
            }
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
