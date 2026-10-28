using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RacingPrototype.Tests
{
    /// <summary>End-to-end flows: 10-car grid, full races on all 4 routes, menu, pools, leaderboard, restart.</summary>
    public class RaceFlowTests
    {
        [SetUp]
        public void SetUp() => RaceTestUtil.IsolateData();

        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator GridHoldsTenCarsInTwoColumns([ValueSource(typeof(RaceTestUtil), nameof(RaceTestUtil.RouteIndices))] int route)
        {
            yield return RaceTestUtil.LoadRoute(RaceTestUtil.Setup(route: route));
            var race = RaceManager.Instance;
            Assert.AreEqual(RaceState.Countdown, race.State);
            var cars = race.Participants;
            Assert.AreEqual(10, cars.Count, "PPT: 10-car field");
            var path = race.Track;
            for (int i = 0; i < cars.Count; i++)
            {
                var p = cars[i];
                float d = path.FindClosestDistance(p.transform.position);
                float lateral = path.GetLateralOffset(p.transform.position, d);
                Assert.Less(Mathf.Abs(lateral) + 1.1f, path.HalfWidthAt(d), $"slot {i + 1} sticks out of the road");
                Assert.AreEqual(i % 2 == 0 ? -1f : 1f, Mathf.Sign(lateral), $"slot {i + 1} column");
                for (int j = i + 1; j < cars.Count; j++)
                    Assert.Greater(Vector3.Distance(p.transform.position, cars[j].transform.position), 4.5f, $"slots {i + 1} and {j + 1} overlap");
                var hits = Physics.OverlapBox(p.transform.position + Vector3.up * 0.6f, new Vector3(1.05f, 0.5f, 2.35f), p.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                Assert.IsTrue(hits.All(h => h.attachedRigidbody != null && h.attachedRigidbody.GetComponent<RaceParticipant>() != null),
                    $"slot {i + 1} intersects scenery or a barrier");
            }
            float poleDistance = path.FindClosestDistance(cars[0].transform.position);
            float lastDistance = path.FindClosestDistance(cars[9].transform.position);
            Assert.Greater(poleDistance, lastDistance, "pole position is at the front");
            Assert.Greater(lastDistance, 8f, "the whole grid sits after the start line");
            yield return RaceTestUtil.Capture($"route{route + 1}_grid_countdown");
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator FullRaceOnEveryRoute([ValueSource(typeof(RaceTestUtil), nameof(RaceTestUtil.RouteIndices))] int route)
        {
            yield return RaceTestUtil.LoadRoute(RaceTestUtil.Setup(car: route % CarCatalog.Current.Count, route: route), autopilot: true);
            var race = RaceManager.Instance;
            Time.timeScale = 6f;
            yield return RaceTestUtil.WaitUntilRacing();
            yield return RaceTestUtil.WaitFor(() => race.Timer.Elapsed > 30f, 120f);
            Time.timeScale = 1f;
            yield return RaceTestUtil.Capture($"route{route + 1}_racing_hud");
            Time.timeScale = 6f;
            yield return RaceTestUtil.WaitFor(() => race.State == RaceState.Results, race.TimeLimit + 60f);
            Time.timeScale = 1f;
            yield return null;
            yield return RaceTestUtil.Capture($"route{route + 1}_results");

            foreach (var p in race.Results)
                Debug.Log($"[RaceFlowTests] Route {route + 1} #{p.FinalRank} {p.DisplayName,-11} {p.CarName,-16} finished={p.Finished} valid={p.ResultValid} " +
                          $"total={RaceFormat.Time(p.FinishTime)} best={RaceFormat.Time(p.BestLapTime)} laps={p.LapsCompleted} resets={p.ResetCount} {p.ResultNote}");

            Assert.AreEqual(RaceState.Results, race.State);
            Assert.AreEqual(10, race.Results.Count);
            Assert.AreEqual(10, race.ValidatedCount, $"every car should complete route {route + 1} with a valid result");
            var winner = race.Winner;
            Assert.IsNotNull(winner);
            float best = race.Participants.Where(p => !p.Dnf).Min(p => p.FinishTime);
            Assert.AreEqual(best, winner.FinishTime, 1e-4f, "lowest total time wins");
            for (int i = 1; i < race.Results.Count; i++)
                Assert.LessOrEqual(RaceRanking.Compare(Input(race.Results[i - 1]), Input(race.Results[i])), 0, "results follow the ranking rules");
            Assert.Greater(winner.FinishTime, 4f * race.Track.Length / 60f, "winning time comes from real driving");

            var stored = LocalDataStore.Results.Where(r => !r.sample).ToList();
            Assert.AreEqual(10, stored.Count, "results saved for the leaderboard");
            Assert.AreEqual(1, stored.Single(r => r.player == winner.DisplayName).rank);
        }

        static ResultInput Input(RaceParticipant p) => new ResultInput
        {
            finished = !p.Dnf, totalTime = p.FinishTime, bestLap = p.BestLapTime, finishOrder = p.Finished ? p.FinishOrder : int.MaxValue,
            lapsCompleted = p.LapsCompleted, progress = p.ComputeProgress()
        };

        [UnityTest]
        public IEnumerator SpectatorsAndScenerySitOffTheRacingSurface([ValueSource(typeof(RaceTestUtil), nameof(RaceTestUtil.RouteIndices))] int route)
        {
            yield return RaceTestUtil.LoadRoute(RaceTestUtil.Setup(opponents: 0, route: route));
            var path = RaceManager.Instance.Track;
            int spectators = 0;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name != "Spectator") continue;
                spectators++;
                float d = path.FindClosestDistance(t.position);
                Assert.Greater(Mathf.Abs(path.GetLateralOffset(t.position, d)), path.BarrierOffsetAt(d) + 0.4f, $"spectator on the racing surface at {t.position}");
            }
            Assert.Greater(spectators, 50, "spectator zones exist");

            // Buildings are statically batched, so in Play mode their renderer bounds are those of the
            // combined mesh. Measure from the building origin (its footprint centre) instead: every
            // building is at least half its depth (>= 4.5 m) behind the frontage.
            var city = GameObject.Find("City").transform;
            int buildings = 0;
            foreach (var r in city.GetComponentsInChildren<MeshRenderer>())
            {
                string n = r.gameObject.name;
                bool building = n.StartsWith("House") || n.StartsWith("Shop") || n.StartsWith("Apartment") || n.StartsWith("Office") || n.StartsWith("Hotel") || n.StartsWith("Warehouse") || n.StartsWith("Civic");
                if (!building || r.transform.parent.name == "City Below" || r.transform.parent.name == "Skyline") continue;
                if (buildings++ == 0)
                    Debug.Log($"[RaceFlowTests] Route {route + 1} first building {n}: staticBatch={r.isPartOfStaticBatch} rendererBounds={r.bounds.size}");
                Vector3 c = r.transform.position;
                Assert.That(c.y, Is.InRange(-0.3f, 0.3f), $"{n} is floating or sunk at {c}");
                float dc = path.FindClosestDistance(c);
                Assert.Greater(Mathf.Abs(path.GetLateralOffset(c, dc)), path.BarrierOffsetAt(dc) + 6f, $"{n} at {c} intrudes on the track");
            }
            Assert.Greater(buildings, 100, "city buildings exist");

            foreach (var cp in RaceManager.Instance.Laps.Checkpoints)
            {
                float d = path.FindClosestDistance(cp.transform.position);
                Assert.Less(Mathf.Abs(path.GetLateralOffset(cp.transform.position, d)), 0.5f, "checkpoint is centred on the road");
                Assert.Greater(Vector3.Dot(cp.transform.forward, path.GetTangent(d)), 0.98f, "checkpoint faces the driving direction");
            }
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator MenuGarageAndQuickRaceStartTheChosenRace()
        {
            yield return RaceTestUtil.LoadMenu(showSplash: true);
            var menu = UnityEngine.Object.FindFirstObjectByType<MenuUI>();
            Assert.IsTrue(menu.SplashVisible, "thegameio splash shows first");
            yield return RaceTestUtil.Capture("menu_00_splash");
            yield return RaceTestUtil.WaitFor(() => !menu.SplashVisible, 6f);
            yield return RaceTestUtil.Capture("menu_01_main");

            menu.Show(MenuScreen.Garage);
            int car = CarCatalog.Current.Count - 1;
            menu.SelectCar(car);
            menu.SelectColour(2);
            yield return null;
            yield return RaceTestUtil.Capture("menu_02_garage");
            Assert.AreEqual(car, menu.CurrentSetup.carIndex);

            menu.Show(MenuScreen.RaceSetup);
            menu.SelectRoute(2);
            menu.SetLaps(6);
            menu.SetLaps(13);
            Assert.AreEqual(10, menu.CurrentSetup.laps, "laps clamp at 10");
            menu.SetLaps(6);
            yield return RaceTestUtil.Capture("menu_03_route_select");
            menu.Show(MenuScreen.Pools);
            yield return null;
            yield return RaceTestUtil.Capture("menu_04_pools");
            menu.Show(MenuScreen.Leaderboard);
            yield return null;
            yield return RaceTestUtil.Capture("menu_05_leaderboard");

            menu.StartQuickRace();
            yield return RaceTestUtil.WaitForScene(RaceTestUtil.SceneOf(2));
            var race = RaceManager.Instance;
            Assert.IsNotNull(race);
            Assert.AreEqual(2, race.Route.RouteIndex);
            Assert.AreEqual(3, race.Player.CarIndex);
            Assert.AreEqual(6, race.Laps.TotalLaps);
            Assert.AreEqual(10, race.Participants.Count);
            Assert.AreEqual(CarCatalog.Current.paintPalette[2], race.Player.GetComponent<CarColourCustomizer>().Colour);
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator PoolJoinRaceUpdatesStatusAndLeaderboard()
        {
            yield return RaceTestUtil.LoadMenu();
            var menu = UnityEngine.Object.FindFirstObjectByType<MenuUI>();
            var pool1 = LocalDataStore.Pools.First(p => p.name == "Pool 1");
            var pool2 = LocalDataStore.Pools.First(p => p.name == "Pool 2");
            var pool3 = LocalDataStore.Pools.First(p => p.name == "Pool 3");
            Assert.AreEqual(PoolStatus.Open, pool1.status);
            Assert.AreEqual(7, pool1.Joined);
            Assert.AreEqual(PoolStatus.Full, pool2.status);
            Assert.IsFalse(pool2.HasSeat, "a full pool cannot be joined");
            Assert.AreEqual(PoolStatus.Racing, pool3.status);
            Assert.IsFalse(LocalDataStore.Join(pool3, "Someone"), "a racing pool cannot be joined");

            var created = LocalDataStore.CreatePool("Pool 4", 25, 4, 3, DateTime.Now.AddHours(1));
            Assert.AreEqual(PoolStatus.Open, created.status);
            Assert.AreEqual(10, created.maxPlayers);
            Assert.AreEqual(4, LocalDataStore.Pools.Count, "admin can add pools beyond the samples");

            menu.JoinPool(pool1);
            yield return RaceTestUtil.WaitForScene(RaceTestUtil.SceneOf(pool1.route));
            var race = RaceManager.Instance;
            Assert.AreSame(pool1, race.Pool);
            Assert.AreEqual(8, race.Participants.Count, "7 pool members + you");
            Assert.AreEqual(PoolStatus.Racing, pool1.status);
            race.SetPlayerAutopilot(true);
            Time.timeScale = 6f;
            yield return RaceTestUtil.WaitUntilRacing();
            race.SetPlayerAutopilot(true);
            yield return RaceTestUtil.WaitFor(() => race.State == RaceState.Results, race.TimeLimit + 60f);
            Time.timeScale = 1f;
            Assert.AreEqual(PoolStatus.Finished, pool1.status);
            var standings = LocalDataStore.PoolStandings(pool1.id);
            Assert.AreEqual(8, standings.Count, "all pool players listed");
            CollectionAssert.AreEqual(Enumerable.Range(1, 8).ToArray(), standings.Select(s => s.rank).ToArray());
            Assert.IsTrue(standings.Any(s => s.isLocalPlayer));

            race.QuitToMenu(MenuScreen.Leaderboard);
            yield return RaceTestUtil.WaitForScene(SceneLoader.MenuScene);
            yield return null;
            yield return RaceTestUtil.Capture("menu_06_leaderboard_after_pool");
            var allTime = LocalDataStore.Aggregate(LocalDataStore.Period.AllTime, DateTime.Now);
            Assert.IsTrue(allTime.Any(t => t.isLocalPlayer), "the player's result appears in the all-time view");
        }

        [UnityTest]
        public IEnumerator TenLapRaceNeedsTenFullLaps()
        {
            yield return RaceTestUtil.LoadRoute(RaceTestUtil.Setup(laps: 10, opponents: 0));
            yield return RaceTestUtil.WaitUntilRacing();
            var race = RaceManager.Instance;
            var p = race.Player;
            RaceTestUtil.TakeControl(p);
            Assert.AreEqual(10, race.Laps.TotalLaps);
            for (int lap = 1; lap <= 10; lap++)
            {
                for (int cp = 1; cp <= 4; cp++) race.Laps.RegisterCrossing(p, cp, true);
                race.Laps.RegisterCrossing(p, 0, true, Time.time + 40f * lap);
                Assert.AreEqual(lap, p.LapsCompleted);
                Assert.AreEqual(lap == 10, p.Finished);
            }
            Assert.AreEqual(RaceState.Results, race.State);
            Assert.AreSame(p, race.Winner);
        }

        [UnityTest]
        public IEnumerator RestartReloadsWithSameSetup()
        {
            yield return RaceTestUtil.LoadRoute(RaceTestUtil.Setup(car: CarCatalog.Current.Count - 1, laps: 5, opponents: 3, route: 1));
            yield return RaceTestUtil.WaitUntilRacing();
            var first = RaceManager.Instance;
            first.Laps.RegisterCrossing(first.Player, 1, true);
            first.RestartRace();
            yield return RaceTestUtil.WaitFor(() => RaceManager.Instance != null && RaceManager.Instance != first && !SceneLoader.IsLoading, 30f);
            var race = RaceManager.Instance;
            Assert.AreEqual(RaceState.Countdown, race.State);
            Assert.AreEqual(1, race.Route.RouteIndex);
            Assert.AreEqual(2, race.Player.CarIndex);
            Assert.AreEqual(5, race.Laps.TotalLaps);
            Assert.AreEqual(4, race.Participants.Count);
            Assert.AreEqual(1, race.Player.NextCheckpoint);
        }
    }
}
