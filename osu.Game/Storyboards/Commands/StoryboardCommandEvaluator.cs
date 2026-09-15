// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Game.Storyboards.Drawables;

namespace osu.Game.Storyboards.Commands
{
    internal sealed class StoryboardCommandEvaluator
    {
        private readonly PropertyGroup? alpha;
        private readonly PropertyGroup[] remainingProperties;

        public bool HasAlphaCommands => alpha != null;

        public StoryboardCommandEvaluator(StoryboardSprite sprite)
        {
            alpha = createPropertyGroup(sprite, static g => g.Alpha);

            var groups = new List<PropertyGroup>(9);
            addGroup(static g => g.X);
            addGroup(static g => g.Y);
            addGroup(static g => g.Scale);
            addGroup(static g => g.VectorScale);
            addGroup(static g => g.Rotation);
            addGroup(static g => g.Colour);
            addGroup(static g => g.BlendingParameters);
            addGroup(static g => g.FlipH);
            addGroup(static g => g.FlipV);
            remainingProperties = groups.ToArray();

            void addGroup(Func<StoryboardCommandGroup, IReadOnlyList<IStoryboardCommand>> select)
            {
                var group = createPropertyGroup(sprite, select);
                if (group != null)
                    groups.Add(group);
            }
        }

        public void ApplyInitialValues<TDrawable>(TDrawable drawable)
            where TDrawable : Drawable, IFlippable, IVectorScalable
        {
            alpha?.InitialCommand.ApplyInitialValue(drawable);
            foreach (var group in remainingProperties)
                group.InitialCommand.ApplyInitialValue(drawable);
        }

        public void ApplyAlphaAt<TDrawable>(TDrawable drawable, double time)
            where TDrawable : Drawable, IFlippable, IVectorScalable
        {
            if (alpha != null)
                applyGroupAt(drawable, alpha, time);
        }

        public void ApplyRemainingAt<TDrawable>(TDrawable drawable, double time)
            where TDrawable : Drawable, IFlippable, IVectorScalable
        {
            foreach (var group in remainingProperties)
                applyGroupAt(drawable, group, time);
        }

        public static double GetStartTime(StoryboardSprite sprite)
        {
            StoryboardCommand<float>? initialAlpha = null;
            double firstVisibleStart = double.PositiveInfinity;
            bool chronological = sprite.TriggerGroups.Count > 0;

            inspect(sprite.Commands.Alpha);
            foreach (var loop in sprite.LoopingGroups)
                inspect(loop.Alpha);

            return initialAlpha?.StartValue == 0 && !double.IsPositiveInfinity(firstVisibleStart)
                ? firstVisibleStart
                : sprite.EarliestTransformTime;

            void inspect(IReadOnlyList<StoryboardCommand<float>> commands)
            {
                for (int i = 0; i < commands.Count; i++)
                {
                    var command = commands[i];
                    if (initialAlpha == null || (chronological
                            ? command.StartTime < initialAlpha.StartTime
                            : command.DeclarationIndex < initialAlpha.DeclarationIndex))
                        initialAlpha = command;

                    if (command.StartValue > 0 || command.EndValue > 0)
                        firstVisibleStart = Math.Min(firstVisibleStart, command.StartTime);
                }
            }
        }

        private static void applyGroupAt<TDrawable>(TDrawable drawable, PropertyGroup group, double time)
            where TDrawable : Drawable, IFlippable, IVectorScalable
        {
            if (time > group.LastEnd)
            {
                group.AfterLastEnd!.ApplyAt(drawable, time);
                return;
            }

            if (time < group.FirstStart)
            {
                group.InitialCommand.ApplyInitialValue(drawable);
                return;
            }

            var governing = group.ActiveAt(time) ?? group.MostRecentlyEndedAt(time);

            if (governing == null)
            {
                group.InitialCommand.ApplyInitialValue(drawable);
                return;
            }

            governing.ApplyAt(drawable, time);
        }

