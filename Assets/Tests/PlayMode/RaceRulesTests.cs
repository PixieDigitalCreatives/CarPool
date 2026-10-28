using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RacingPrototype.Tests
{
    /// <summary>Race rules (PPT slide 12 + handwritten requirements) exercised in the real route scenes.</summary>
    public class RaceRulesTests
    {
        [SetUp]
        public void SetUp() => RaceTestUtil.IsolateData();

        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        [Test]
        public void LapCountIsClampedBetweenFourAndTen()
        {
            Assert.AreEqual(4, RaceRules.DefaultLaps);
            Assert.AreEqual(4, RaceRules.ClampLaps(3), "PPT pool example of 3 laps is raised to the handwritten minimum of 4");
            Assert.AreEqual(7, RaceRules.ClampLaps(7));
            Assert.AreEqual(10, RaceRules.ClampLaps(12));
            Assert.AreEqual(10, RaceRules.MaxRacers);
            Assert.AreEqual(4, RaceRules.RouteCount);
        }

        [Test]
        public void RankingUsesTotalTimeThenBestLapThenEarliestFinish()
        {
            var inputs = new List<ResultInput>
            {
                new ResultInput { finished = true, totalTime = 100f, bestLap = 30f, finishOrder = 2 },  // 0
                new ResultInput { finished = true, totalTime = 100f, bestLap = 29f, finishOrder = 3 },  // 1 beats 0 on best lap
                new ResultInput { finished = true, totalTime = 99f, bestLap = 31f, finishOrder = 4 },   // 2 lowest total time wins
                new ResultInput { finished = false, lapsCompleted = 3, progress = 3500f },              // 3 DNF, more laps
                new ResultInput { finished = false, lapsCompleted = 2, progress = 2900f },              // 4 DNF
                new ResultInput { finished = true, totalTime = 100f, bestLap = 29f, finishOrder = 1 },  // 5 same as 1, finished earlier
            };
            CollectionAssert.AreEqual(new[] { 2, 5, 1, 0, 3, 4 }, RaceRanking.Order(inputs));
        }

        [UnityTest]
        public IEnumerator LapCountsOnlyAfterAllCheckpointsInOrder()
        {
            yield return RaceTestUtil.LoadRoute(RaceTestUtil.Setup(opponents: 0));
            yield return RaceTestUtil.WaitUntilRacing();
            var race = RaceManager.Instance;
            var laps = race.Laps;
            var p = race.Player;
            RaceTestUtil.TakeControl(p);
            Assert.AreEqual(4, laps.IntermediateCount);

            laps.RegisterCrossing(p, 0, true);
            Assert.AreEqual(0, p.LapsCompleted, "finish line before any checkpoint must not count");
            laps.RegisterCrossing(p, 2, true);
            Assert.AreEqual(1, p.NextCheckpoint, "checkpoint 2 before 1 is ignored");
            Assert.Contains("missed:1", p.EventLog);
            laps.RegisterCrossing(p, 1, true);
            laps.RegisterCrossing(p, 3, true);
            Assert.AreEqual(2, p.NextCheckpoint, "skipping checkpoint 2 is ignored");
            laps.RegisterCrossing(p, 2, true);
            laps.RegisterCrossing(p, 3, true);
            laps.RegisterCrossing(p, 0, true);
            Assert.AreEqual(0, p.LapsCompleted, "missed checkpoint 4 = lap not counted");
            laps.RegisterCrossing(p, 4, true);
            laps.RegisterCrossing(p, 0, true);
            Assert.AreEqual(1, p.LapsCompleted);
            Assert.AreEqual(1, p.LapTimes.Count);
        }

        [UnityTest]
        public IEnumerator BackwardsCrossingsNeverCount()
        {
            yield return RaceTestUtil.LoadRoute(RaceTestUtil.Setup(opponents: 0));
            yield return RaceTestUtil.WaitUntilRacing();
            var race = RaceManager.Instance;
            var p = race.Player;
            RaceTestUtil.TakeControl(p);
            race.Laps.RegisterCrossing(p, 1, false);
            Assert.AreEqual(1, p.NextCheckpoint);
            for (int i = 1; i <= 4; i++) race.Laps.RegisterCrossing(p, i, true);
            race.Laps.RegisterCrossing(p, 0, false);
            Assert.AreEqual(0, p.LapsCompleted, "finish line crossed the wrong way does not count");
            race.Laps.RegisterCrossing(p, 0, true);
            Assert.AreEqual(1, p.LapsCompleted);
        }

        [UnityTest]
        public IEnumerator CheckpointGateDetectsDrivingDirection()
        {
            yield return RaceTestUtil.LoadRoute(RaceTestUtil.Setup(opponents: 0));
            yield return RaceTestUtil.WaitUntilRacing();
            var race = RaceManager.Instance;
            var p = race.Player;
            var car = RaceTestUtil.TakeControl(p);
            float cp1 = race.Track.FindClosestDistance(race.Laps.Checkpoints[1].transform.position);
            float cp2 = race.Track.FindClosestDistance(race.Laps.Checkpoints[2].transform.position);

            RaceTestUtil.Place(p, cp1 - 20f);
            car.ThrottleInput = 1f;
            yield return RaceTestUtil.WaitFor(() => p.NextCheckpoint == 2, 8f);
            Assert.AreEqual(2, p.NextCheckpoint, "driving forwards through checkpoint 1 registers it");

            RaceTestUtil.Place(p, cp2 + 20f, 0f, true);
            car.ThrottleInput = 1f;
            yield return RaceTestUtil.WaitFor(() => p.EventLog.Contains("reverse:2"), 8f);
            Assert.Contains("reverse:2", p.EventLog);
            Assert.AreEqual(2, p.NextCheckpoint, "reverse crossing does not advance");

            RaceTestUtil.Place(p, cp2 - 12f);
            car.ThrottleInput = 1f;
            yield return RaceTestUtil.WaitForFixedSeconds(0.9f);
            car.ThrottleInput = -1f;
            yield return RaceTestUtil.WaitForFixedSeconds(3f);
            Assert.AreEqual(2, p.NextCheckpoint, "nosing into a gate and reversing out does not count");
        }

        [UnityTest]
        public IEnumerator BarriersStopCarsLeavingTheTrack([ValueSource(typeof(RaceTestUtil), nameof(RaceTestUtil.RouteIndices))] int route)
        {
            yield return RaceTestUtil.LoadRoute(RaceTestUtil.Setup(opponents: 0, route: route));
            yield return RaceTestUtil.WaitUntilRacing();
            var race = RaceManager.Instance;
            var path = race.Track;
            var car = RaceTestUtil.TakeControl(race.Player);
            for (int k = 0; k < 4; k++)
            {
                float d = (0.1f + k * 0.23f) * path.Length;
                foreach (float side in new[] { 1f, -1f })
                {
                    Vector3 heading = (path.GetRight(d) * side + path.GetTangent(d) * 0.3f).normalized;
                    car.Teleport(path.GetPoint(d) + Vector3.up * 0.05f, Quaternion.LookRotation(heading, Vector3.up));
                    car.Body.linearVelocity = heading * 48f;   // ~170 km/h into the wall
                    car.ThrottleInput = 1f;
                    yield return RaceTestUtil.WaitForFixedSeconds(1.6f);
                    float at = path.FindClosestDistance(car.transform.position, d);
                    float lateral = Mathf.Abs(path.GetLateralOffset(car.transform.position, at));
                    Assert.Less(lateral, path.BarrierOffsetAt(at), $"route {route + 1}: car passed through the barrier at d={d:F0} side={side}");
                    Assert.Greater(car.transform.position.y, -0.5f);
                }
            }
        }

        [UnityTest]
        public IEnumerator ResetReturnsCarToTheRoadFacingForward()
        {
            yield return RaceTestUtil.LoadRoute(RaceTestUtil.Setup(opponents: 0));
            yield return RaceTestUtil.WaitUntilRacing();
            var race = RaceManager.Instance;
            var path = race.Track;
            var p = race.Player;
            var car = RaceTestUtil.TakeControl(p);
            RaceTestUtil.Place(p, 250f);
            car.ThrottleInput = 1f;
            yield return RaceTestUtil.WaitForFixedSeconds(1.5f);
            float safe = p.TrackDistance;
            car.ThrottleInput = 0f;
            Vector3 right = path.GetRight(safe);
            car.Teleport(path.GetPoint(safe) + right * (path.BarrierOffsetAt(safe) - 1.2f), Quaternion.LookRotation(-path.GetTangent(safe) + right, Vector3.up));
            yield return RaceTestUtil.WaitForFixedSeconds(0.2f);
            p.ResetToLastSafePosition();
            yield return RaceTestUtil.WaitForFixedSeconds(0.3f);
            float d = path.FindClosestDistance(car.transform.position);
            Assert.Less(Mathf.Abs(path.GetLateralOffset(car.transform.position, d)), path.HalfWidthAt(d));
            Assert.Greater(Vector3.Dot(car.transform.forward, path.GetTangent(d)), 0.95f);
            Assert.LessOrEqual(d, safe + 0.5f, "a reset never moves the car forward");
        }

        [UnityTest]
        public IEnumerator ValidatorFlagsTamperedResults()
        {
            yield return RaceTestUtil.LoadRoute(RaceTestUtil.Setup(opponents: 0));
            yield return RaceTestUtil.WaitUntilRacing();
            var race = RaceManager.Instance;
            var p = race.Player;
            RaceTestUtil.TakeControl(p);
            for (int lap = 0; lap < 4; lap++)
            {
                for (int cp = 1; cp <= 4; cp++) race.Laps.RegisterCrossing(p, cp, true);
                race.Laps.RegisterCrossing(p, 0, true, Time.time + 40f * (lap + 1));
            }
            Assert.IsTrue(p.Finished);
            var ok = RaceResultValidator.Validate(p, 4, 4, race.Track.Length);
            Assert.IsTrue(ok.valid, ok.reason);

            p.EventLog.Remove("cp:3");
            Assert.IsFalse(RaceResultValidator.Validate(p, 4, 4, race.Track.Length).valid, "missing checkpoint is flagged");
            p.EventLog.Add("cp:3");
            p.LapTimes[1] = 1f;
            Assert.IsFalse(RaceResultValidator.Validate(p, 4, 4, race.Track.Length).valid, "tampered lap times are flagged");
        }
    }
}
