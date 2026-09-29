using System;
using System.Collections.Generic;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Dialogue;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Executors;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>
    /// Production executor map for the LessonGraph V2 node types implemented so far.
    /// Construct this with the same clock supplied to <see cref="LessonGraphRunner.Configure"/>.
    /// </summary>
    public sealed class LessonGraphExecutorRegistry : INodeExecutorRegistry
    {
        private readonly IReadOnlyDictionary<NodeType, INodeExecutor> _executors;

        public LessonGraphExecutorRegistry(
            IQuestBindingResolver questBindingResolver,
            INodeClock clock,
            ICheckpointTelemetry checkpointTelemetry = null,
            IDialogueTransportV2 dialogueTransport = null,
            ITimelinePlaybackController timelinePlaybackController = null)
        {
            if (questBindingResolver == null) throw new ArgumentNullException(nameof(questBindingResolver));
            if (clock == null) throw new ArgumentNullException(nameof(clock));

            var executors = new Dictionary<NodeType, INodeExecutor>
            {
                { NodeType.Wait, new WaitNodeExecutor(clock) },
                { NodeType.Checkpoint, new CheckpointNodeExecutor(checkpointTelemetry) },
                { NodeType.Quest, new QuestNodeExecutor(questBindingResolver, clock) },
                { NodeType.Dialogue, new DialogueNodeExecutor(dialogueTransport, clock) },
            };

            if (timelinePlaybackController != null)
                executors.Add(NodeType.Timeline, new TimelineNodeExecutor(timelinePlaybackController, clock));
            _executors = executors;
        }

        public bool TryGet(NodeType type, out INodeExecutor executor) =>
            _executors.TryGetValue(type, out executor);
    }
}