        private const int index_threshold = 32;

        private abstract class PropertyGroup
        {
            public readonly IStoryboardCommand InitialCommand;
            public readonly double FirstStart;
            public readonly double LastEnd;
            public readonly IStoryboardCommand? AfterLastEnd;

            protected PropertyGroup(IStoryboardCommand[] commands)
            {
                InitialCommand = commands[0];
                FirstStart = double.PositiveInfinity;
                foreach (var command in commands)
                    FirstStart = Math.Min(FirstStart, command.StartTime);

                AfterLastEnd = scanForMostRecentlyEnded(commands, double.MaxValue);
                LastEnd = AfterLastEnd?.MostRecentEndTimeAt(double.MaxValue) ?? double.PositiveInfinity;
            }

            public abstract IStoryboardCommand? ActiveAt(double time);
            public abstract IStoryboardCommand? MostRecentlyEndedAt(double time);
        }

        private sealed class LinearPropertyGroup : PropertyGroup
        {
            private readonly IStoryboardCommand[] commands;

            public LinearPropertyGroup(IStoryboardCommand[] commands)
                : base(commands)
            {
                this.commands = commands;
            }

            public override IStoryboardCommand? ActiveAt(double time)
            {
                foreach (var command in commands)
                {
                    if (command.IsActiveAt(time))
                        return command;
                }

                return null;
            }

            public override IStoryboardCommand? MostRecentlyEndedAt(double time) => scanForMostRecentlyEnded(commands, time);
        }

        private sealed class MixedPropertyGroup : PropertyGroup
        {
            private readonly PropertyGroup[] groups;

            public MixedPropertyGroup(IStoryboardCommand[] commands, PropertyGroup[] groups)
                : base(commands)
            {
                this.groups = groups;
            }

            public override IStoryboardCommand? ActiveAt(double time)
            {
                IStoryboardCommand? governing = null;
                foreach (var group in groups)
                    governing = firstDeclared(governing, group.ActiveAt(time));
                return governing;
            }

            public override IStoryboardCommand? MostRecentlyEndedAt(double time)
            {
                IStoryboardCommand? governing = null;
                foreach (var group in groups)
                    governing = mostRecentlyEnded(governing, group.MostRecentlyEndedAt(time), time);
                return governing;
            }
        }

        private class CommandIndex : PropertyGroup
        {
            private readonly IStoryboardCommand[] byStartTime;
            protected readonly double[] EndTimes;
            protected readonly IStoryboardCommand[] EndTimeWinners;
            private readonly double[] latestEndTimes;

            public CommandIndex(IStoryboardCommand[] commands)
                : base(commands)
            {
                int count = commands.Length;

                byStartTime = (IStoryboardCommand[])commands.Clone();
                var byEndTime = (IStoryboardCommand[])commands.Clone();

                double[] startTimes = new double[count];
                double[] ends = new double[count];

                for (int i = 0; i < count; i++)
                {
                    startTimes[i] = commands[i].StartTime;
                    ends[i] = commands[i].EndTime;
                }

                Array.Sort(startTimes, byStartTime);
                latestEndTimes = new double[count];
                double latestEnd = double.NegativeInfinity;
                for (int i = 0; i < count; i++)
                    latestEndTimes[i] = latestEnd = Math.Max(latestEnd, byStartTime[i].EndTime);
                Array.Sort(ends, byEndTime);

                int distinct = 0;

                for (int i = 0; i < count;)
                {
                    var winner = byEndTime[i];
                    int next = i + 1;

                    while (next < count && ends[next] == ends[i])
                    {
                        var candidate = byEndTime[next];

                        if (candidate.StartTime > winner.StartTime
                            || (candidate.StartTime == winner.StartTime && candidate.DeclarationIndex < winner.DeclarationIndex))
                        {
                            winner = candidate;
                        }

                        next++;
                    }

                    ends[distinct] = ends[i];
                    byEndTime[distinct] = winner;
                    distinct++;

                    i = next;
                }

                EndTimes = distinct == count ? ends : ends[..distinct];
                EndTimeWinners = distinct == count ? byEndTime : byEndTime[..distinct];

            }

