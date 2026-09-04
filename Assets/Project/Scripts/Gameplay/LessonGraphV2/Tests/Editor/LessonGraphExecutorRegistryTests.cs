using NUnit.Framework;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Executors;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonGraphExecutorRegistryTests
    {
        [Test]
        public void TryGet_RegistersAllImplementedNodeTypes()
        {
            var registry = new LessonGraphExecutorRegistry(new EmptyQuestResolver(), new ManualClock());

            Assert.IsTrue(registry.TryGet(NodeType.Wait, out var wait));
            Assert.IsInstanceOf<WaitNodeExecutor>(wait);
            Assert.IsTrue(registry.TryGet(NodeType.Checkpoint, out var checkpoint));
            Assert.IsInstanceOf<CheckpointNodeExecutor>(checkpoint);
            Assert.IsTrue(registry.TryGet(NodeType.Quest, out var quest));
            Assert.IsInstanceOf<QuestNodeExecutor>(quest);
        }

        [Test]
        public void Constructor_RequiresResolverAndClock()
        {
            Assert.Throws<System.ArgumentNullException>(() => new LessonGraphExecutorRegistry(null, new ManualClock()));
            Assert.Throws<System.ArgumentNullException>(() => new LessonGraphExecutorRegistry(new EmptyQuestResolver(), null));
        }

        private sealed class EmptyQuestResolver : IQuestBindingResolver
        {
            public QuestBindingResolution Resolve(string bindingId) => QuestBindingResolution.Failure(null);
        }

        private sealed class ManualClock : INodeClock
        {
            public double ElapsedSeconds => 0d;
            public System.Threading.Tasks.Task Delay(float seconds, System.Threading.CancellationToken cancellationToken) =>
                System.Threading.Tasks.Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        }
    }
}
