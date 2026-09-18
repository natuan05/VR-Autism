using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Dialogue;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Executors;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class DialogueNodeExecutorTests
    {
        [UnityTest]
        public IEnumerator NonBlocking_SucceedsImmediately()
        {
            var transport = new MockDialogueTransport();
            var executor = new DialogueNodeExecutor(transport);
            var config = new DialogueNodeConfig("seq-1", "Hello there!", false, -1f, "teacher-npc");
            var context = CreateContext(config, "act-1");

            var task = executor.ExecuteAsync(context);
            yield return CompleteWithinFrames(task);

            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(NodeStatus.Success, result.Status);
            Assert.AreEqual("non_blocking", result.CompletionChannel);
            Assert.AreEqual(1, transport.SpeakCallCount);
            Assert.AreEqual("Hello there!", transport.LastRequest.text);
        }

        [UnityTest]
        public IEnumerator Blocking_AwaitsDoneSignal()
        {
            var transport = new MockDialogueTransport();
            var executor = new DialogueNodeExecutor(transport);
            var config = new DialogueNodeConfig("seq-1", "Hello there!", true, -1f, "teacher-npc");
            var context = CreateContext(config, "act-1");

            var task = executor.ExecuteAsync(context);
            Assert.IsFalse(task.IsCompleted);

            transport.EmitSignal(new DialogueSignalV2("act-1", "seq-1", "teacher-npc", DialogueSignalType.Done));
            yield return CompleteWithinFrames(task);

            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(NodeStatus.Success, result.Status);
        }

        [UnityTest]
        public IEnumerator Blocking_FailedSignal_ReturnsFailed()
        {
            var transport = new MockDialogueTransport();
            var executor = new DialogueNodeExecutor(transport);
            var config = new DialogueNodeConfig("seq-1", "Hello there!", true, -1f, "teacher-npc");
            var context = CreateContext(config, "act-1");

            var task = executor.ExecuteAsync(context);
            transport.EmitSignal(new DialogueSignalV2("act-1", "seq-1", "teacher-npc", DialogueSignalType.Failed, "tts_error"));
            yield return CompleteWithinFrames(task);

            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(NodeStatus.Failed, result.Status);
            Assert.AreEqual("tts_error", result.CompletionChannel);
        }

        [UnityTest]
        public IEnumerator Blocking_CancelledSignal_ReturnsSkipped()
        {
            var transport = new MockDialogueTransport();
            var executor = new DialogueNodeExecutor(transport);
            var config = new DialogueNodeConfig("seq-1", "Hello there!", true, -1f, "teacher-npc");
            var context = CreateContext(config, "act-1");

            var task = executor.ExecuteAsync(context);
            transport.EmitSignal(new DialogueSignalV2("act-1", "seq-1", "teacher-npc", DialogueSignalType.Cancelled, "user_interrupted"));
            yield return CompleteWithinFrames(task);

            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(NodeStatus.Skipped, result.Status);
            Assert.AreEqual("user_interrupted", result.CompletionChannel);
        }

        [UnityTest]
        public IEnumerator Blocking_IgnoresMismatchedSignals()
        {
            var transport = new MockDialogueTransport();
            var executor = new DialogueNodeExecutor(transport);
            var config = new DialogueNodeConfig("seq-1", "Hello there!", true, -1f, "teacher-npc");
            var context = CreateContext(config, "act-1");

            var task = executor.ExecuteAsync(context);

            // Mismatched activation
            transport.EmitSignal(new DialogueSignalV2("other-act", "seq-1", "teacher-npc", DialogueSignalType.Done));
            Assert.IsFalse(task.IsCompleted);

            // Mismatched sequence
            transport.EmitSignal(new DialogueSignalV2("act-1", "other-seq", "teacher-npc", DialogueSignalType.Done));
            Assert.IsFalse(task.IsCompleted);

            // Matching signal
            transport.EmitSignal(new DialogueSignalV2("act-1", "seq-1", "teacher-npc", DialogueSignalType.Done));
            yield return CompleteWithinFrames(task);

            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(NodeStatus.Success, result.Status);
        }

        [UnityTest]
        public IEnumerator Blocking_TimeoutExpires_ReturnsTimeout()
        {
            var transport = new MockDialogueTransport();
            var clock = new ManualClock();
            var executor = new DialogueNodeExecutor(transport, clock);
            var config = new DialogueNodeConfig("seq-1", "Hello there!", true, 5f, "teacher-npc");
            var context = CreateContext(config, "act-1", clock);

            var task = executor.ExecuteAsync(context);
            Assert.IsFalse(task.IsCompleted);

            clock.TriggerDelay();
            yield return CompleteWithinFrames(task);

            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(NodeStatus.Timeout, result.Status);
            Assert.AreEqual(1, transport.CancelCallCount);
        }

        [UnityTest]
        public IEnumerator MissingTransport_ReturnsFailed()
        {
            var executor = new DialogueNodeExecutor(null);
            var config = new DialogueNodeConfig("seq-1", "Hello", true, -1f, "teacher-npc");
            var context = CreateContext(config, "act-1");

            var task = executor.ExecuteAsync(context);
            yield return CompleteWithinFrames(task);

            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(NodeStatus.Failed, result.Status);
        }

        [UnityTest]
        public IEnumerator Blocking_SkipToken_ReturnsSkippedAndCancelsTransport()
        {
            var transport = new MockDialogueTransport();
            var executor = new DialogueNodeExecutor(transport);
            var config = new DialogueNodeConfig("seq-1", "Hello there!", true, -1f, "teacher-npc");

            using (var skipCts = new CancellationTokenSource())
            {
                var context = CreateContext(config, "act-1", skipToken: skipCts.Token);
                var task = executor.ExecuteAsync(context);
                Assert.IsFalse(task.IsCompleted);

                skipCts.Cancel();
                yield return CompleteWithinFrames(task);

                var result = task.GetAwaiter().GetResult();
                Assert.AreEqual(NodeStatus.Skipped, result.Status);
                Assert.AreEqual(1, transport.CancelCallCount);
            }
        }

        [UnityTest]
        public IEnumerator ExecuteAsync_InvalidActivationId_ReturnsFailed()
        {
            var transport = new MockDialogueTransport();
            var executor = new DialogueNodeExecutor(transport);
            var config = new DialogueNodeConfig("seq-1", "Hello", true, -1f, "teacher-npc");
            var context = CreateContext(config, "");

            var task = executor.ExecuteAsync(context);
            yield return CompleteWithinFrames(task);

            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(NodeStatus.Failed, result.Status);
            Assert.AreEqual("invalid_activation", result.CompletionChannel);
        }

        [UnityTest]
        public IEnumerator ExecuteAsync_InvalidConfigParameters_ReturnsFailed()
        {
            var transport = new MockDialogueTransport();
            var executor = new DialogueNodeExecutor(transport);

            // Empty text
            var emptyTextConfig = new DialogueNodeConfig("seq-1", "", true, -1f, "teacher-npc");
            var context1 = CreateContext(emptyTextConfig, "act-1");
            var task1 = executor.ExecuteAsync(context1);
            yield return CompleteWithinFrames(task1);
            Assert.AreEqual(NodeStatus.Failed, task1.GetAwaiter().GetResult().Status);

            // Empty sequenceId
            var emptySeqConfig = new DialogueNodeConfig("", "Hello", true, -1f, "teacher-npc");
            var context2 = CreateContext(emptySeqConfig, "act-1");
            var task2 = executor.ExecuteAsync(context2);
            yield return CompleteWithinFrames(task2);
            Assert.AreEqual(NodeStatus.Failed, task2.GetAwaiter().GetResult().Status);

            // Empty npcBindingId
            var emptyNpcConfig = new DialogueNodeConfig("seq-1", "Hello", true, -1f, "");
            var context3 = CreateContext(emptyNpcConfig, "act-1");
            var task3 = executor.ExecuteAsync(context3);
            yield return CompleteWithinFrames(task3);
            Assert.AreEqual(NodeStatus.Failed, task3.GetAwaiter().GetResult().Status);
        }

        private static NodeExecutionContext CreateContext(
            DialogueNodeConfig config,
            string activationId,
            INodeClock clock = null,
            CancellationToken cancellationToken = default,
            CancellationToken skipToken = default,
            CancellationToken timeoutToken = default)
        {
            var nodeData = new LessonNodeData("dialogue-node-1", NodeType.Dialogue, config);
            return new NodeExecutionContext(
                "run-1",
                activationId,
                "graph-1",
                nodeData,
                0d,
                cancellationToken,
                skipToken,
                timeoutToken,
                null,
                clock);
        }

        private static IEnumerator CompleteWithinFrames(Task task)
        {
            for (var frame = 0; frame < 60 && !task.IsCompleted; frame++)
                yield return null;
            Assert.IsTrue(task.IsCompleted, "Dialogue executor task did not complete within 60 frames.");
        }

        private sealed class MockDialogueTransport : IDialogueTransportV2
        {
            public event Action<DialogueSignalV2> SignalReceived;
            public int SpeakCallCount { get; private set; }
            public int CancelCallCount { get; private set; }
            public DialogueRequestV2 LastRequest { get; private set; }

            public Task SpeakAsync(DialogueRequestV2 request, CancellationToken cancellationToken)
            {
                SpeakCallCount++;
                LastRequest = request;
                return Task.CompletedTask;
            }

            public Task CancelAsync(string activationId, string sequenceId, string reason, CancellationToken cancellationToken)
            {
                CancelCallCount++;
                return Task.CompletedTask;
            }

            public void EmitSignal(DialogueSignalV2 signal)
            {
                SignalReceived?.Invoke(signal);
            }
        }

        private sealed class ManualClock : INodeClock
        {
            private readonly TaskCompletionSource<bool> _delayTcs = new TaskCompletionSource<bool>();
            public double ElapsedSeconds => 1d;
            public Task Delay(float seconds, CancellationToken cancellationToken) => _delayTcs.Task;
            public void TriggerDelay() => _delayTcs.TrySetResult(true);
        }
    }
}
