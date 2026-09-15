// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using BenchmarkDotNet.Attributes;
using osu.Framework.Graphics;
using osu.Game.Storyboards;
using osu.Game.Storyboards.Drawables;
using osuTK;

namespace osu.Game.Benchmarks
{
    public class BenchmarkStoryboardCommandSelection : BenchmarkTest
    {
        private const int samples = 64;
        private const double duration = 120000;

        [Params(31, 32, 1000, 5000)]
        public int CommandCount { get; set; }

        private DrawableStoryboardSprite sequential = null!;
        private DrawableStoryboardSprite overlapping = null!;
        private DrawableStoryboardSprite mixedLoop = null!;
        private DrawableStoryboardSprite loopBody = null!;
        private readonly double[] times = new double[samples];

        public override void SetUp()
        {
            sequential = create(false);
            overlapping = create(true);
            mixedLoop = create(false, true);
            loopBody = create(false, loopBody: true);

            for (int i = 0; i < samples; i++)
                times[i] = duration * (i + 0.5) / samples;
        }

        private DrawableStoryboardSprite create(bool overlap, bool withLoop = false, bool loopBody = false)
        {
            var sprite = new StoryboardSprite(StoryboardElementSource.Shared, "test.png", Anchor.Centre, Vector2.Zero);

            if (withLoop)
            {
                var loop = sprite.AddLoopingGroup(-10, 0);
                loop.AddX(Easing.None, 0, 1, 0, 0);
                loop.AddY(Easing.None, 0, 1, 0, 0);
            }

            var commands = loopBody ? sprite.AddLoopingGroup(0, 3) : sprite.Commands;
            double span = loopBody ? duration / 4 : duration;
            for (int i = 0; i < CommandCount; i++)
            {
                double start = span * i / CommandCount;
                double end = overlap ? span : span * (i + 1) / CommandCount;
                commands.AddX(Easing.None, start, end, i, i + 1);
                commands.AddY(Easing.None, start, end, i, i + 1);
                commands.AddAlpha(Easing.None, start, end, 1, 1);
            }

            var drawable = new DrawableStoryboardSprite(sprite);
            sprite.ApplyInitialValues(drawable);
            return drawable;
        }

        [Benchmark(OperationsPerInvoke = samples)]
        public void Sequential() => apply(sequential);

        [Benchmark(OperationsPerInvoke = samples)]
        public void Overlapping() => apply(overlapping);

        [Benchmark(OperationsPerInvoke = samples)]
        public void MixedLoop() => apply(mixedLoop);

        [Benchmark(OperationsPerInvoke = samples)]
        public void LoopBody() => apply(loopBody);

        [Benchmark]
        public void LoopSetup()
        {
            using var drawable = create(false, loopBody: true);
        }

        private void apply(DrawableStoryboardSprite drawable)
        {
            foreach (double time in times)
                drawable.Sprite.ApplyAt(drawable, time);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            sequential.Dispose();
            overlapping.Dispose();
            mixedLoop.Dispose();
            loopBody.Dispose();
        }
    }
}
