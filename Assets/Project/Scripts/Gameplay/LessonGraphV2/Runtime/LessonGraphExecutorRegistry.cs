using System;
using System.Collections.Generic;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Questing;
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
            ICheckpointTelemetry checkpointTelemetry = null)
        {
            if (questBindingResolver == null) throw new ArgumentNullException(nameof(questBindingResolver));
            if (clock == null) throw new ArgumentNullException(nameof(clock));

            _executors = new Dictionary<NodeType, INodeExecutor>
            {
                { NodeType.Wait, new WaitNodeExecutor(clock) },
                { NodeType.Checkpoint, new CheckpointNodeExecutor(checkpointTelemetry) },
                { NodeType.Quest, new QuestNodeExecutor(questBindingResolver, clock) },
            };
        }

        public bool TryGet(NodeType type, out INodeExecutor executor) =>
            _executors.TryGetValue(type, out executor);
    }
}
