// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Storyboards.Commands;
using osu.Game.Storyboards.Drawables;
using osuTK;

namespace osu.Game.Storyboards
{
    public class StoryboardSprite : IStoryboardElementWithDuration
    {
        public readonly List<StoryboardLoopingGroup> LoopingGroups = new List<StoryboardLoopingGroup>();
        public readonly List<StoryboardTriggerGroup> TriggerGroups = new List<StoryboardTriggerGroup>();

        public StoryboardElementSource Source { get; }
        public string Path { get; }
        public virtual bool IsDrawable => HasCommands;

        public Anchor Origin;
        public Vector2 InitialPosition;

        public readonly StoryboardCommandGroup Commands = new StoryboardCommandGroup();

        public virtual double StartTime
        {
            get
            {
                // Users that are crafting storyboards using raw osb scripting or external tools may create alpha events far before the actual display time
                // of sprites.
                //
                // To make sure lifetime optimisations work as efficiently as they can, let's locally find the first time a sprite becomes visible.
                var alphaCommands = new List<StoryboardCommand<float>>();

                foreach (var command in Commands.Alpha)
                {
                    alphaCommands.Add(command);
                    if (visibleAtStartOrEnd(command))
                        break;
                }

                foreach (var loop in LoopingGroups)
                {
                    foreach (var command in loop.Alpha)
                    {
                        alphaCommands.Add(command);
                        if (visibleAtStartOrEnd(command))
                            break;
                    }
                }

                if (alphaCommands.Count > 0)
                {
                    // Special care is given to cases where there's one or more no-op transforms (ie transforming from alpha 0 to alpha 0).
                    // - If a 0->0 transform exists, we still need to check it to ensure the absolute first start value is non-visible.
                    // - After ascertaining this, we then check the first non-noop transform to get the true start lifetime.
                    var firstAlpha = alphaCommands.MinBy(c => c.StartTime);
                    var firstRealAlpha = alphaCommands.Where(visibleAtStartOrEnd).MinBy(c => c.StartTime);

                    if (firstAlpha!.StartValue == 0 && firstRealAlpha != null)
                        return firstRealAlpha.StartTime;
                }

                return EarliestTransformTime;

                bool visibleAtStartOrEnd(StoryboardCommand<float> command) => command.StartValue > 0 || command.EndValue > 0;
            }
        }

        public double EarliestTransformTime
        {
            get
            {
                // If we got to this point, either no alpha commands were present, or the earliest had a non-zero start value.
                // The sprite's StartTime will be determined by the earliest command, regardless of type.
                double earliestStartTime = Commands.StartTime;
                foreach (var l in LoopingGroups)
                    earliestStartTime = Math.Min(earliestStartTime, l.StartTime);
                return earliestStartTime;
            }
        }

        public double EndTime
        {
            get
            {
                double latestEndTime = Commands.EndTime;

                foreach (var l in LoopingGroups)
                    latestEndTime = Math.Max(latestEndTime, l.EndTime);

                return latestEndTime;
            }
        }

        public double EndTimeForDisplay
        {
            get
            {
                double latestEndTime = Commands.EndTime;

                foreach (var l in LoopingGroups)
                    latestEndTime = Math.Max(latestEndTime, l.StartTime + l.Duration * l.TotalIterations);

                return latestEndTime;
            }
        }

        public bool HasCommands => Commands.HasCommands || LoopingGroups.Any(l => l.HasCommands);

        private int nextDeclarationIndex;

        public StoryboardSprite(StoryboardElementSource source, string path, Anchor origin, Vector2 initialPosition)
        {
            Source = source;
            Path = path;
            Origin = origin;
            InitialPosition = initialPosition;

            Commands.DeclarationIndexSource = () => nextDeclarationIndex++;
        }

        public virtual Drawable CreateDrawable() => new DrawableStoryboardSprite(this);

        public StoryboardLoopingGroup AddLoopingGroup(double loopStartTime, int repeatCount)
        {
            var loop = new StoryboardLoopingGroup(loopStartTime, repeatCount) { DeclarationIndexSource = () => nextDeclarationIndex++ };
            LoopingGroups.Add(loop);
            return loop;
        }

        public StoryboardTriggerGroup AddTriggerGroup(string triggerName, double startTime, double endTime, int groupNumber)
        {
            var trigger = new StoryboardTriggerGroup(triggerName, startTime, endTime, groupNumber) { DeclarationIndexSource = () => nextDeclarationIndex++ };
            TriggerGroups.Add(trigger);
            return trigger;
        }

        private IStoryboardCommand[][]? commandsByProperty;

        public void ApplyInitialValues<TDrawable>(TDrawable drawable)
            where TDrawable : Drawable, IFlippable, IVectorScalable
        {
            commandsByProperty ??= groupCommandsByProperty();

            foreach (var commands in commandsByProperty)
                commands[0].ApplyInitialValue(drawable);
        }

        public void ApplyAt<TDrawable>(TDrawable drawable, double time)
            where TDrawable : Drawable, IFlippable, IVectorScalable
        {
            commandsByProperty ??= groupCommandsByProperty();

            foreach (var commands in commandsByProperty)
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

                // nothing running: hold what the most recently finished
                // command left behind. if several finished at the same time,
                // last started decides. commands also started together are
                // decided by declaration order
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
                    // nothing has run yet either, so the property is in the state it started in - which is
                    // the value the first declared command begins from, not the last
                    commands[0].ApplyInitialValue(drawable);
                    continue;
                }

                governing.ApplyAt(drawable, time);
            }
        }

        private IStoryboardCommand[][] groupCommandsByProperty()
            => Commands.AllCommands
                       .Concat(LoopingGroups.SelectMany(l => l.AllCommands))
                       .GroupBy(c => c.PropertyName)
                       .Select(g => g.OrderBy(c => c.DeclarationIndex).ToArray())
                       .ToArray();

        public void ApplyTransforms<TDrawable>(TDrawable drawable, StoryboardTriggerController triggerController)
            where TDrawable : Drawable, IFlippable, IVectorScalable
        {
            HashSet<string> appliedProperties = new HashSet<string>();

            // For performance reasons, we need to apply the commands in chronological order.
            // Not doing so will cause many functions to be interleaved, resulting in O(n^2) complexity.
            IEnumerable<IStoryboardCommand> commands = Commands.AllCommands;
            commands = commands.Concat(LoopingGroups.SelectMany(l => l.AllCommands));

            foreach (var command in commands.OrderBy(c => c.StartTime))
            {
                if (appliedProperties.Add(command.PropertyName))
                    command.ApplyInitialValue(drawable);

                using (drawable.BeginAbsoluteSequence(command.StartTime))
                    command.ApplyTransforms(drawable);
            }

            foreach (var triggerGroup in TriggerGroups)
                triggerController.Bind(drawable, triggerGroup);
        }
    }
}
