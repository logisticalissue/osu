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

        public virtual double StartTime => StoryboardCommandEvaluator.GetStartTime(this);

        public double EarliestTransformTime
        {
            get
            {
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
            var loop = new StoryboardLoopingGroup(loopStartTime, repeatCount)
            {
                DeclarationIndex = nextDeclarationIndex++,
                DeclarationIndexSource = () => nextDeclarationIndex++,
            };
            LoopingGroups.Add(loop);
            return loop;
        }

        public StoryboardTriggerGroup AddTriggerGroup(string triggerName, double startTime, double endTime, int groupNumber)
        {
            var trigger = new StoryboardTriggerGroup(triggerName, startTime, endTime, groupNumber)
            {
                DeclarationIndex = nextDeclarationIndex++,
                DeclarationIndexSource = () => nextDeclarationIndex++,
            };
            TriggerGroups.Add(trigger);
            return trigger;
        }

        private StoryboardCommandEvaluator? commandEvaluator;

        private StoryboardCommandEvaluator evaluator => commandEvaluator ??= new StoryboardCommandEvaluator(this);

        public void ApplyInitialValues<TDrawable>(TDrawable drawable)
            where TDrawable : Drawable, IFlippable, IVectorScalable
            => evaluator.ApplyInitialValues(drawable);

        public void ApplyAt<TDrawable>(TDrawable drawable, double time)
            where TDrawable : Drawable, IFlippable, IVectorScalable
            => drawable.ApplyStoryboardCommands(evaluator, time);

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