            public override IStoryboardCommand? ActiveAt(double time) => FindActiveAt(time, 0);

            protected IStoryboardCommand? FindActiveAt(double time, double offset)
            {
                IStoryboardCommand? governing = null;

                for (int i = firstEndingAtOrAfter(time, offset); i < byStartTime.Length; i++)
                {
                    var command = byStartTime[i];

                    if (!command.IsActiveAt(time))
                    {
                        if (command.StartTime + offset > time)
                            break;
                        continue;
                    }

                    if (governing == null || command.DeclarationIndex < governing.DeclarationIndex)
                        governing = command;

                    if (governing == InitialCommand)
                        return governing;
                }

                return governing;
            }

            private int firstEndingAtOrAfter(double time, double offset)
            {
                int low = 0;
                int high = byStartTime.Length - 1;
                int result = byStartTime.Length;

                while (low <= high)
                {
                    int mid = low + ((high - low) >> 1);

                    if (latestEndTimes[mid] + offset >= time)
                    {
                        result = mid;
                        high = mid - 1;
                    }
                    else
                        low = mid + 1;
                }

                return result;
            }

            public override IStoryboardCommand? MostRecentlyEndedAt(double time)
            {
                int low = 0;
                int high = EndTimes.Length - 1;
                int result = -1;

                while (low <= high)
                {
                    int mid = low + ((high - low) >> 1);

                    if (EndTimes[mid] <= time)
                    {
                        result = mid;
                        low = mid + 1;
                    }
                    else
                        high = mid - 1;
                }

                return result < 0 ? null : EndTimeWinners[result];
            }
        }

        private sealed class LoopCommandIndex : CommandIndex
        {
            private readonly double period;
            private readonly int iterations;

            public LoopCommandIndex(IStoryboardCommand[] commands, StoryboardLoopingGroup loop)
                : base(commands)
            {
                period = loop.Duration;
                iterations = loop.TotalIterations;
            }

            public override IStoryboardCommand? ActiveAt(double time)
            {
                if (time < FirstStart || time > LastEnd)
                    return null;

                double iteration = Math.Min(Math.Floor((time - FirstStart) / period), iterations - 1);
                var governing = FindActiveAt(time, iteration * period);

                if (iteration > 0 && time <= EndTimes[^1] + (iteration - 1) * period)
                    governing = firstDeclared(governing, FindActiveAt(time, (iteration - 1) * period));

                return governing;
            }

            public override IStoryboardCommand? MostRecentlyEndedAt(double time)
            {
                if (time < EndTimes[0])
                    return null;

                double iteration = Math.Min(Math.Floor((time - EndTimes[0]) / period), iterations - 1);
                var governing = findMostRecentlyEndedAt(time, iteration);

                if (iteration > 0)
                    governing = mostRecentlyEnded(governing, findMostRecentlyEndedAt(time, iteration - 1), time);

                return governing;
            }

            private IStoryboardCommand? findMostRecentlyEndedAt(double time, double iteration)
            {
                int low = 0;
                int high = EndTimes.Length - 1;
                int result = -1;

                while (low <= high)
                {
                    int mid = low + ((high - low) >> 1);

                    if (EndTimes[mid] <= time && Math.Floor((time - EndTimes[mid]) / period) >= iteration)
                    {
                        result = mid;
                        low = mid + 1;
                    }
                    else
                        high = mid - 1;
                }

                if (result < 0)
                    return null;

                var governing = EndTimeWinners[result];
                double end = governing.MostRecentEndTimeAt(time);

                for (int i = result - 1; i >= 0; i--)
                {
                    var candidate = EndTimeWinners[i];
                    if (candidate.MostRecentEndTimeAt(time) != end)
                        break;
                    governing = mostRecentlyEnded(governing, candidate, time)!;
                }

                return governing;
            }
        }

