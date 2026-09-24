using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonGraphRemoteCommandTests
    {
        private LessonGraph _graph;
        private GameObject _runnerObject;
        private LessonGraphRunner _runner;
        private ControlledExecutor _executor;

        [TearDown]
        public void TearDown()
        {
            _executor?.CompleteAll(NodeStatus.Failed);
            _runner?.AbortLesson();
            if (_runnerObject != null) Object.DestroyImmediate(_runnerObject);
            if (_graph != null) Object.DestroyImmediate(_graph);
        }

        [UnityTest]
        public IEnumerator Pause_CancelsActivationAndWaitsForCleanupWithoutCompletingLesson()
        {
            _graph = Graph(new List<LessonNodeData>
            {
                new LessonNodeData("quest-1", NodeType.Wait, new WaitNodeConfig(30))
            });
            _executor = new ControlledExecutor();
            _runnerObject = new GameObject("LessonGraphRemoteCommandTests");
            _runner = _runnerObject.AddComponent<LessonGraphRunner>();
            _runner.Configure(_graph, new SingleRegistry(_executor));
            _runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));
            var completedNodes = 0;
            var lessonCompletions = 0;
            var cancelledActivations = new List<NodeCancelledEventV2>();
            _runner.NodeCompleted += _ => completedNodes++;
            _runner.LessonCompleted += _ => lessonCompletions++;
            _runner.NodeCancelled += e => cancelledActivations.Add(e);

            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _executor.Contexts.Count == 1);

            var activationId = _runner.CurrentState.activation_id;
            var runningRevision = _runner.CurrentState.state_revision;
            var pauseTask = _runner.ApplyCommandAsync(Command("pause-1", "session-1", _runner.CurrentState.run_id,
                "quest-1", activationId, LessonCommandKindV2.Pause));

            Assert.That(_executor.Contexts[0].CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(_runner.CurrentState.status, Is.EqualTo("pausing"));
            Assert.That(_runner.CurrentState.state_revision, Is.GreaterThan(runningRevision));
            var pausingRevision = _runner.CurrentState.state_revision;
            Assert.That(pauseTask.IsCompleted, Is.False, "Pause must wait until the executor has cleaned up.");
            Assert.That(lessonTask.IsCompleted, Is.False, "Pause must leave the logical lesson task pending.");
            Assert.That(completedNodes, Is.Zero, "A pause must not complete the node or choose an edge.");

            _executor.Complete(0, NodeStatus.Success);
            yield return CompleteWithinFrames(pauseTask);

            var result = pauseTask.GetAwaiter().GetResult();
            Assert.That(result.accepted, Is.True);
            Assert.That(result.reason, Is.EqualTo(LessonCommandReasonV2.None));
            Assert.That(result.state.status, Is.EqualTo("paused"));
            Assert.That(result.state.node_id, Is.EqualTo("quest-1"));
            Assert.That(result.state.activation_id, Is.EqualTo(activationId));
            Assert.That(result.state.state_revision, Is.GreaterThan(pausingRevision));
            Assert.That(result.state.state_revision, Is.EqualTo(_runner.CurrentState.state_revision));
            Assert.That(lessonTask.IsCompleted, Is.False);
            Assert.That(completedNodes, Is.Zero);
            Assert.That(cancelledActivations.Count, Is.EqualTo(1));
            Assert.That(cancelledActivations[0].Reason, Is.EqualTo(LessonCommandKindV2.Pause));

            var pausedRevision = _runner.CurrentState.state_revision;
            var skipWhilePausedTask = _runner.ApplyCommandAsync(Command("skip-paused", "session-1", result.state.run_id,
                result.state.node_id, result.state.activation_id, LessonCommandKindV2.Skip));
            yield return CompleteWithinFrames(skipWhilePausedTask);
            var skipWhilePaused = skipWhilePausedTask.GetAwaiter().GetResult();
            Assert.That(skipWhilePaused.reason, Is.EqualTo(LessonCommandReasonV2.InvalidState));
            Assert.That(skipWhilePaused.state.state_revision, Is.EqualTo(pausedRevision));

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
            Assert.That(_runner.CurrentState.status, Is.EqualTo("cancelled"));
            Assert.That(lessonTask.GetAwaiter().GetResult().FailureReason, Is.EqualTo(LessonFailureReason.Aborted));
            Assert.That(lessonCompletions, Is.EqualTo(1));
            Assert.That(cancelledActivations.Count, Is.EqualTo(1), "Abort while paused must not emit a second activation-cancel event.");
        }

        [UnityTest]
        public IEnumerator Resume_ReactivatesSameNodeWithFreshActivationAfterCleanup()
        {
            _graph = Graph(new List<LessonNodeData>
            {
                new LessonNodeData("quest-1", NodeType.Wait, new WaitNodeConfig(30))
            });
            _executor = new ControlledExecutor();
            _runnerObject = new GameObject("LessonGraphResumeTests");
            _runner = _runnerObject.AddComponent<LessonGraphRunner>();
            _runner.Configure(_graph, new SingleRegistry(_executor));
            _runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));
            var completedActivations = new List<string>();
            _runner.NodeCompleted += e => completedActivations.Add(e.Result.ActivationId);

            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _executor.Contexts.Count == 1);
            var oldActivation = _runner.CurrentState.activation_id;
            var pauseTask = _runner.ApplyCommandAsync(Command("pause-1", "session-1", _runner.CurrentState.run_id,
                "quest-1", oldActivation, LessonCommandKindV2.Pause));
            Assert.That(_executor.Contexts[0].CancellationToken.IsCancellationRequested, Is.True);
            _executor.Complete(0, NodeStatus.Success);
            yield return CompleteWithinFrames(pauseTask);
            Assert.That(completedActivations, Is.Empty, "The old activation result must be discarded while pausing.");
            var pausedRevision = _runner.CurrentState.state_revision;

            var resumeTask = _runner.ApplyCommandAsync(Command("resume-1", "session-1", _runner.CurrentState.run_id,
                "quest-1", oldActivation, LessonCommandKindV2.Resume));
            yield return CompleteWithinFrames(resumeTask);
            var resumed = resumeTask.GetAwaiter().GetResult();
            Assert.That(resumed.accepted, Is.True);
            Assert.That(resumed.state.status, Is.EqualTo("running"));
            Assert.That(resumed.state.node_id, Is.EqualTo("quest-1"));
            Assert.That(resumed.state.activation_id, Is.Not.EqualTo(oldActivation));
            Assert.That(resumed.state.state_revision, Is.GreaterThan(pausedRevision));
            Assert.That(_executor.Contexts.Count, Is.EqualTo(2));
            Assert.That(_executor.Contexts[1].ActivationId, Is.EqualTo(resumed.state.activation_id));
            Assert.That(lessonTask.IsCompleted, Is.False);

            _executor.Complete(1, NodeStatus.Success);
            yield return CompleteWithinFrames(lessonTask);
            Assert.That(completedActivations, Is.EqualTo(new[] { resumed.state.activation_id }));
        }

        [UnityTest]
        public IEnumerator ApplyCommandAsync_RejectsWrongTargetsAndInvalidStatesWithoutRevisionChange()
        {
            _graph = Graph(new List<LessonNodeData>
            {
                new LessonNodeData("quest-1", NodeType.Wait, new WaitNodeConfig(30))
            });
            _executor = new ControlledExecutor();
            _runnerObject = new GameObject("LessonGraphRejectionTests");
            _runner = _runnerObject.AddComponent<LessonGraphRunner>();
            _runner.Configure(_graph, new SingleRegistry(_executor));
            _runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));
            Task<LessonCommandResultV2> earlyHintTask = null;
            _runner.NodeEntered += entry =>
            {
                earlyHintTask = _runner.ApplyCommandAsync(Command("early-hint", "session-1", entry.RunId,
                    entry.NodeId, entry.ActivationId, LessonCommandKindV2.VerbalHint, "soap-touch"));
            };
            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _executor.Contexts.Count == 1);
            yield return CompleteWithinFrames(earlyHintTask);
            Assert.That(earlyHintTask.GetAwaiter().GetResult().reason, Is.EqualTo(LessonCommandReasonV2.NotActive));

            var state = _runner.CurrentState;
            var revision = state.state_revision;
            var exposedState = _runner.CurrentState;
            exposedState.state_revision = -1;
            Assert.That(_runner.CurrentState.state_revision, Is.EqualTo(revision), "CurrentState must return an immutable snapshot.");
            var rejected = new[]
            {
                Command("wrong-session", "other-session", state.run_id, state.node_id, state.activation_id, LessonCommandKindV2.Skip),
                Command("wrong-run", "session-1", "other-run", state.node_id, state.activation_id, LessonCommandKindV2.Skip),
                Command("wrong-node", "session-1", state.run_id, "other-node", state.activation_id, LessonCommandKindV2.Skip),
                Command("stale-activation", "session-1", state.run_id, state.node_id, "old-activation", LessonCommandKindV2.Skip),
                Command("resume-while-running", "session-1", state.run_id, state.node_id, state.activation_id, LessonCommandKindV2.Resume),
                Command("unsupported-hint", "session-1", state.run_id, state.node_id, state.activation_id, LessonCommandKindV2.VerbalHint, "soap-touch")
            };
            var reasons = new[]
            {
                LessonCommandReasonV2.WrongSession,
                LessonCommandReasonV2.WrongRun,
                LessonCommandReasonV2.WrongNode,
                LessonCommandReasonV2.StaleActivation,
                LessonCommandReasonV2.InvalidState,
                LessonCommandReasonV2.UnsupportedCapability
            };
            for (var index = 0; index < rejected.Length; index++)
            {
                var decisionTask = _runner.ApplyCommandAsync(rejected[index]);
                yield return CompleteWithinFrames(decisionTask);
                var decision = decisionTask.GetAwaiter().GetResult();
                Assert.That(decision.accepted, Is.False);
                Assert.That(decision.reason, Is.EqualTo(reasons[index]));
                Assert.That(decision.state.state_revision, Is.EqualTo(revision));
                Assert.That(_runner.CurrentState.state_revision, Is.EqualTo(revision));
            }

            _runner.AbortLesson();
            yield return CompleteWithinFrames(lessonTask);
        }

        [UnityTest]
        public IEnumerator ApplyCommandAsync_DuplicateSkipAppliesOneTransition()
        {
            _graph = Graph(new List<LessonNodeData>
            {
                new LessonNodeData("first", NodeType.Wait, new WaitNodeConfig(30)),
                new LessonNodeData("after-skip", NodeType.Wait, new WaitNodeConfig(30))
            }, new List<LessonEdgeData>
            {
                new LessonEdgeData("first", "after-skip", new StatusCondition(StatusCondition.Skipped))
            });
            _executor = new ControlledExecutor();
            _executor.OnExecute = (context, completion) =>
            {
                if (context.Node.Id == "first")
                    context.SkipToken.Register(() => completion.TrySetResult(NodeResult.Completed(context.Node.Id,
                        context.ActivationId, NodeStatus.Skipped, context.ElapsedSeconds)));
            };
            _runnerObject = new GameObject("LessonGraphDuplicateSkipTests");
            _runner = _runnerObject.AddComponent<LessonGraphRunner>();
            _runner.Configure(_graph, new SingleRegistry(_executor));
            _runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));
            var entered = new List<string>();
            _runner.NodeEntered += e => entered.Add(e.NodeId);
            var lessonTask = _runner.StartLessonAsync();
            yield return UntilFrames(() => _executor.Contexts.Count == 1);

            var state = _runner.CurrentState;
            var skip = Command("skip-once", "session-1", state.run_id, state.node_id, state.activation_id, LessonCommandKindV2.Skip);
            var firstDecisionTask = _runner.ApplyCommandAsync(skip);
            yield return CompleteWithinFrames(firstDecisionTask);
            var firstDecision = firstDecisionTask.GetAwaiter().GetResult();
            Assert.That(firstDecision.accepted, Is.True);
            Assert.That(_executor.Contexts[0].SkipToken.IsCancellationRequested, Is.True);

            var duplicateTask = _runner.ApplyCommandAsync(skip);
            yield return CompleteWithinFrames(duplicateTask);
            var duplicate = duplicateTask.GetAwaiter().GetResult();
            Assert.That(duplicate.accepted, Is.False);
            Assert.That(duplicate.reason, Is.EqualTo(LessonCommandReasonV2.Duplicate));

            yield return UntilFrames(() => _executor.Contexts.Count == 2);
            _executor.Complete(1, NodeStatus.Success);
            yield return CompleteWithinFrames(lessonTask);
            Assert.That(entered, Is.EqualTo(new[] { "first", "after-skip" }));
        }

        [UnityTest]
        public IEnumerator TerminalFailedNodePublishesFailedStateAndRetainsActivationIdentity()
        {
            _graph = Graph(new List<LessonNodeData>
            {
                new LessonNodeData("quest-1", NodeType.Wait, new WaitNodeConfig(30))
            });
            _executor = new ControlledExecutor
            {
                OnExecute = (context, completion) => completion.TrySetResult(NodeResult.Completed(context.Node.Id,
                    context.ActivationId, NodeStatus.Failed, context.ElapsedSeconds))
            };
            _runnerObject = new GameObject("LessonGraphTerminalStateTests");
            _runner = _runnerObject.AddComponent<LessonGraphRunner>();
            _runner.Configure(_graph, new SingleRegistry(_executor));
            _runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));

            var lessonTask = _runner.StartLessonAsync();
            yield return CompleteWithinFrames(lessonTask);

            Assert.That(lessonTask.GetAwaiter().GetResult().IsSuccess, Is.True, "Preserve the existing local LessonResult contract.");
            Assert.That(_runner.CurrentState.status, Is.EqualTo("failed"));
            Assert.That(_runner.CurrentState.node_id, Is.EqualTo("quest-1"));
            Assert.That(_runner.CurrentState.activation_id, Is.Not.Empty);
            Assert.That(_runner.CurrentState.active_node_ids, Is.Empty);
        }

        private static LessonCommandV2 Command(string id, string session, string run, string node, string activation, string kind, string binding = "")
        {
            return new LessonCommandV2
            {
                contract_version = LessonRemoteContractV2.ContractVersion,
                @event = LessonRemoteContractV2.CommandEvent,
                command_id = id,
                session_id = session,
                run_id = run,
                node_id = node,
                activation_id = activation,
                command = kind,
                binding_id = binding
            };
        }

        private static LessonGraph Graph(List<LessonNodeData> nodes, List<LessonEdgeData> edges = null)
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            graph.name = "remote-command-test-graph";
            graph.Editor_SetEntryNodeId(nodes[0].Id);
            graph.Editor_SetNodes(nodes);
            graph.Editor_SetEdges(edges ?? new List<LessonEdgeData>());
            return graph;
        }

        private static IEnumerator UntilFrames(System.Func<bool> predicate)
        {
            for (var frame = 0; frame < 30 && !predicate(); frame++) yield return null;
            Assert.That(predicate(), Is.True, "Condition did not become true within 30 editor frames.");
        }

        private static IEnumerator CompleteWithinFrames(Task task)
        {
            for (var frame = 0; frame < 30 && !task.IsCompleted; frame++) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Task did not complete within 30 editor frames.");
        }

        private sealed class ControlledExecutor : INodeExecutor
        {
            private readonly List<TaskCompletionSource<NodeResult>> _completions = new List<TaskCompletionSource<NodeResult>>();

            public readonly List<NodeExecutionContext> Contexts = new List<NodeExecutionContext>();
            public System.Action<NodeExecutionContext, TaskCompletionSource<NodeResult>> OnExecute { get; set; }

            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
            {
                var completion = new TaskCompletionSource<NodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                Contexts.Add(context);
                _completions.Add(completion);
                OnExecute?.Invoke(context, completion);
                return completion.Task;
            }

            public void Complete(int index, NodeStatus status)
            {
                var context = Contexts[index];
                _completions[index].TrySetResult(NodeResult.Completed(context.Node.Id, context.ActivationId, status, context.ElapsedSeconds));
            }

            public void CompleteAll(NodeStatus status)
            {
                for (var index = 0; index < Contexts.Count; index++) Complete(index, status);
            }
        }

        private sealed class SingleRegistry : INodeExecutorRegistry
        {
            private readonly INodeExecutor _executor;

            public SingleRegistry(INodeExecutor executor) { _executor = executor; }

            public bool TryGet(NodeType type, out INodeExecutor executor)
            {
                executor = _executor;
                return executor != null;
            }
        }
    }
}
