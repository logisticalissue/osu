// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Game.Storyboards;
using osu.Game.Storyboards.Commands;
using osu.Game.Storyboards.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tests.NonVisual.Storyboards
{
    [TestFixture]
    public class StoryboardSpriteApplyAtTest
    {
        private const float visibility_cutoff = 0.0001f;

        [TestCase(Easing.None, 25)]
        [TestCase(Easing.InQuad, 6.25f)]
        [TestCase(Easing.OutQuad, 43.75f)]
        public void InterpolationUsesTheCommandInterval(Easing easing, float quarterValue)
        {
            var s = sprite(b => b.Commands.AddX(easing, 100, 200, 0, 100));
            using var drawable = new DrawableStoryboardSprite(s);

            foreach (var (time, expected) in new[] { (99, 0f), (100, 0f), (125, quarterValue), (200, 100f), (201, 100f), (125, quarterValue) })
            {
                s.ApplyAt(drawable, time);
                Assert.That(drawable.X, Is.EqualTo(expected).Within(0.001), $"{easing} at {time}");
            }
        }

        [TestCase(false, 10)]
        [TestCase(true, 60)]
        public void InvisiblePropertiesAreSkippedUnlessAlwaysPresent(bool alwaysPresent, float expectedX)
        {
            var s = sprite(b =>
            {
                b.Commands.AddAlpha(Easing.None, 0, 1000, 0, 0);
                b.Commands.AddX(Easing.None, 0, 1000, 10, 110);
            });
            using var drawable = new DrawableStoryboardSprite(s) { AlwaysPresent = alwaysPresent };
            s.ApplyInitialValues(drawable);
            s.ApplyAt(drawable, 500);
            Assert.That(drawable.Alpha, Is.Zero);
            Assert.That(drawable.X, Is.EqualTo(expectedX));
        }

        [Test]
        public void LoopGapsRetainTheMostRecentlyEndedValue()
        {
            var s = sprite(b =>
            {
                var loop = b.AddLoopingGroup(100, 1);
                loop.AddX(Easing.None, 0, 100, 0, 100);
                loop.AddX(Easing.None, 200, 300, 200, 300);
                loop.AddY(Easing.None, 0, 400, 0, 0);
            });
            using var drawable = new DrawableStoryboardSprite(s);
            var samples = new[] { (99, 0), (150, 50), (200, 100), (250, 100), (300, 200), (400, 300), (450, 300), (500, 0), (650, 100), (750, 250), (800, 300), (850, 300) };

            foreach (var (time, expected) in samples.Concat(samples.Reverse()))
            {
                s.ApplyAt(drawable, time);
                Assert.That(drawable.X, Is.EqualTo(expected), $"loop gap at {time}");
            }
        }

        [TestCase(31)]
        [TestCase(32)]
        [TestCase(33)]
        public void SelectionAcrossIndexThreshold(int count)
        {
            var data = new List<(double start, double end, float from, float to)>
            {
                (400, 800, 40, 80),
                (100, 600, 10, 60),
                (200, 800, 200, 800),
                (400, 800, 4000, 8000),
                (900, 900, 9, 9),
                (1100, 1200, 11, 12),
            };

            var random = new Random(3452);
            while (data.Count < count)
            {
                double start = 2000 + random.Next(10) * 100;
                data.Add((start, start + random.Next(5) * 100, random.Next(100), random.Next(100)));
            }

            var s = sprite(b =>
            {
                foreach (var command in data)
                    b.Commands.AddX(Easing.None, command.start, command.end, command.from, command.to);
            });
            using var drawable = new DrawableStoryboardSprite(s);

            var times = data.SelectMany(c => new[] { c.start - 0.25, c.start, c.start + 0.25, (c.start + c.end) / 2, c.end - 0.25, c.end, c.end + 0.25 })
                            .Distinct().OrderBy(t => t).ToArray();

            foreach (double time in times.Concat(times.Reverse()).Concat(times.OrderBy(_ => random.Next())))
            {
                int winner = data.FindIndex(c => c.start <= time && time <= c.end);
                if (winner < 0)
                {
                    winner = Enumerable.Range(0, data.Count).Where(i => data[i].end <= time)
                                       .OrderByDescending(i => data[i].end).ThenByDescending(i => data[i].start).ThenBy(i => i)
                                       .FirstOrDefault(-1);
                }

                float expected = data[0].from;
                if (winner >= 0)
                {
                    var command = data[winner];
                    expected = time >= command.end ? command.to
                        : command.from + (command.to - command.from) * (float)((time - command.start) / (command.end - command.start));
                }

                s.ApplyAt(drawable, time);
                Assert.That(drawable.X, Is.EqualTo(expected).Within(0.001), $"{count} commands at {time}");
            }
        }

        [TestCase(31, false)]
        [TestCase(31, true)]
        [TestCase(32, false)]
        [TestCase(32, true)]
        [TestCase(33, false)]
        [TestCase(33, true)]
        [TestCase(31, false, 31)]
        [TestCase(31, true, 32)]
        [TestCase(32, false, 32)]
        [TestCase(32, true, 32)]
        [TestCase(33, false, 33)]
        [TestCase(33, true, 33)]
        public void IndexedCommandsAndLoopsPreserveSelection(int ordinaryCount, bool loopsFirst, int bodyCount = 2)
        {
            var s = sprite(_ => { });
            var commands = new List<(double start, double end, float value, double period, int iterations)>();

            if (loopsFirst)
                addLoops();

            add(s.Commands, 120, 200, 40);
            add(s.Commands, 900, 1000, 50);
            for (int i = 2; i < ordinaryCount; i++)
                add(s.Commands, -2000 + i * 20, -1995 + i * 20, 100 + i);

            if (!loopsFirst)
                addLoops();

            var intervals = commands.SelectMany((c, declaration) => Enumerable.Range(0, c.iterations).Select(iteration =>
                (start: c.start + iteration * c.period, end: c.end + iteration * c.period, originalStart: c.start, c.value, declaration))).ToArray();
            var times = intervals.SelectMany(c => new[] { c.start - 0.25, c.start, c.start + 0.25, (c.start + c.end) / 2, c.end - 0.25, c.end, c.end + 0.25 })
                                 .Distinct().OrderBy(t => t).ToArray();
            var random = new Random(619);
            using var drawable = new DrawableStoryboardSprite(s);

            foreach (double time in times.Concat(times.Reverse()).Concat(times.OrderBy(_ => random.Next())))
            {
                var active = intervals.Where(c => c.start <= time && time <= c.end).OrderBy(c => c.declaration).ToArray();
                var ended = intervals.Where(c => c.end <= time).OrderByDescending(c => c.end)
                                     .ThenByDescending(c => c.originalStart).ThenBy(c => c.declaration).ToArray();
                float expected = active.Length > 0 ? active[0].value : ended.Length > 0 ? ended[0].value : commands[0].value;

                s.ApplyAt(drawable, time);
                Assert.That(drawable.X, Is.EqualTo(expected), $"{ordinaryCount} ordinary commands, loops first {loopsFirst}, time {time}");
            }

            void addLoops()
            {
                var loop = s.AddLoopingGroup(100, 2);
                add(loop, 0, 100, 10, 100, 400, 3);
                add(loop, 200, 300, 20, 100, 400, 3);
                loop.AddY(Easing.None, 0, 400, 0, 0);
                for (int i = 2; i < bodyCount; i++)
                    add(loop, 40 + i % 5 * 40, 60 + i % 5 * 40, 200 + i, 100, 400, 3);

                add(s.AddLoopingGroup(900, 0), 0, 100, 30, 900);
                var zeroDuration = s.AddLoopingGroup(1500, 2);
                for (int i = 0; i < bodyCount; i++)
                    add(zeroDuration, 0, 0, 60 + i, 1500);
            }

            void add(StoryboardCommandGroup group, double start, double end, float value, double offset = 0, double period = 0, int iterations = 1)
            {
                group.AddX(Easing.None, start, end, value, value);
                commands.Add((start + offset, end + offset, value, period, iterations));
            }
        }

        [Test]
        public void LoopEndTimeTiesCanAppearInLaterIterations()
        {
            var s = sprite(_ => { });
            var loop = s.AddLoopingGroup(0, 2);
            loop.AddY(Easing.None, 0, 1024, 0, 0);
            loop.AddX(Easing.None, 0.5, 1, 100, 100);
            loop.AddX(Easing.None, 0, Math.BitIncrement(1), 200, 200);
            for (int i = 0; i < 30; i++)
                loop.AddX(Easing.None, 0, 0.125, i, i);

            using var drawable = new DrawableStoryboardSprite(s);
            foreach (double time in new[] { 2.0, 1026, 2, 1026 })
            {
                s.ApplyAt(drawable, time);
                Assert.That(drawable.X, Is.EqualTo(time < 1024 ? 200 : 100));
            }
        }

        [TestCase(31)]
        [TestCase(32)]
        [TestCase(33)]
        [TestCase(32, true)]
        [TestCase(33, true)]
        public void LoopIndexMatchesLinearSelectionAtFractionalBoundaries(int count, bool pointsAtLoopEnd = false)
        {
            var s = sprite(_ => { });
            var loop = s.AddLoopingGroup(-0.1, 4);
            loop.AddY(Easing.None, 0, 17.3, 0, 0);
            for (int i = 0; i < count; i++)
            {
                double start = pointsAtLoopEnd ? 17.3 : i * 17.3 / count;
                double end = pointsAtLoopEnd ? start : start + 0.7 * 17.3 / count;
                loop.AddX(Easing.InQuad, start, end, i, i + 1);
            }

            var commands = loop.X.OrderBy(c => c.DeclarationIndex).ToArray();
            var times = commands.SelectMany(c => Enumerable.Range(0, loop.TotalIterations).SelectMany(iteration =>
            {
                double start = c.StartTime + iteration * loop.Duration;
                double end = c.EndTime + iteration * loop.Duration;
                return new[] { Math.BitDecrement(start), start, Math.BitIncrement(start), (start + end) / 2, Math.BitDecrement(end), end, Math.BitIncrement(end) };
            })).Distinct().OrderBy(t => t).ToArray();
            using var drawable = new DrawableStoryboardSprite(s);

            foreach (double time in times.Concat(times.Reverse()))
            {
                var winner = commands.FirstOrDefault(c => c.IsActiveAt(time))
                             ?? commands.Where(c => !double.IsNegativeInfinity(c.MostRecentEndTimeAt(time)))
                                        .OrderByDescending(c => c.MostRecentEndTimeAt(time)).ThenByDescending(c => c.StartTime).ThenBy(c => c.DeclarationIndex)
                                        .FirstOrDefault();
                float expected = winner?.ValueAt(time) ?? commands[0].StartValue;
                s.ApplyAt(drawable, time);
                Assert.That(drawable.X, Is.EqualTo(expected), $"{count} loop commands at {time:R}");
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void LoopEndpointsIncludeTheFinishingIteration(int repeats)
        {
            var s = sprite(b =>
            {
                b.AddLoopingGroup(50, repeats).AddX(Easing.None, 0, 100, 0, 100);
                b.Commands.AddX(Easing.None, 75, 1000, 1000, 1000);
            });
            using var drawable = new DrawableStoryboardSprite(s);

            var times = Enumerable.Range(1, repeats + 1).Select(i => 50 + i * 100.0).ToArray();
            foreach (double end in times.Concat(times.Reverse()))
            {
                s.ApplyAt(drawable, end - 0.25);
                Assert.That(drawable.X, Is.EqualTo(99.75f));
                s.ApplyAt(drawable, end);
                Assert.That(drawable.X, Is.EqualTo(100), $"endpoint {end}");
                s.ApplyAt(drawable, end + 0.25);
                Assert.That(drawable.X, Is.EqualTo(end == times[^1] ? 1000 : 0.25f));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TemporaryParametersResetWhenRewindingBeforeStart(bool looping)
        {
            var s = sprite(b =>
            {
                b.Commands.AddAlpha(Easing.None, 0, 1000, 1, 1);
                var commands = looping ? b.AddLoopingGroup(100, 1) : b.Commands;
                double start = looping ? 0 : 100;
                commands.AddFlipH(Easing.None, start, start + 100, true, false);
                commands.AddFlipV(Easing.None, start, start + 100, true, false);
                commands.AddBlendingParameters(Easing.None, start, start + 100, BlendingParameters.Additive, BlendingParameters.Inherit);
            });

            using var drawable = new DrawableStoryboardSprite(s);

            assertAt(50, false);
            assertAt(150, true);
            assertAt(50, false);
            assertAt(350, false);
            assertAt(150, true);
            assertAt(50, false);

            void assertAt(double time, bool active)
            {
                s.ApplyAt(drawable, time);
                Assert.That(drawable.FlipH, Is.EqualTo(active), $"horizontal flip at {time}");
                Assert.That(drawable.FlipV, Is.EqualTo(active), $"vertical flip at {time}");
                Assert.That(drawable.Blending, Is.EqualTo(active ? BlendingParameters.Additive : BlendingParameters.Inherit), $"blending at {time}");
            }
        }

        [Test]
        public void PermanentParametersApplyBeforeStartIncludingAfterRewind()
        {
            var s = sprite(b =>
            {
                b.Commands.AddAlpha(Easing.None, 0, 1000, 1, 1);
                b.Commands.AddFlipH(Easing.None, 100, 100, true, true);
                b.Commands.AddFlipV(Easing.None, 100, 100, true, true);
                b.Commands.AddBlendingParameters(Easing.None, 100, 100, BlendingParameters.Additive, BlendingParameters.Additive);
            });

            using var drawable = new DrawableStoryboardSprite(s);

            foreach (double time in new double[] { 50, 100, 150, 50 })
            {
                s.ApplyAt(drawable, time);
                Assert.That(drawable.FlipH, Is.True, $"horizontal flip at {time}");
                Assert.That(drawable.FlipV, Is.True, $"vertical flip at {time}");
                Assert.That(drawable.Blending, Is.EqualTo(BlendingParameters.Additive), $"blending at {time}");
            }
        }

        [TestCaseSource(nameof(cases))]
        public void MatchesReferenceImplementation(string name, Action<StoryboardSprite> build)
        {
            var actualSprite = sprite(build);
            var expectedSprite = sprite(build);

            var actual = new DrawableStoryboardSprite(actualSprite);
            var expected = new DrawableStoryboardSprite(expectedSprite);

            foreach (double time in sampleTimes(actualSprite))
            {
                actualSprite.ApplyAt(actual, time);
                applyReference(expectedSprite, expected, time);

                Assert.That(actual.Alpha, Is.EqualTo(expected.Alpha), $"{name} alpha diverged at t={time}");

                if (expected.Alpha > visibility_cutoff)
                    Assert.That(snapshot(actual), Is.EqualTo(snapshot(expected)), $"{name} diverged at t={time}");
            }
        }

        [Test]
        public void RecoversFullyWhenSpriteBecomesVisibleAgain()
        {
            var s = sprite(b =>
            {
                b.Commands.AddAlpha(Easing.None, 0, 100, 1, 1);
                b.Commands.AddAlpha(Easing.None, 100, 200, 0, 0);
                b.Commands.AddAlpha(Easing.None, 900, 1000, 1, 1);
                b.Commands.AddX(Easing.None, 0, 1000, 0, 100);
                b.Commands.AddRotation(Easing.None, 0, 1000, 0, 10);
                b.Commands.AddScale(Easing.None, 0, 1000, 1, 5);
            });

            var drawable = new DrawableStoryboardSprite(s);

            for (double time = 150; time < 900; time += 10)
                s.ApplyAt(drawable, time);

            Assert.That(drawable.Alpha, Is.Zero);

            s.ApplyAt(drawable, 950);

            Assert.That(drawable.Alpha, Is.EqualTo(1));
            Assert.That(drawable.X, Is.EqualTo(95).Within(0.001));
            Assert.That(drawable.Rotation, Is.EqualTo(9.5).Within(0.001));
            Assert.That(drawable.Scale.X, Is.EqualTo(4.8).Within(0.001));
        }

        [Test]
        public void InitialValuesMatchReferenceImplementation()
        {
            foreach (object?[] c in cases())
            {
                string name = (string)c[0]!;
                var build = (Action<StoryboardSprite>)c[1]!;

                var actualSprite = sprite(build);
                var expectedSprite = sprite(build);

                var actual = new DrawableStoryboardSprite(actualSprite);
                var expected = new DrawableStoryboardSprite(expectedSprite);

                actualSprite.ApplyInitialValues(actual);

                foreach (var commands in reference(expectedSprite))
                    commands[0].ApplyInitialValue(expected);

                Assert.That(snapshot(actual), Is.EqualTo(snapshot(expected)), $"{name} initial values diverged");
            }
        }

        private static StoryboardSprite sprite(Action<StoryboardSprite> build)
        {
            var s = new StoryboardSprite(StoryboardElementSource.Shared, "test.png", Anchor.Centre, Vector2.Zero);
            build(s);
            return s;
        }

        private static IEnumerable<object?[]> cases()
        {
            yield return new object?[]
            {
                "sequential commands", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddAlpha(Easing.None, 0, 1000, 0, 1);
                    s.Commands.AddAlpha(Easing.None, 1000, 2000, 1, 0);
                    s.Commands.AddX(Easing.OutQuad, 0, 2000, 0, 320);
                })
            };

            yield return new object?[]
            {
                "gaps between commands", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddX(Easing.None, 0, 100, 0, 50);
                    s.Commands.AddX(Easing.None, 500, 600, 100, 150);
                    s.Commands.AddX(Easing.None, 1500, 1600, 200, 250);
                    s.Commands.AddAlpha(Easing.None, 0, 2000, 1, 1);
                })
            };

            yield return new object?[]
            {
                "overlapping commands", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddX(Easing.None, 0, 1500, 0, 100);
                    s.Commands.AddX(Easing.None, 500, 1000, 200, 300);
                    s.Commands.AddX(Easing.None, 700, 2000, 400, 500);
                    s.Commands.AddAlpha(Easing.None, 0, 2000, 1, 1);
                })
            };

            yield return new object?[]
            {
                "declared out of start time order", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddY(Easing.None, 1000, 2000, 100, 200);
                    s.Commands.AddY(Easing.None, 0, 1000, 0, 100);
                    s.Commands.AddY(Easing.None, 500, 1500, 400, 500);
                    s.Commands.AddAlpha(Easing.None, 0, 2000, 1, 1);
                })
            };

            yield return new object?[]
            {
                "commands ending before lifetime", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddAlpha(Easing.None, 0, 100, 1, 1);
                    s.Commands.AddX(Easing.None, 0, 100, 0, 50);
                    s.Commands.AddScale(Easing.None, 0, 100, 1, 2);
                    s.Commands.AddRotation(Easing.None, 5000, 5000, 1, 1);
                })
            };

            yield return new object?[]
            {
                "zero duration commands", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddFlipH(Easing.None, 500, 500, true, true);
                    s.Commands.AddFlipV(Easing.None, 0, 1000, true, false);
                    s.Commands.AddBlendingParameters(Easing.None, 300, 300, BlendingParameters.Additive, BlendingParameters.Additive);
                    s.Commands.AddBlendingParameters(Easing.None, 800, 1200, BlendingParameters.Additive, BlendingParameters.Inherit);
                    s.Commands.AddAlpha(Easing.None, 0, 2000, 1, 1);
                })
            };

            yield return new object?[]
            {
                "every property at once", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddX(Easing.None, 0, 1000, 0, 100);
                    s.Commands.AddY(Easing.InQuad, 0, 1000, 0, 100);
                    s.Commands.AddScale(Easing.None, 200, 800, 1, 3);
                    s.Commands.AddVectorScale(Easing.None, 300, 900, Vector2.One, new Vector2(2, 4));
                    s.Commands.AddRotation(Easing.OutQuint, 0, 1500, 0, 3);
                    s.Commands.AddColour(Easing.None, 0, 1500, Color4.Red, Color4.Blue);
                    s.Commands.AddAlpha(Easing.None, 0, 2000, 0.25f, 1);
                    s.Commands.AddBlendingParameters(Easing.None, 0, 1000, BlendingParameters.Additive, BlendingParameters.Inherit);
                    s.Commands.AddFlipH(Easing.None, 100, 400, true, false);
                    s.Commands.AddFlipV(Easing.None, 100, 100, true, true);
                })
            };

            yield return new object?[]
            {
                "looping group", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddAlpha(Easing.None, 0, 3000, 1, 1);

                    var loop = s.AddLoopingGroup(500, 3);
                    loop.AddX(Easing.None, 0, 200, 0, 100);
                    loop.AddY(Easing.None, 0, 400, 0, 50);
                })
            };

            yield return new object?[]
            {
                "looping group overlapping static commands", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddAlpha(Easing.None, 0, 3000, 1, 1);
                    s.Commands.AddX(Easing.None, 0, 2000, 0, 640);

                    var loop = s.AddLoopingGroup(400, 5);
                    loop.AddX(Easing.None, 0, 150, 10, 20);
                })
            };

            yield return new object?[]
            {
                "zero duration loop body", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddAlpha(Easing.None, 0, 2000, 1, 1);

                    var loop = s.AddLoopingGroup(300, 4);
                    loop.AddRotation(Easing.None, 0, 0, 1, 2);
                })
            };

            yield return new object?[]
            {
                "two looping groups", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddAlpha(Easing.None, 0, 4000, 1, 1);

                    var first = s.AddLoopingGroup(0, 2);
                    first.AddScale(Easing.None, 0, 500, 1, 2);

                    var second = s.AddLoopingGroup(250, 6);
                    second.AddScale(Easing.None, 0, 100, 5, 6);
                })
            };

            yield return new object?[]
            {
                "identical timings on one property", (Action<StoryboardSprite>)(s =>
                {
                    s.Commands.AddX(Easing.None, 200, 400, 10, 20);
                    s.Commands.AddX(Easing.None, 200, 400, 30, 40);
                    s.Commands.AddX(Easing.None, 200, 400, 50, 60);
                    s.Commands.AddAlpha(Easing.None, 0, 2000, 1, 1);
                })
            };

            yield return new object?[] { "fuzzed", (Action<StoryboardSprite>)fuzz };
        }

        private static void fuzz(StoryboardSprite s)
        {
            var random = new Random(3452);

            for (int i = 0; i < 60; i++)
            {
                double start = random.Next(0, 2000);
                double end = start + random.Next(0, 500);
                float from = random.Next(0, 100);
                float to = random.Next(0, 100);

                switch (random.Next(0, 6))
                {
                    case 0:
                        s.Commands.AddX(Easing.None, start, end, from, to);
                        break;

                    case 1:
                        s.Commands.AddY(Easing.None, start, end, from, to);
                        break;

                    case 2:
                        s.Commands.AddAlpha(Easing.None, start, end, from / 100, to / 100);
                        break;

                    case 3:
                        s.Commands.AddScale(Easing.None, start, end, from, to);
                        break;

                    case 4:
                        s.Commands.AddFlipH(Easing.None, start, end, from > 50, to > 50);
                        break;

                    case 5:
                        var loop = s.AddLoopingGroup(start, random.Next(0, 4));
                        loop.AddRotation(Easing.None, 0, random.Next(0, 300), from, to);
                        break;
                }
            }
        }

        private static IEnumerable<double> sampleTimes(StoryboardSprite s)
        {
            var times = new HashSet<double>();

            foreach (var commands in reference(s))
            {
                foreach (var command in commands)
                {
                    foreach (double t in new[] { command.StartTime, command.EndTime })
                    {
                        times.Add(t - 1);
                        times.Add(t);
                        times.Add(t + 1);
                    }
                }
            }

            for (double t = -500; t <= 6000; t += 17)
                times.Add(t);

            return times.OrderBy(t => t);
        }

        private static (float x, float y, Vector2 scale, Vector2 vectorScale, float rotation, ColourInfo colour, float alpha, BlendingParameters blending, bool flipH, bool flipV)
            snapshot(DrawableStoryboardSprite d)
            => (d.X, d.Y, d.Scale, d.VectorScale, d.Rotation, d.Colour, d.Alpha, d.Blending, d.FlipH, d.FlipV);

        #region Reference implementation

        private static IStoryboardCommand[][] reference(StoryboardSprite sprite)
            => sprite.Commands.AllCommands
                     .Concat(sprite.LoopingGroups.SelectMany(l => l.AllCommands))
                     .GroupBy(c => c.PropertyName)
                     .Select(g => g.OrderBy(c => c.DeclarationIndex).ToArray())
                     .ToArray();

        private static void applyReference(StoryboardSprite sprite, DrawableStoryboardSprite drawable, double time)
        {
            foreach (var commands in reference(sprite))
            {
                IStoryboardCommand? governing = null;

                foreach (var command in commands)
                {
                    if (!command.IsActiveAt(time))
                        continue;

                    governing = command;
                    break;
                }

                if (governing != null)
                {
                    governing.ApplyAt(drawable, time);
                    continue;
                }

                double mostRecentEnd = double.NegativeInfinity;
                double governingStart = double.NegativeInfinity;

                foreach (var command in commands)
                {
                    double end = command.MostRecentEndTimeAt(time);

                    if (double.IsNegativeInfinity(end) || end < mostRecentEnd)
                        continue;

                    if (end == mostRecentEnd && command.StartTime <= governingStart)
                        continue;

                    mostRecentEnd = end;
                    governingStart = command.StartTime;
                    governing = command;
                }

                if (governing == null)
                {
                    commands[0].ApplyInitialValue(drawable);
                    continue;
                }

                governing.ApplyAt(drawable, time);
            }
        }

        #endregion
    }
}
