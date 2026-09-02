using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Executors;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonGraphRunnerTests
    {
        [Test]
        public async Task StartLessonAsync_UsesLowestPriorityStatusEdge_AndCompletesOnce()
        {
            var graph = Graph("first", new List<LessonNodeData>
            {
                new LessonNodeData("first", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("second", NodeType.Checkpoint, new CheckpointNodeConfig("second")),
            }, new List<LessonEdgeData>
            {
                new LessonEdgeData("first", "second", new StatusCondition(StatusCondition.Success), 10),
                new LessonEdgeData("first", "second", new StatusCondition(StatusCondition.Success), 1),
            });
            var first = new ImmediateExecutor(NodeStatus.Success);
            var second = new ImmediateExecutor(NodeStatus.Success);
            var runner = NewRunner(graph, new MapRegistry(first, second));
            var entered = new List<string>();
            var completed = 0;
            runner.NodeEntered += e => entered.Add(e.NodeId);
            runner.LessonCompleted += _ => completed++;

            var result = await runner.StartLessonAsync();

            Assert.IsTrue(result.IsSuccess);
            CollectionAssert.AreEqual(new[] { "first", "second" }, entered);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, first.Executions);
            Assert.AreEqual(1, second.Executions);
            Object.DestroyImmediate(runner.gameObject);
            Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task StartLessonAsync_InvalidGraph_ProducesInvalidGraphWithoutActivation()
        {
            var graph = Graph("missing", new List<LessonNodeData>());
            var executor = new ImmediateExecutor(NodeStatus.Success);
            var runner = NewRunner(graph, new MapRegistry(executor));
            var entered = 0;
            runner.NodeEntered += _ => entered++;

            var result = await runner.StartLessonAsync();

            Assert.AreEqual(LessonFailureReason.InvalidGraph, result.FailureReason);
            Assert.AreEqual(0, entered);
            Assert.AreEqual(0, executor.Executions);
            Object.DestroyImmediate(runner.gameObject);
            Object.DestroyImmediate(graph);
        }

        [UnityTest]
        public IEnumerator WaitExecutor_SkipWinsBeforeDelay()
        {
            var clock = new ManualClock();
            var executor = new WaitNodeExecutor(clock);
            using var cancellation = new CancellationTokenSource();
            using var skip = new CancellationTokenSource();
            using var timeout = new CancellationTokenSource();
            var context = new NodeExecutionContext("run", "activation", "graph", new LessonNodeData("wait", NodeType.Wait, new WaitNodeConfig(3)), 0, cancellation.Token, skip.Token, timeout.Token, null);
            skip.Cancel();
            var task = executor.ExecuteAsync(context);

            yield return CompleteWithinFrames(task);
            var result = task.GetAwaiter().GetResult();

            Assert.AreEqual(NodeStatus.Skipped, result.Status);
        }

        [Test]
        public async Task CheckpointExecutor_EmitsOncePerActivation()
        {
            var telemetry = new RecordingTelemetry();
            var executor = new CheckpointNodeExecutor(telemetry);
            var node = new LessonNodeData("checkpoint-node", NodeType.Checkpoint, new CheckpointNodeConfig("checkpoint-1"));
            var context = new NodeExecutionContext("run", "activation", "graph", node, 3, CancellationToken.None, CancellationToken.None, CancellationToken.None, null);

            await executor.ExecuteAsync(context);
            await executor.ExecuteAsync(context);

            Assert.AreEqual(1, telemetry.Markers.Count);
            Assert.AreEqual("checkpoint-1", telemetry.Markers[0].CheckpointId);
        }

        [Test]
        public async Task StartLessonAsync_UsesAlwaysOnlyWhenNoStatusEdgeMatches()
        {
            var graph = Graph("source", new List<LessonNodeData>
            {
                new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("status", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("fallback", NodeType.Wait, new WaitNodeConfig(1)),
            }, new List<LessonEdgeData>
            {
                new LessonEdgeData("source", "status", new StatusCondition(StatusCondition.Success), 0),
                new LessonEdgeData("source", "fallback", new AlwaysCondition(), 100),
            });
            var executor = new NodeIdExecutor(new Dictionary<string, NodeStatus> { { "source", NodeStatus.Failed }, { "fallback", NodeStatus.Success } });
            var runner = NewRunner(graph, new SingleRegistry(executor));
            var entered = new List<string>();
            runner.NodeEntered += entry => entered.Add(entry.NodeId);

            var result = await runner.StartLessonAsync();

            Assert.IsTrue(result.IsSuccess);
            CollectionAssert.AreEqual(new[] { "source", "fallback" }, entered);
            Object.DestroyImmediate(runner.gameObject); Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task StartLessonAsync_UnmatchedOutgoingEdge_IsTerminalAndCompletesOnce()
        {
            var graph = Graph("source", new List<LessonNodeData>
            {
                new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("unreachable", NodeType.Wait, new WaitNodeConfig(1)),
            }, new List<LessonEdgeData> { new LessonEdgeData("source", "unreachable", new StatusCondition(StatusCondition.Success)) });
            var runner = NewRunner(graph, new SingleRegistry(new NodeIdExecutor(new Dictionary<string, NodeStatus> { { "source", NodeStatus.Failed } })));
            var entered = 0; var completed = 0;
            runner.NodeEntered += _ => entered++;
            runner.LessonCompleted += _ => completed++;

            var result = await runner.StartLessonAsync();

            Assert.IsTrue(result.IsSuccess); Assert.AreEqual(NodeStatus.Failed, result.FinalNodeResult.Status);
            Assert.AreEqual(1, entered); Assert.AreEqual(1, completed);
            Object.DestroyImmediate(runner.gameObject); Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task StartLessonAsync_EqualPriorityPreservesSerializedEdgeOrder()
        {
            var graph = Graph("source", new List<LessonNodeData>
            {
                new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("first", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("second", NodeType.Wait, new WaitNodeConfig(1)),
            }, new List<LessonEdgeData>
            {
                new LessonEdgeData("source", "first", new StatusCondition(StatusCondition.Success), 5),
                new LessonEdgeData("source", "second", new StatusCondition(StatusCondition.Success), 5),
            });
            var executor = new NodeIdExecutor(new Dictionary<string, NodeStatus> { { "source", NodeStatus.Success }, { "first", NodeStatus.Success }, { "second", NodeStatus.Success } });
            var runner = NewRunner(graph, new SingleRegistry(executor));
            var entered = new List<string>(); runner.NodeEntered += entry => entered.Add(entry.NodeId);

            await runner.StartLessonAsync();

            CollectionAssert.AreEqual(new[] { "source", "first" }, entered);
            Object.DestroyImmediate(runner.gameObject); Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task StartLessonAsync_FailedPreflightDoesNotStartClockOrActivate()
        {
            var graph = Graph("source", new List<LessonNodeData> { new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)) });
            var clock = new TrackingClock(); var executor = new ImmediateExecutor(NodeStatus.Success);
            var go = new GameObject("preflight"); var runner = go.AddComponent<LessonGraphRunner>();
            runner.Configure(graph, new MapRegistry(executor), new FailingPreflight(), clock);

            var result = await runner.StartLessonAsync();

            Assert.AreEqual(LessonFailureReason.InvalidGraph, result.FailureReason);
            Assert.AreEqual(0, executor.Executions); Assert.AreEqual(0, clock.DelayCalls);
            Object.DestroyImmediate(go); Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task StartLessonAsync_MissingExecutorDoesNotEnterNode()
        {
            var graph = Graph("source", new List<LessonNodeData> { new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)) });
            var go = new GameObject("missing executor"); var runner = go.AddComponent<LessonGraphRunner>();
            runner.Configure(graph, new MissingRegistry(), new PassPreflight(), new ManualClock());
            var entered = 0; runner.NodeEntered += _ => entered++;

            var result = await runner.StartLessonAsync();

            Assert.AreEqual(LessonFailureReason.InvalidGraph, result.FailureReason); Assert.AreEqual(0, entered);
            Object.DestroyImmediate(go); Object.DestroyImmediate(graph);
        }

        [UnityTest]
        public IEnumerator WaitExecutor_DurationCompletesWithSuccessWithoutSleeping()
        {
            var clock = new TrackingClock(); var executor = new WaitNodeExecutor(clock);
            using var cancellation = new CancellationTokenSource();
            var task = executor.ExecuteAsync(WaitContext(cancellation.Token, CancellationToken.None, CancellationToken.None));
            clock.CompleteDelay();

            yield return CompleteWithinFrames(task);
            var result = task.GetAwaiter().GetResult();

            Assert.AreEqual(NodeStatus.Success, result.Status); Assert.AreEqual("duration", result.CompletionChannel);
        }

        [UnityTest]
        public IEnumerator WaitExecutor_TimeoutWinsBeforeDelay()
        {
            var clock = new TrackingClock(); var executor = new WaitNodeExecutor(clock);
            using var cancellation = new CancellationTokenSource(); using var timeout = new CancellationTokenSource();
            var task = executor.ExecuteAsync(WaitContext(cancellation.Token, CancellationToken.None, timeout.Token));
            timeout.Cancel();

            yield return CompleteWithinFrames(task);
            var result = task.GetAwaiter().GetResult();

            Assert.AreEqual(NodeStatus.Timeout, result.Status);
        }

        [UnityTest]
        public IEnumerator WaitExecutor_AbortCancelsWithoutProducingResult()
        {
            var clock = new TrackingClock(); var executor = new WaitNodeExecutor(clock);
            using var cancellation = new CancellationTokenSource();
            var task = executor.ExecuteAsync(WaitContext(cancellation.Token, CancellationToken.None, CancellationToken.None));
            cancellation.Cancel();

            yield return CompleteWithinFrames(task);
            Assert.IsTrue(task.IsCanceled);
        }

        [Test]
        public async Task Runner_AbortRejectsLateExecutorCompletion()
        {
            var graph = Graph("source", new List<LessonNodeData> { new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)) });
            var executor = new ControlledExecutor(); var runner = NewRunner(graph, new SingleRegistry(executor));
            var nodeCompleted = 0; var lessonCompleted = 0;
            runner.NodeCompleted += _ => nodeCompleted++; runner.LessonCompleted += _ => lessonCompleted++;
            var task = runner.StartLessonAsync();
            runner.AbortLesson(); executor.Complete(NodeStatus.Success);

            var result = await task;

            Assert.AreEqual(LessonFailureReason.Aborted, result.FailureReason);
            Assert.AreEqual(0, nodeCompleted); Assert.AreEqual(0, lessonCompleted);
            Object.DestroyImmediate(runner.gameObject); Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task CheckpointExecutor_TelemetryFailureReturnsFailedResult()
        {
            var executor = new CheckpointNodeExecutor(new ThrowingTelemetry());
            var node = new LessonNodeData("checkpoint", NodeType.Checkpoint, new CheckpointNodeConfig("marker"));
            var context = new NodeExecutionContext("run", "activation", "graph", node, 0, CancellationToken.None, CancellationToken.None, CancellationToken.None, null);

            var result = await executor.ExecuteAsync(context);

            Assert.AreEqual(NodeStatus.Failed, result.Status); Assert.AreEqual("telemetry_exception", result.CompletionChannel);
        }

        [Test]
        public async Task CheckpointExecutor_AllowsOneMarkerForEachActivation()
        {
            var telemetry = new RecordingTelemetry(); var executor = new CheckpointNodeExecutor(telemetry);
            var node = new LessonNodeData("checkpoint", NodeType.Checkpoint, new CheckpointNodeConfig("marker"));
            await executor.ExecuteAsync(new NodeExecutionContext("run", "one", "graph", node, 0, CancellationToken.None, CancellationToken.None, CancellationToken.None, null));
            await executor.ExecuteAsync(new NodeExecutionContext("run", "two", "graph", node, 0, CancellationToken.None, CancellationToken.None, CancellationToken.None, null));

            Assert.AreEqual(2, telemetry.Markers.Count);
            Assert.AreNotEqual(telemetry.Markers[0].ActivationId, telemetry.Markers[1].ActivationId);
        }
        [TestCase(NodeStatus.Success)]
        [TestCase(NodeStatus.Skipped)]
        [TestCase(NodeStatus.Timeout)]
        [TestCase(NodeStatus.Failed)]
        public async Task StartLessonAsync_TransitionsForEveryCanonicalStatus(NodeStatus status)
        {
            var graph = Graph("source", new List<LessonNodeData>
            {
                new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("target", NodeType.Wait, new WaitNodeConfig(1)),
            }, new List<LessonEdgeData> { new LessonEdgeData("source", "target", new StatusCondition(NodeStatusCondition.ToCondition(status))) });
            var executor = new NodeIdExecutor(new Dictionary<string, NodeStatus> { { "source", status }, { "target", NodeStatus.Success } });
            var runner = NewRunner(graph, new SingleRegistry(executor));
            var entered = new List<string>(); runner.NodeEntered += entry => entered.Add(entry.NodeId);

            var result = await runner.StartLessonAsync();

            Assert.IsTrue(result.IsSuccess);
            CollectionAssert.AreEqual(new[] { "source", "target" }, entered);
            Object.DestroyImmediate(runner.gameObject); Object.DestroyImmediate(graph);
        }
        [Test]
        public async Task StartLessonAsync_DuplicateStartSharesTheActiveTask()
        {
            var graph = Graph("source", new List<LessonNodeData> { new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)) });
            var executor = new ControlledExecutor(); var runner = NewRunner(graph, new SingleRegistry(executor));
            var first = runner.StartLessonAsync(); var second = runner.StartLessonAsync();

            Assert.AreSame(first, second);
            runner.AbortLesson(); executor.Complete(NodeStatus.Success);
            Assert.AreEqual(LessonFailureReason.Aborted, (await first).FailureReason);
            Object.DestroyImmediate(runner.gameObject); Object.DestroyImmediate(graph);
        }

        [UnityTest]
        public IEnumerator Runner_OnDisableCancelsNonCooperativeExecutorWithoutStaleCompletion()
        {
            var graph = Graph("source", new List<LessonNodeData> { new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)) });
            var executor = new ControlledExecutor(); var runner = NewRunner(graph, new SingleRegistry(executor));
            var completed = 0; runner.LessonCompleted += _ => completed++;
            var task = runner.StartLessonAsync();
            typeof(LessonGraphRunner)
                .GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(runner, null);
            runner.gameObject.SetActive(false);

            yield return CompleteWithinFrames(task);
            var result = task.GetAwaiter().GetResult();

            Assert.AreEqual(LessonFailureReason.Aborted, result.FailureReason);
            Assert.AreEqual(0, completed);
            executor.Complete(NodeStatus.Success);
            Object.DestroyImmediate(runner.gameObject); Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task StartLessonAsync_NullExecutorResultBecomesFailedNodeResult()
        {
            var graph = Graph("source", new List<LessonNodeData> { new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)) });
            var runner = NewRunner(graph, new SingleRegistry(new NullResultExecutor()));

            var result = await runner.StartLessonAsync();

            Assert.AreEqual(NodeStatus.Failed, result.FinalNodeResult.Status);
            Assert.AreEqual("invalid_result", result.FinalNodeResult.CompletionChannel);
            Object.DestroyImmediate(runner.gameObject); Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task StartLessonAsync_WrongExecutorResultBecomesFailedNodeResult()
        {
            var graph = Graph("source", new List<LessonNodeData> { new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)) });
            var runner = NewRunner(graph, new SingleRegistry(new WrongResultExecutor()));

            var result = await runner.StartLessonAsync();

            Assert.AreEqual(NodeStatus.Failed, result.FinalNodeResult.Status);
            Assert.AreEqual("invalid_result", result.FinalNodeResult.CompletionChannel);
            Object.DestroyImmediate(runner.gameObject); Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task StartLessonAsync_ExecutorExceptionBecomesFailedNodeResult()
        {
            var graph = Graph("source", new List<LessonNodeData> { new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)) });
            var runner = NewRunner(graph, new SingleRegistry(new ThrowingExecutor()));

            var result = await runner.StartLessonAsync();

            Assert.AreEqual(NodeStatus.Failed, result.FinalNodeResult.Status);
            Assert.AreEqual("exception", result.FinalNodeResult.CompletionChannel);
            Object.DestroyImmediate(runner.gameObject); Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task Runner_IsolatesThrowingEventSubscribers()
        {
            var graph = Graph("source", new List<LessonNodeData> { new LessonNodeData("source", NodeType.Wait, new WaitNodeConfig(1)) });
            var runner = NewRunner(graph, new SingleRegistry(new ImmediateExecutor(NodeStatus.Success)));
            var entered = 0; var completed = 0;
            runner.NodeEntered += _ => throw new System.InvalidOperationException("subscriber");
            runner.NodeEntered += _ => entered++;
            runner.LessonCompleted += _ => completed++;

            LogAssert.Expect(LogType.Exception, new Regex("^InvalidOperationException: subscriber"));
            var result = await runner.StartLessonAsync();

            Assert.IsTrue(result.IsSuccess); Assert.AreEqual(1, entered); Assert.AreEqual(1, completed);
            Object.DestroyImmediate(runner.gameObject); Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task Runner_ResetsSkipAndTimeoutAcrossNodeActivations()
        {
            var graph = Graph("first", new List<LessonNodeData>
            {
                new LessonNodeData("first", NodeType.Wait, new WaitNodeConfig(1)),
                new LessonNodeData("second", NodeType.Wait, new WaitNodeConfig(1)),
            }, new List<LessonEdgeData> { new LessonEdgeData("first", "second", new StatusCondition(StatusCondition.Skipped)) });
            var clock = new QueueClock(); var go = new GameObject("token isolation"); var runner = go.AddComponent<LessonGraphRunner>();
            runner.Configure(graph, new SingleRegistry(new WaitNodeExecutor(clock)), new PassPreflight(), clock);
            var statuses = new List<NodeStatus>(); runner.NodeCompleted += result => statuses.Add(result.Result.Status);
            var task = runner.StartLessonAsync();
            runner.RequestSkip();
            await clock.SecondDelayStarted.Task;
            Assert.IsFalse(task.IsCompleted, "Previous node skip must not cancel the next activation.");
            runner.RequestTimeout();

            var result = await task;

            CollectionAssert.AreEqual(new[] { NodeStatus.Skipped, NodeStatus.Timeout }, statuses);
            Assert.AreEqual(NodeStatus.Timeout, result.FinalNodeResult.Status);
            Object.DestroyImmediate(go); Object.DestroyImmediate(graph);
        }

        [Test]
        public async Task Executors_RecordCompletionTimeElapsedValues()
        {
            var clock = new TrackingClock(); var wait = new WaitNodeExecutor(clock);
            var waitTask = wait.ExecuteAsync(new NodeExecutionContext("run", "wait", "graph", new LessonNodeData("wait", NodeType.Wait, new WaitNodeConfig(1)), 1, CancellationToken.None, CancellationToken.None, CancellationToken.None, null, clock));
            clock.Elapsed = 7; clock.CompleteDelay();
            var waitResult = await waitTask;
            var telemetry = new RecordingTelemetry(); var checkpoint = new CheckpointNodeExecutor(telemetry);
            clock.Elapsed = 11;
            await checkpoint.ExecuteAsync(new NodeExecutionContext("run", "checkpoint", "graph", new LessonNodeData("checkpoint", NodeType.Checkpoint, new CheckpointNodeConfig("marker")), 1, CancellationToken.None, CancellationToken.None, CancellationToken.None, null, clock));

            Assert.AreEqual(7d, waitResult.ElapsedSeconds);
            Assert.AreEqual(11d, telemetry.Markers[0].ElapsedSeconds);
        }
        private static LessonGraphRunner NewRunner(LessonGraph graph, INodeExecutorRegistry registry)
        {
            var go = new GameObject("LessonGraphRunnerTests");
            var runner = go.AddComponent<LessonGraphRunner>();
            runner.Configure(graph, registry, new PassPreflight(), new ManualClock());
            return runner;
        }

        private static LessonGraph Graph(string entry, List<LessonNodeData> nodes, List<LessonEdgeData> edges = null)
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            graph.Editor_SetEntryNodeId(entry);
            graph.Editor_SetNodes(nodes);
            graph.Editor_SetEdges(edges ?? new List<LessonEdgeData>());
            return graph;
        }

        private sealed class ImmediateExecutor : INodeExecutor
        {
            private readonly NodeStatus _status;
            public int Executions { get; private set; }
            public ImmediateExecutor(NodeStatus status) { _status = status; }
            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context) { Executions++; return Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId, _status, context.ElapsedSeconds)); }
        }
        private static IEnumerator CompleteWithinFrames(Task task)
        {
            for (var frame = 0; frame < 30 && !task.IsCompleted; frame++) yield return null;
            Assert.IsTrue(task.IsCompleted, "Wait task did not complete within 30 editor frames.");
        }

        private static NodeExecutionContext WaitContext(CancellationToken cancellation, CancellationToken skip, CancellationToken timeout)
        {
            return new NodeExecutionContext("run", "activation", "graph", new LessonNodeData("wait", NodeType.Wait, new WaitNodeConfig(1)), 0, cancellation, skip, timeout, null);
        }

        private sealed class NodeIdExecutor : INodeExecutor
        {
            private readonly IDictionary<string, NodeStatus> _statuses;
            public NodeIdExecutor(IDictionary<string, NodeStatus> statuses) { _statuses = statuses; }
            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
            {
                var status = _statuses.TryGetValue(context.Node.Id, out var configured) ? configured : NodeStatus.Success;
                return Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId, status, context.ElapsedSeconds));
            }
        }

        private sealed class SingleRegistry : INodeExecutorRegistry
        {
            private readonly INodeExecutor _executor;
            public SingleRegistry(INodeExecutor executor) { _executor = executor; }
            public bool TryGet(NodeType type, out INodeExecutor executor) { executor = _executor; return executor != null; }
        }

        private sealed class MissingRegistry : INodeExecutorRegistry
        {
            public bool TryGet(NodeType type, out INodeExecutor executor) { executor = null; return false; }
        }

        private sealed class FailingPreflight : ILessonStartPreflight
        {
            public bool IsReady(LessonGraph graph, out string reason) { reason = "binding missing"; return false; }
        }

        private sealed class TrackingClock : INodeClock
        {
            private readonly TaskCompletionSource<bool> _delay = new TaskCompletionSource<bool>();
            public int DelayCalls { get; private set; }
            public double Elapsed { get; set; }
            public double ElapsedSeconds => Elapsed;
            public Task Delay(float seconds, CancellationToken cancellationToken)
            {
                DelayCalls++;
                if (cancellationToken.CanBeCanceled) cancellationToken.Register(() => _delay.TrySetCanceled());
                return _delay.Task;
            }
            public void CompleteDelay() => _delay.TrySetResult(true);
        }

        private sealed class ControlledExecutor : INodeExecutor
        {
            private readonly TaskCompletionSource<NodeResult> _completion = new TaskCompletionSource<NodeResult>();
            private NodeExecutionContext _context;
            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context) { _context = context; return _completion.Task; }
            public void Complete(NodeStatus status) => _completion.TrySetResult(NodeResult.Completed(_context.Node.Id, _context.ActivationId, status, _context.ElapsedSeconds));
        }

        private sealed class ThrowingTelemetry : ICheckpointTelemetry
        {
            public void Record(CheckpointMarker marker) { throw new System.InvalidOperationException("telemetry failed"); }
        }
        private sealed class NullResultExecutor : INodeExecutor
        {
            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context) => Task.FromResult<NodeResult>(null);
        }

        private sealed class WrongResultExecutor : INodeExecutor
        {
            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context) => Task.FromResult(NodeResult.Completed("wrong", "wrong", NodeStatus.Success, 0));
        }

        private sealed class ThrowingExecutor : INodeExecutor
        {
            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context) { throw new System.InvalidOperationException("executor"); }
        }

        private sealed class QueueClock : INodeClock
        {
            private int _delays;
            public readonly TaskCompletionSource<bool> SecondDelayStarted = new TaskCompletionSource<bool>();
            public double ElapsedSeconds => 0;
            public Task Delay(float seconds, CancellationToken cancellationToken)
            {
                _delays++;
                if (_delays == 2) SecondDelayStarted.TrySetResult(true);
                return new TaskCompletionSource<bool>().Task;
            }
        }
        private sealed class MapRegistry : INodeExecutorRegistry
        {
            private readonly INodeExecutor _first; private readonly INodeExecutor _second;
            public MapRegistry(INodeExecutor first, INodeExecutor second = null) { _first = first; _second = second ?? first; }
            public bool TryGet(NodeType type, out INodeExecutor executor) { executor = type == NodeType.Wait ? _first : _second; return executor != null; }
        }
        private sealed class PassPreflight : ILessonStartPreflight { public bool IsReady(LessonGraph graph, out string reason) { reason = null; return true; } }
        private sealed class ManualClock : INodeClock { public double ElapsedSeconds => 0; public Task Delay(float seconds, CancellationToken cancellationToken) => Task.Delay(System.Threading.Timeout.Infinite, cancellationToken); }
        private sealed class RecordingTelemetry : ICheckpointTelemetry { public readonly List<CheckpointMarker> Markers = new List<CheckpointMarker>(); public void Record(CheckpointMarker marker) => Markers.Add(marker); }
    }
}
