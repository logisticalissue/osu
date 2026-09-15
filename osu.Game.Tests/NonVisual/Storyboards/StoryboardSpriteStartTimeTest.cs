// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Storyboards;
using osu.Game.Storyboards.Commands;
using osu.Game.Storyboards.Drawables;
using osuTK;

namespace osu.Game.Tests.NonVisual.Storyboards
{
    [TestFixture]
    public class StoryboardSpriteStartTimeTest
    {
        [TestCase(false)]
        [TestCase(true)]
        public void LifetimeIncludesTheFirstDeclaredAlphaInitialValue(bool looping)
        {
            var sprite = new StoryboardSprite(StoryboardElementSource.Shared, "test.png", Anchor.Centre, Vector2.Zero);
            sprite.Commands.AddX(Easing.None, 0, 6000, 0, 100);
            if (looping)
                sprite.AddLoopingGroup(5000, 0).AddAlpha(Easing.None, 0, 1000, 1, 1);
            else
                sprite.Commands.AddAlpha(Easing.None, 5000, 6000, 1, 1);
            sprite.Commands.AddAlpha(Easing.None, 1000, 2000, 0, 0);

            using var drawable = new DrawableStoryboardSprite(sprite);
            sprite.ApplyAt(drawable, 500);
            Assert.That(drawable.Alpha, Is.EqualTo(1));
            Assert.That(drawable.LifetimeStart, Is.EqualTo(0));
        }

        [Test]
        public void TriggeredSpritesRetainChronologicalInitialAlpha()
        {
            var sprite = new StoryboardSprite(StoryboardElementSource.Shared, "test.png", Anchor.Centre, Vector2.Zero);
            sprite.Commands.AddX(Easing.None, 0, 6000, 0, 100);
            sprite.Commands.AddAlpha(Easing.None, 5000, 6000, 1, 1);
            sprite.Commands.AddAlpha(Easing.None, 1000, 2000, 0, 0);
            sprite.AddTriggerGroup("Passing", 0, 6000, 0);
            Assert.That(sprite.StartTime, Is.EqualTo(5000));
        }

        [TestCaseSource(nameof(cases))]
        public void MatchesReferenceImplementation(string name, Action<StoryboardSprite> build)
        {
            var sprite = new StoryboardSprite(StoryboardElementSource.Shared, "test.png", Anchor.Centre, Vector2.Zero);
            build(sprite);

            Assert.That(sprite.StartTime, Is.EqualTo(reference(sprite)), name);
        }

        private static IEnumerable<object?[]> cases()
        {
            yield return new object?[]
            {
                "no alpha commands", (Action<StoryboardSprite>)(s => s.Commands.AddX(Easing.None, 500, 1000, 0, 100))
            };

            yield return new object?[]
            {
                "starts already visible", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddX(Easing.None, 100, 1000, 0, 100);
                    s.Commands.AddAlpha(Easing.None, 500, 1000, 0.5f, 1);
                })
            };

            yield return new object?[]
            {
                "fades in from nothing", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddX(Easing.None, 100, 1000, 0, 100);
                    s.Commands.AddAlpha(Easing.None, 500, 1000, 0, 1);
                })
            };

            yield return new object?[]
            {
                "no-op fade before the real one", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddX(Easing.None, 0, 5000, 0, 100);
                    s.Commands.AddAlpha(Easing.None, 100, 200, 0, 0);
                    s.Commands.AddAlpha(Easing.None, 3000, 3500, 0, 1);
                })
            };

            yield return new object?[]
            {
                "several no-op fades, never visible", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddX(Easing.None, 0, 5000, 0, 100);
                    s.Commands.AddAlpha(Easing.None, 100, 200, 0, 0);
                    s.Commands.AddAlpha(Easing.None, 300, 400, 0, 0);
                })
            };

            yield return new object?[]
            {
                "alpha only inside a loop", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddX(Easing.None, 0, 5000, 0, 100);

                    var loop = s.AddLoopingGroup(2000, 3);
                    loop.AddAlpha(Easing.None, 0, 100, 0, 1);
                })
            };

            yield return new object?[]
            {
                "loop alpha earlier than sprite alpha", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddAlpha(Easing.None, 4000, 4500, 0, 1);

                    var loop = s.AddLoopingGroup(1000, 2);
                    loop.AddAlpha(Easing.None, 0, 100, 0, 1);
                })
            };

            yield return new object?[]
            {
                "no-op in commands, real fade in loop", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddAlpha(Easing.None, 200, 300, 0, 0);

                    var loop = s.AddLoopingGroup(5000, 2);
                    loop.AddAlpha(Easing.None, 0, 100, 0, 1);
                })
            };

            yield return new object?[]
            {
                "two loops with alpha", (Action<StoryboardSprite>)(s =>
                {
                    var first = s.AddLoopingGroup(3000, 2);
                    first.AddAlpha(Easing.None, 0, 100, 0, 0);

                    var second = s.AddLoopingGroup(1500, 2);
                    second.AddAlpha(Easing.None, 0, 100, 0, 1);
                })
            };

            yield return new object?[]
            {
                "visible only at the end of a fade out", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddAlpha(Easing.None, 800, 900, 0, 0);
                    s.Commands.AddAlpha(Easing.None, 1200, 1300, 0, 0.5f);
                })
            };

            for (int seed = 0; seed < 8; seed++)
            {
                int captured = seed;
                yield return new object?[] { $"fuzzed {seed}", (Action<StoryboardSprite>)(s => fuzz(s, captured)) };
            }
        }

        private static void fuzz(StoryboardSprite s, int seed)
        {
            var random = new Random(seed * 977 + 13);

            for (int i = 0; i < 12; i++)
            {
                double start = random.Next(0, 3000);
                double end = start + random.Next(0, 400);

                float from = random.Next(0, 4) == 0 ? random.Next(1, 100) / 100f : 0;
                float to = random.Next(0, 3) == 0 ? random.Next(1, 100) / 100f : 0;

                switch (random.Next(0, 4))
                {
                    case 0:
                        s.Commands.AddX(Easing.None, start, end, 0, 100);
                        break;

                    case 1:
                        s.Commands.AddAlpha(Easing.None, start, end, from, to);
                        break;

                    case 2:
                        var loop = s.AddLoopingGroup(start, random.Next(0, 3));
                        loop.AddAlpha(Easing.None, 0, random.Next(0, 200), from, to);
                        break;

                    case 3:
                        var moveLoop = s.AddLoopingGroup(start, random.Next(0, 3));
                        moveLoop.AddY(Easing.None, 0, random.Next(0, 200), 0, 50);
                        break;
                }
            }
        }

        #region Reference implementation

        private static double reference(StoryboardSprite sprite)
        {
            var alphaCommands = sprite.Commands.Alpha.Concat(sprite.LoopingGroups.SelectMany(l => l.Alpha)).ToArray();
            var firstAlpha = sprite.TriggerGroups.Count == 0
                ? alphaCommands.MinBy(c => c.DeclarationIndex)
                : alphaCommands.MinBy(c => c.StartTime);
            var firstVisible = alphaCommands.Where(c => c.StartValue > 0 || c.EndValue > 0).MinBy(c => c.StartTime);

            return firstAlpha?.StartValue == 0 && firstVisible != null ? firstVisible.StartTime : sprite.EarliestTransformTime;
        }

        #endregion
    }
}
