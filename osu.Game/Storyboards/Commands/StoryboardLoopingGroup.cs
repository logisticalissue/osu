// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics.Transforms;

namespace osu.Game.Storyboards.Commands
{
    public class StoryboardLoopingGroup : StoryboardCommandGroup
    {
        private readonly double loopStartTime;

        /// <summary>
        /// The total number of times this loop is played back. Always greater than zero.
        /// </summary>
        public readonly int TotalIterations;

        /// <summary>
        /// Construct a new command loop.
        /// </summary>
        /// <param name="startTime">The start time of the loop.</param>
        /// <param name="repeatCount">The number of times the loop should repeat. Should be greater than zero. Zero means a single playback.</param>
        public StoryboardLoopingGroup(double startTime, int repeatCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(repeatCount);

            loopStartTime = startTime;
            TotalIterations = repeatCount + 1;
        }

        protected override void AddCommand<T>(ICollection<StoryboardCommand<T>> list, StoryboardCommand<T> command)
            => base.AddCommand(list, new StoryboardLoopingCommand<T>(command, this));

        public override string ToString() => $"{loopStartTime} x{TotalIterations}";

        private class StoryboardLoopingCommand<T> : StoryboardCommand<T>, IStoryboardLoopingCommand
        {
            IStoryboardCommand IStoryboardLoopingCommand.OriginalCommand => command;

            private readonly StoryboardCommand<T> command;
            private readonly StoryboardLoopingGroup loopingGroup;

            public StoryboardLoopingCommand(StoryboardCommand<T> command, StoryboardLoopingGroup loopingGroup)
                // In an ideal world, we would multiply the command duration by TotalIterations in command end time.
                // Unfortunately this would clash with how stable handled end times, and results in some storyboards playing outro
                // sequences for minutes or hours.
                : base(command.Easing, loopingGroup.loopStartTime + command.StartTime, loopingGroup.loopStartTime + command.EndTime, command.StartValue, command.EndValue)
            {
                this.command = command;
                this.loopingGroup = loopingGroup;
            }

            public override string PropertyName => command.PropertyName;

            private (double start, double end)? iterationAt(double time)
            {
                double period = loopingGroup.Duration;

                if (period <= 0)
                    return time >= StartTime && time <= EndTime ? (StartTime, EndTime) : null;

                if (time < StartTime)
                    return null;

                double iteration = Math.Floor((time - StartTime) / period);

                if (iteration >= loopingGroup.TotalIterations)
                    return null;

                double start = StartTime + iteration * period;
                double end = EndTime + iteration * period;

                return time <= end ? (start, end) : null;
            }

            public override bool IsActiveAt(double time) => iterationAt(time) != null;

            public override double MostRecentEndTimeAt(double time)
            {
                double period = loopingGroup.Duration;

                if (period <= 0 || time < EndTime)
                    return EndTime <= time ? EndTime : double.NegativeInfinity;

                double elapsed = Math.Min(Math.Floor((time - EndTime) / period), loopingGroup.TotalIterations - 1);

                return EndTime + elapsed * period;
            }

            // the body is the same length every iteration, so mapping back onto it is a plain offset
            private double toBodyTime(double time)
            {
                var iteration = iterationAt(time);

                if (iteration == null)
                    return time <= StartTime ? command.StartTime : command.EndTime;

                return command.StartTime + (time - iteration.Value.start);
            }

            public override T ValueAt(double time) => command.ValueAt(toBodyTime(time));

            public override void ApplyAt<TDrawable>(TDrawable d, double time) => command.ApplyAt(d, toBodyTime(time));

            public override void ApplyInitialValue<TDrawable>(TDrawable d) => command.ApplyInitialValue(d);

            public override TransformSequence<TDrawable> ApplyTransforms<TDrawable>(TDrawable d)
            {
                if (loopingGroup.TotalIterations == 0)
                    return command.ApplyTransforms(d);

                double loopingGroupDuration = loopingGroup.Duration;
                return command.ApplyTransforms(d).Loop(loopingGroupDuration - Duration, loopingGroup.TotalIterations);
            }
        }
    }
}
