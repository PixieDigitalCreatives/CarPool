using UnityEngine;

namespace RacingPrototype
{
    /// <summary>Fixed rules shared by gameplay, UI and tests.</summary>
    public static class RaceRules
    {
        public const int MinLaps = 4;            // handwritten requirement: 4-10, default 4
        public const int MaxLaps = 10;
        public const int DefaultLaps = 4;
        public const int MaxRacers = 10;         // PPT: 10 racers per pool, 10-car grid
        public const int MaxOpponents = MaxRacers - 1;
        public const int RouteCount = 4;         // PPT: 4 confirmed routes
        /// <summary>Race time limit as a multiple of the route's par time (docx: "2x the route's par time").</summary>
        public const float TimeLimitParMultiplier = 2f;

        public static int ClampLaps(int laps) => Mathf.Clamp(laps, MinLaps, MaxLaps);
        public static int ClampOpponents(int opponents) => Mathf.Clamp(opponents, 0, MaxOpponents);
        public static int ClampRoute(int route) => Mathf.Clamp(route, 0, RouteCount - 1);
    }

    /// <summary>Everything chosen before a race (garage, route select or pool).</summary>
    [System.Serializable]
    public struct RaceSetup
    {
        public int carIndex;
        public Color paintColour;
        public int laps;
        public int opponents;
        public int routeIndex;
        /// <summary>Pool being raced, or empty for a quick race.</summary>
        public string poolId;

        public RaceSetup Normalised(int carCount)
        {
            var s = this;
            s.carIndex = carCount > 0 ? Mathf.Clamp(carIndex, 0, carCount - 1) : 0;
            s.laps = RaceRules.ClampLaps(laps);
            s.opponents = RaceRules.ClampOpponents(opponents);
            s.routeIndex = RaceRules.ClampRoute(routeIndex);
            s.paintColour.a = 1f;
            return s;
        }

        public static RaceSetup Default => new RaceSetup
        {
            carIndex = 0,
            paintColour = new Color(1f, 0.71f, 0f),
            laps = RaceRules.DefaultLaps,
            opponents = RaceRules.MaxOpponents,
            routeIndex = 0,
            poolId = ""
        };
    }

    /// <summary>Which menu screen to show when returning from a race.</summary>
    public enum MenuScreen { Main, Garage, RaceSetup, Pools, Leaderboard }

    /// <summary>
    /// Survives scene loads (menu -> route -> menu). Reset on every play-mode start so nothing
    /// leaks between editor sessions.
    /// </summary>
    public static class RaceSession
    {
        public static bool HasSetup;
        public static RaceSetup Setup = RaceSetup.Default;
        public static bool PlayerAutopilot;
        /// <summary>Names of the other racers (pool members). Empty = generated AI names.</summary>
        public static string[] OpponentNames = new string[0];
        public static string PlayerName = "You";
        /// <summary>Grid slot for the player (pool join order); -1 = default.</summary>
        public static int PlayerGridSlot = -1;
        public static MenuScreen ReturnScreen = MenuScreen.Main;
        public static bool SplashShown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            HasSetup = false;
            Setup = RaceSetup.Default;
            PlayerAutopilot = false;
            OpponentNames = new string[0];
            PlayerName = "You";
            PlayerGridSlot = -1;
            ReturnScreen = MenuScreen.Main;
            SplashShown = false;
        }
    }

    public static class RaceFormat
    {
        /// <summary>m:ss.hh as used on the PPT leaderboard, or a placeholder for "no time".</summary>
        public static string Time(float seconds)
        {
            if (seconds < 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return "-:--.--";
            int hundredths = Mathf.FloorToInt(seconds * 100f);
            int minutes = hundredths / 6000;
            int secs = hundredths / 100 % 60;
            int frac = hundredths % 100;
            return $"{minutes}:{secs:00}.{frac:00}";
        }

        public static string Ordinal(int position)
        {
            if (position <= 0) return "-";
            int mod100 = position % 100;
            if (mod100 >= 11 && mod100 <= 13) return position + "th";
            switch (position % 10)
            {
                case 1: return position + "st";
                case 2: return position + "nd";
                case 3: return position + "rd";
                default: return position + "th";
            }
        }
    }
}
