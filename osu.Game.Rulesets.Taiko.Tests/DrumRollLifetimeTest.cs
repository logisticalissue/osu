// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Taiko.Objects;

namespace osu.Game.Rulesets.Taiko.Tests
{
    [TestFixture]
    public class DrumRollLifetimeTest
    {
        [Test]
        public void TestLifetimeExtendedToCoverTicksJudgedPastEndTime()
        {
            var drumRoll = new DrumRoll { StartTime = 1000, Duration = 1000 };
            drumRoll.ApplyDefaults(createControlPointInfo(), new BeatmapDifficulty());

            var ticks = drumRoll.NestedHitObjects.OfType<DrumRollTick>().ToArray();
            Assert.That(ticks, Is.Not.Empty);

            double latestPossibleJudgement = ticks.Max(t => t.GetEndTime() + t.MaximumJudgementOffset);

            // The scenario is only meaningful if a tick can be judged after the drum roll's own judgement window closes.
            Assert.That(latestPossibleJudgement, Is.GreaterThan(drumRoll.GetEndTime() + drumRoll.MaximumJudgementOffset));

            var entry = new HitObjectLifetimeEntry(drumRoll);
            foreach (var nested in drumRoll.NestedHitObjects)
                entry.NestedEntries.Add(new HitObjectLifetimeEntry(nested));

            // Simulate the drawable requesting a short lifetime end around the drum roll's end time.
            entry.LifetimeEnd = drumRoll.GetEndTime();

            // While unjudged, the entry must extend its lifetime to cover the latest nested tick judgement.
            Assert.That(entry.LifetimeEnd, Is.EqualTo(latestPossibleJudgement));
        }

        private static ControlPointInfo createControlPointInfo()
        {
            var controlPointInfo = new ControlPointInfo();
            controlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            return controlPointInfo;
        }
    }
}
