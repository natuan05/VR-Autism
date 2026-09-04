using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Executors;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class QuestNodeExecutorTests
    {
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        [TearDown] public void TearDown() { foreach (var item in _objects) if (item != null) UnityEngine.Object.DestroyImmediate(item); _objects.Clear(); }

        [UnityTest]
        public IEnumerator SynchronousFirstTerminal_UsesSharedActivationAndStopsUntouchedSource()
        {
            var winner = Source("touch", completeOnActivation: true);
            var untouched = Source("hold");
            var task = new QuestNodeExecutor(new Resolver(winner, untouched), new Clock()).ExecuteAsync(Context("a1", "touch", "hold"));
            yield return CompleteWithinFrames(task);
            var result = task.GetAwaiter().GetResult();

            Assert.AreEqual(NodeStatus.Success, result.Status);
            Assert.AreEqual("instant", result.CompletionChannel);
            Assert.AreEqual("a1", winner.CurrentActivationId);
            Assert.AreEqual(QuestSourceState.Inactive, untouched.State);
        }

        [UnityTest]
        public IEnumerator FirstCorrelatedTerminal_Completed_MapsStatusAndCancelsLoser()
        {
            yield return FirstCorrelatedTerminal(QuestSourceTerminalStatus.Completed, NodeStatus.Success);
        }

        [UnityTest]
        public IEnumerator FirstCorrelatedTerminal_Failed_MapsStatusAndCancelsLoser()
        {
            yield return FirstCorrelatedTerminal(QuestSourceTerminalStatus.Failed, NodeStatus.Failed);
        }

        [UnityTest]
        public IEnumerator FirstCorrelatedTerminal_Cancelled_MapsStatusAndCancelsLoser()
        {
            yield return FirstCorrelatedTerminal(QuestSourceTerminalStatus.Cancelled, NodeStatus.Skipped);
        }

        private IEnumerator FirstCorrelatedTerminal(QuestSourceTerminalStatus terminal, NodeStatus expected)
        {
            var first = Source("first"); var loser = Source("loser");
            var task = new QuestNodeExecutor(new Resolver(first, loser), new Clock()).ExecuteAsync(Context("a2", "first", "loser"));
            first.Emit("a2", terminal);
            yield return CompleteWithinFrames(task);
            var result = task.GetAwaiter().GetResult();

            Assert.AreEqual(expected, result.Status);
            Assert.AreEqual(QuestSourceState.Cancelled, loser.State);
        }

        [UnityTest]
        public IEnumerator StaleTerminal_IsIgnoredUntilMatchingTerminalArrives()
        {
            var source = Source("touch");
            var task = new QuestNodeExecutor(new Resolver(source), new Clock()).ExecuteAsync(Context("current", "touch"));
            source.Emit("stale", QuestSourceTerminalStatus.Completed);
            Assert.IsFalse(task.IsCompleted);
            source.Emit("current", QuestSourceTerminalStatus.Completed);
            yield return CompleteWithinFrames(task);
            Assert.AreEqual(NodeStatus.Success, task.GetAwaiter().GetResult().Status);
        }

        [UnityTest]
        public IEnumerator ResolutionOrActivationFailure_ReturnsFailedWithoutActivatingLaterSources()
        {
            var available = Source("available");
            var missing = new QuestNodeExecutor(new FailingResolver(), new Clock());
            var missingTask = missing.ExecuteAsync(Context("a3", "missing"));
            yield return CompleteWithinFrames(missingTask);
            Assert.AreEqual(NodeStatus.Failed, missingTask.GetAwaiter().GetResult().Status);

            available.enabled = false;
            var rejected = new QuestNodeExecutor(new Resolver(available), new Clock());
            var rejectedTask = rejected.ExecuteAsync(Context("a4", "available"));
            yield return CompleteWithinFrames(rejectedTask);
            Assert.AreEqual(NodeStatus.Failed, rejectedTask.GetAwaiter().GetResult().Status);
        }

        [UnityTest]
        public IEnumerator SkipAndTimeoutTokens_WinAndCancelActiveSources()
        {
            var skipSource = Source("skip"); var skip = new CancellationTokenSource();
            var skipTask = new QuestNodeExecutor(new Resolver(skipSource), new Clock()).ExecuteAsync(Context("skip", "skip", skipToken: skip.Token));
            skip.Cancel();
            yield return CompleteWithinFrames(skipTask);
            Assert.AreEqual(NodeStatus.Skipped, skipTask.GetAwaiter().GetResult().Status); Assert.AreEqual(QuestSourceState.Cancelled, skipSource.State);

            var timeoutSource = Source("timeout"); var timeout = new CancellationTokenSource();
            var timeoutTask = new QuestNodeExecutor(new Resolver(timeoutSource), new Clock()).ExecuteAsync(Context("timeout", "timeout", timeoutToken: timeout.Token));
            timeout.Cancel();
            yield return CompleteWithinFrames(timeoutTask);
            Assert.AreEqual(NodeStatus.Timeout, timeoutTask.GetAwaiter().GetResult().Status); Assert.AreEqual(QuestSourceState.Cancelled, timeoutSource.State);
        }

        [UnityTest]
        public IEnumerator ConfiguredClockTimeoutAndLessonAbort_CancelActiveSources()
        {
            var clock = new Clock(); var timed = Source("timed");
            var timeoutTask = new QuestNodeExecutor(new Resolver(timed), clock).ExecuteAsync(Context("timed", new[] { "timed" }, default, default, default, clock, 1f));
            clock.CompleteDelay();
            yield return CompleteWithinFrames(timeoutTask);
            Assert.AreEqual(NodeStatus.Timeout, timeoutTask.GetAwaiter().GetResult().Status); Assert.AreEqual(QuestSourceState.Cancelled, timed.State);

            var aborted = Source("aborted"); var abort = new CancellationTokenSource();
            var abortTask = new QuestNodeExecutor(new Resolver(aborted), new Clock()).ExecuteAsync(Context("aborted", new[] { "aborted" }, abort.Token, default, default, new Clock(), -1f));
            abort.Cancel();
            yield return CompleteWithinFrames(abortTask);
            Assert.IsTrue(abortTask.IsCanceled);
            Assert.AreEqual(QuestSourceState.Cancelled, aborted.State);
        }

        [UnityTest]
        public IEnumerator ResolverExceptionAndInvalidBindingList_ReturnTypedFailed()
        {
            var throwing = new QuestNodeExecutor(new ThrowingResolver(), new Clock());
            var throwingTask = throwing.ExecuteAsync(Context("error", "binding"));
            yield return CompleteWithinFrames(throwingTask);
            Assert.AreEqual(NodeStatus.Failed, throwingTask.GetAwaiter().GetResult().Status);
            var duplicateTask = new QuestNodeExecutor(new FailingResolver(), new Clock()).ExecuteAsync(Context("duplicate", new[] { "same", "same" }, default, default, default, new Clock(), -1f));
            yield return CompleteWithinFrames(duplicateTask);
            Assert.AreEqual(NodeStatus.Failed, duplicateTask.GetAwaiter().GetResult().Status);
        }

        private TestSource Source(string binding, bool completeOnActivation = false)
        {
            var go = new GameObject(binding); _objects.Add(go); var source = go.AddComponent<TestSource>(); source.Assign(binding); typeof(QuestSourceV2).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(source, null); source.CompleteOnActivation = completeOnActivation; return source;
        }
        private static NodeExecutionContext Context(string activation, params string[] bindings) => Context(activation, bindings, default, default, default, new Clock(), -1f);
        private static NodeExecutionContext Context(string activation, string binding, CancellationToken skipToken = default, CancellationToken timeoutToken = default) => Context(activation, new[] { binding }, default, skipToken, timeoutToken, new Clock(), -1f);
        private static NodeExecutionContext Context(string activation, string[] bindings, CancellationToken cancellationToken, CancellationToken skipToken, CancellationToken timeoutToken, INodeClock clock, float configTimeout) => new NodeExecutionContext("run", activation, "graph", new LessonNodeData("node", NodeType.Quest, new QuestNodeConfig(new List<string>(bindings), configTimeout)), 0d, cancellationToken, skipToken, timeoutToken, null, clock);
        private static IEnumerator CompleteWithinFrames(Task task)
        {
            for (var frame = 0; frame < 60 && !task.IsCompleted; frame++) yield return null;
            Assert.IsTrue(task.IsCompleted, "Quest task did not complete within 60 editor frames.");
        }

        private sealed class Resolver : IQuestBindingResolver
        {
            private readonly Dictionary<string, QuestSourceV2> _sources = new Dictionary<string, QuestSourceV2>();
            public Resolver(params QuestSourceV2[] sources) { foreach (var source in sources) _sources[source.BindingId] = source; }
            public QuestBindingResolution Resolve(string id) => _sources.TryGetValue(id, out var source) ? QuestBindingResolution.Success(source) : QuestBindingResolution.Failure(new QuestBindingValidationIssue(QuestBindingFailureCodes.MissingBinding, id, "missing"));
        }
        private sealed class FailingResolver : IQuestBindingResolver { public QuestBindingResolution Resolve(string id) => QuestBindingResolution.Failure(new QuestBindingValidationIssue(QuestBindingFailureCodes.BindingUnavailable, id, "failed")); }
        private sealed class ThrowingResolver : IQuestBindingResolver { public QuestBindingResolution Resolve(string id) => throw new InvalidOperationException(); }
        private sealed class Clock : INodeClock { private readonly TaskCompletionSource<bool> _delay = new TaskCompletionSource<bool>(); public double ElapsedSeconds => 1d; public Task Delay(float seconds, CancellationToken token) => _delay.Task; public void CompleteDelay() => _delay.TrySetResult(true); }
        private sealed class TestSource : QuestSourceV2
        {
            public bool CompleteOnActivation { get; set; }
            public void Assign(string binding) => typeof(QuestSourceV2).GetField("_bindingId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(this, binding);
            public void Emit(string activation, QuestSourceTerminalStatus status)
            {
                if (status == QuestSourceTerminalStatus.Completed) TryComplete(activation, "manual");
                else if (status == QuestSourceTerminalStatus.Failed) TryFail(activation, "manual_failure");
                else TryCancel(new QuestSourceCancellation(activation, "manual_cancel"));
            }
            protected override void OnSourceActivated(QuestSourceActivation activation) { if (CompleteOnActivation) TryComplete(activation.ActivationId, "instant"); }
        }
    }
}
