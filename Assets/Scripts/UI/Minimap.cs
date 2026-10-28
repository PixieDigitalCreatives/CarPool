using UnityEngine;
using UnityEngine.UI;

namespace RacingPrototype
{
    /// <summary>
    /// HUD minimap (PPT slide 2: every route has its own minimap): the route's generated map image
    /// with a dot per car. The player dot is drawn larger in the brand colour.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        [SerializeField] Image map;
        [SerializeField] RectTransform dotLayer;

        Image[] dots = new Image[0];

        /// <summary>Builds the map widget inside the given rect.</summary>
        public static Minimap Create(RectTransform area)
        {
            var mm = area.gameObject.AddComponent<Minimap>();
            mm.map = UIFactory.Panel("Map", area, Color.white);
            mm.map.rectTransform.Stretch(8, 8, 8, 8);
            mm.map.preserveAspect = true;
            mm.map.enabled = false;   // shown once the route's map sprite is assigned (no white placeholder square)
            mm.dotLayer = mm.map.rectTransform;
            return mm;
        }

        void LateUpdate()
        {
            var race = RaceManager.Instance;
            var route = RouteInfo.Current;
            if (race == null || route == null) return;
            if (map.sprite != route.Minimap)
            {
                map.sprite = route.Minimap;
                map.enabled = route.Minimap != null;
            }

            var racers = race.Participants;
            if (dots.Length != racers.Count) BuildDots(racers.Count);

            Rect r = map.rectTransform.rect;
            // preserveAspect letterboxes the sprite inside the rect; map into the visible area.
            float aspect = map.sprite != null ? map.sprite.rect.width / map.sprite.rect.height : 1f;
            float w = r.width, h = r.height;
            if (w / h > aspect) w = h * aspect; else h = w / aspect;

            for (int i = 0; i < racers.Count; i++)
            {
                var p = racers[i];
                if (p == null) continue;
                Vector2 uv = route.ToMap(p.transform.position);
                var rt = dots[i].rectTransform;
                rt.anchoredPosition = new Vector2((uv.x - 0.5f) * w, (uv.y - 0.5f) * h);
                bool player = p.IsPlayer;
                dots[i].color = player ? UIFactory.Accent : UIFactory.WithAlpha(p.Colour, 0.95f);
                rt.sizeDelta = player ? new Vector2(16, 16) : new Vector2(10, 10);
                if (player) rt.SetAsLastSibling();
            }
        }

        void BuildDots(int count)
        {
            foreach (var d in dots) if (d != null) Destroy(d.gameObject);
            dots = new Image[count];
            for (int i = 0; i < count; i++)
            {
                dots[i] = UIFactory.Panel("Car Dot", dotLayer, Color.white, UIFactory.Circle);
                dots[i].rectTransform.anchorMin = dots[i].rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                var outline = dots[i].gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }
    }
}