        private static IStoryboardCommand? scanForMostRecentlyEnded(IStoryboardCommand[] commands, double time)
        {
            IStoryboardCommand? governing = null;
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

            return governing;
        }

        private static IStoryboardCommand? firstDeclared(IStoryboardCommand? first, IStoryboardCommand? second)
            => first == null || (second != null && second.DeclarationIndex < first.DeclarationIndex) ? second : first;

        private static IStoryboardCommand? mostRecentlyEnded(IStoryboardCommand? first, IStoryboardCommand? second, double time)
        {
            if (second == null)
                return first;

            double end = second.MostRecentEndTimeAt(time);
            if (double.IsNegativeInfinity(end))
                return first;

            if (first == null)
                return second;

            double firstEnd = first.MostRecentEndTimeAt(time);
            if (end != firstEnd)
                return end > firstEnd ? second : first;

            if (second.StartTime != first.StartTime)
                return second.StartTime > first.StartTime ? second : first;

            return firstDeclared(first, second);
        }

        private static PropertyGroup? createPropertyGroup(StoryboardSprite sprite, Func<StoryboardCommandGroup, IReadOnlyList<IStoryboardCommand>> select)
        {
            var ordinary = select(sprite.Commands);
            int count = ordinary.Count;
            bool hasLargeGroup = count >= index_threshold;
            foreach (var loop in sprite.LoopingGroups)
            {
                int loopCount = select(loop).Count;
                count += loopCount;
                hasLargeGroup |= loopCount >= index_threshold;
            }

            if (count == 0)
                return null;

            var ordered = new IStoryboardCommand[count];
            long[] keys = new long[count];
            int next = 0;
            append(ordinary);
            foreach (var loop in sprite.LoopingGroups)
                append(select(loop));
            Array.Sort(keys, ordered);
            if (!hasLargeGroup)
                return new LinearPropertyGroup(ordered);

            if (ordinary.Count == count)
                return new CommandIndex(ordered);

            var groups = new List<PropertyGroup>();
            var remaining = new List<IStoryboardCommand>();
            add(ordinary);
            foreach (var loop in sprite.LoopingGroups)
            {
                var body = select(loop);
                if (body.Count == count)
                    return loop.Duration > 0 ? new LoopCommandIndex(ordered, loop) : new CommandIndex(ordered);
                add(body, loop);
            }

            if (remaining.Count > 0)
                groups.Add(new LinearPropertyGroup(inDeclarationOrder(remaining)));

            return new MixedPropertyGroup(ordered, groups.ToArray());

            void append(IReadOnlyList<IStoryboardCommand> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    ordered[next] = list[i];
                    keys[next] = ((long)list[i].DeclarationIndex << 32) | (uint)next;
                    next++;
                }
            }

            void add(IReadOnlyList<IStoryboardCommand> list, StoryboardLoopingGroup? loop = null)
            {
                if (list.Count < index_threshold)
                {
                    remaining.AddRange(list);
                    return;
                }

                var sorted = inDeclarationOrder(list);
                groups.Add(loop != null && loop.Duration > 0 ? new LoopCommandIndex(sorted, loop) : new CommandIndex(sorted));
            }
        }

        private static IStoryboardCommand[] inDeclarationOrder(IReadOnlyList<IStoryboardCommand> commands)
        {
            var ordered = new IStoryboardCommand[commands.Count];
            long[] keys = new long[commands.Count];
            for (int i = 0; i < commands.Count; i++)
            {
                ordered[i] = commands[i];
                keys[i] = ((long)commands[i].DeclarationIndex << 32) | (uint)i;
            }

            Array.Sort(keys, ordered);
            return ordered;
        }
    }
}
