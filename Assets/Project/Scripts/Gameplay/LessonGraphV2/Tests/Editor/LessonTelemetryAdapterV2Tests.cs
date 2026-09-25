using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Telemetry;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonTelemetryAdapterV2Tests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        [UnityTest]
        public IEnumerator Attach_ObservesRunnerEventsOnceAndDetachStopsObservation()
        {
            var sink = new FakeSink();
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("telemetry-runner-test");
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            SetPrivateField(runner, "_currentState", State("running", 1));
            var writer = new LessonTelemetryWriterV2(sink, (delay, token) => Task.CompletedTask);
            var adapter = new LessonTelemetryAdapterV2(writer, graph, new FixedUtcClock(), () => 1d);
            var context = new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 2, 3);

            try
            {
                adapter.Attach(runner, context);
                adapter.Attach(runner, context);
                Assert.That(GetEventHandlerCount(runner, "NodeEntered"), Is.EqualTo(1));

                var observed = new NodeEnteredEvent("run-1", "activation-1", "node-1", 1d);
                Raise(runner, "NodeEntered", observed);
                Raise(runner, "NodeEntered", observed);
                adapter.Detach();
                Raise(runner, "NodeEntered", new NodeEnteredEvent("run-1", "activation-2", "node-1", 2d));

                Task flush = writer.FlushAsync(CancellationToken.None);
                yield return WaitFor(flush);

                Assert.That(GetEventHandlerCount(runner, "NodeEntered"), Is.Zero);
                Assert.That(sink.NodeEnteredAuditCount, Is.EqualTo(1));
                Assert.That(runner.CurrentState.state_revision, Is.EqualTo(1), "Telemetry observation must not drive runner state.");
            }
            finally
            {
                adapter.Detach();
                writer.Dispose();
                UnityEngine.Object.DestroyImmediate(runnerObject);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator ExecutorReadyRunningRevisionAfterNodeEntered_PersistsHintBindings()
        {
            var sink = new FakeSink();
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("telemetry-bindings-test");
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            var writer = new LessonTelemetryWriterV2(sink, (delay, token) => Task.CompletedTask);
            var adapter = new LessonTelemetryAdapterV2(writer, graph, new FixedUtcClock(), () => 1d);

            try
            {
                adapter.Attach(runner, new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 2, 3));

                LessonStateV2 enteringState = State("running", 1);
                SetPrivateField(runner, "_currentState", enteringState);
                Raise(runner, "StateChanged", enteringState);
                Raise(runner, "NodeEntered", new NodeEnteredEvent("run-1", "activation-1", "node-1", 0.01d));

                LessonStateV2 executorReadyState = State("running", 2);
                executorReadyState.bindings = new[]
                {
                    new LessonBindingV2
                    {
                        binding_id = "soap-touch",
                        npc_binding_id = "npc-soap",
                        can_verbal_hint = true,
                        can_visual_hint = false
                    }
                };
                SetPrivateField(runner, "_currentState", executorReadyState);
                Raise(runner, "StateChanged", executorReadyState);

                Task flush = writer.FlushAsync(CancellationToken.None);
                yield return WaitFor(flush);

                Assert.That(sink.LatestState.state_revision, Is.EqualTo(2));
                Assert.That(sink.LatestState.bindings, Has.Length.EqualTo(1));
                Assert.That(sink.LatestState.bindings[0].binding_id, Is.EqualTo("soap-touch"));
                Assert.That(sink.LatestState.bindings[0].npc_binding_id, Is.EqualTo("npc-soap"));
                Assert.That(sink.LatestState.bindings[0].can_verbal_hint, Is.True);
            }
            finally
            {
                adapter.Detach();
                writer.Dispose();
                UnityEngine.Object.DestroyImmediate(runnerObject);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator SceneUnload_CapturesTerminalStateAndClosesOpenQuestLog()
        {
            var sink = new FakeSink();
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("telemetry-unload-test");
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            LessonStateV2 originalState = State("running", 1);
            SetPrivateField(runner, "_currentState", originalState);
            var writer = new LessonTelemetryWriterV2(sink, (delay, token) => Task.CompletedTask);
            var adapter = new LessonTelemetryAdapterV2(writer, graph, new FixedUtcClock(), () => 2d);
            var context = new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 2, 3);

            try
            {
                adapter.Attach(runner, context);
                Raise(runner, "NodeEntered", new NodeEnteredEvent("run-1", "activation-1", "node-1", 1d));
                UnityEngine.Object.DestroyImmediate(runnerObject);
                runnerObject = null;
                adapter.CaptureSceneUnload();
                adapter.Detach();

                Task flush = writer.FlushAsync(CancellationToken.None);
                yield return WaitFor(flush);

                Assert.That(sink.SessionCancelledAuditCount, Is.EqualTo(1));
                Assert.That(sink.NodeLogCount, Is.EqualTo(1));
                Assert.That(sink.QuestLogCount, Is.EqualTo(1));
                Assert.That(sink.LatestState.status, Is.EqualTo("cancelled"));
                Assert.That(originalState.status, Is.EqualTo("running"), "Scene teardown capture must not mutate the runner snapshot.");
            }
            finally
            {
                adapter.Detach();
                writer.Dispose();
                UnityEngine.Object.DestroyImmediate(runnerObject);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator SceneUnload_PreservesAnAlreadyTerminalRunnerSnapshot()
        {
            var sink = new FakeSink();
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("terminal-unload-test");
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            SetPrivateField(runner, "_currentState", State("completed", 3));
            var writer = new LessonTelemetryWriterV2(sink, (delay, token) => Task.CompletedTask);
            var adapter = new LessonTelemetryAdapterV2(writer, graph, new FixedUtcClock(), () => 3d);

            try
            {
                adapter.Attach(runner, new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 2, 3));
                Assert.That(adapter.CaptureSceneUnload(), Is.True);
                adapter.Detach();

                Task flush = writer.FlushAsync(CancellationToken.None);
                yield return WaitFor(flush);

                Assert.That(sink.SessionCompletedAuditCount, Is.EqualTo(1));
                Assert.That(sink.LatestState.status, Is.EqualTo("completed"));
                Assert.That(sink.LatestState.active_node_ids, Is.Empty);
                Assert.That(sink.LatestState.state_revision, Is.EqualTo(3));
            }
            finally
            {
                adapter.Detach();
                writer.Dispose();
                UnityEngine.Object.DestroyImmediate(runnerObject);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator PausingState_IsPersistedWithoutInventingAnAuditEvent()
        {
            var sink = new FakeSink();
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("telemetry-pausing-test");
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            SetPrivateField(runner, "_currentState", State("running", 1));
            var writer = new LessonTelemetryWriterV2(sink, (delay, token) => Task.CompletedTask);
            var adapter = new LessonTelemetryAdapterV2(writer, graph, new FixedUtcClock(), () => 1d);

            try
            {
                adapter.Attach(runner, new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 2, 3));
                Raise(runner, "StateChanged", State("pausing", 2));

                Task flush = writer.FlushAsync(CancellationToken.None);
                yield return WaitFor(flush);

                Assert.That(sink.LatestState.status, Is.EqualTo("pausing"));
                Assert.That(sink.LatestState.state_revision, Is.EqualTo(2));
                Assert.That(sink.AuditEventCount, Is.Zero, "The intermediate state is a projection, not a new audit lifecycle event.");
                Assert.That(runner.CurrentState.status, Is.EqualTo("running"), "Telemetry observation must not mutate runner state.");
            }
            finally
            {
                adapter.Detach();
                writer.Dispose();
                UnityEngine.Object.DestroyImmediate(runnerObject);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator DelayedManualStart_ElapsedTelemetryBeginsAtFirstRunState()
        {
            var sink = new FakeSink();
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            var runnerObject = new GameObject("telemetry-delayed-start-test");
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            var writer = new LessonTelemetryWriterV2(sink, (delay, token) => Task.CompletedTask);
            var adapter = new LessonTelemetryAdapterV2(writer, graph, new FixedUtcClock());

            try
            {
                adapter.Attach(runner, new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 2, 3));
                yield return WaitFor(Task.Delay(500));

                LessonStateV2 running = State("running", 1);
                SetPrivateField(runner, "_currentState", running);
                Raise(runner, "StateChanged", running);
                Raise(runner, "NodeEntered", new NodeEnteredEvent("run-1", "activation-1", "node-1", 0.01d));
                yield return WaitFor(Task.Delay(250));

                var nodeResult = NodeResult.Completed("node-1", "activation-1", NodeStatus.Success, 0.25d);
                Raise(runner, "NodeCompleted", new NodeCompletedEvent(nodeResult));
                Raise(runner, "LessonCompleted", new LessonCompletedEvent(LessonResult.Completed("run-1", nodeResult, 0.25d)));

                Task flush = writer.FlushAsync(CancellationToken.None);
                yield return WaitFor(flush);

                Assert.That(sink.NodeEnteredElapsedSeconds, Is.LessThan(sink.TerminalElapsedSeconds),
                    "Node telemetry should use the run timeline, not the time spent composed before manual start.");
                Assert.That(sink.TerminalElapsedSeconds, Is.EqualTo(0.25d),
                    "The terminal projection keeps the runner's elapsed value as its shared run-time reference.");
            }
            finally
            {
                adapter.Detach();
                writer.Dispose();
                UnityEngine.Object.DestroyImmediate(runnerObject);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void LegacyFirebasePersistence_IsSkippedOnlyWhenAnActiveV2InstallerOwnsTheScene(bool hasActiveInstaller, bool expectedSkip)
        {
            Assert.That(LessonGraphRunnerInstaller.ShouldSkipLegacyFirebasePersistence(hasActiveInstaller), Is.EqualTo(expectedSkip));
        }

        [Test]
        public void ActiveInstaller_SuppressesLegacyPersistenceEvenWhenV2CompositionFails()
        {
            var installerObject = new GameObject("invalid-v2-installer-test");
            try
            {
                LogAssert.Expect(LogType.Error,
                    "[LessonGraphV2] V2 installer composition failed: LessonGraphRunnerInstaller needs a LessonGraph asset.");
                LessonGraphRunnerInstaller installer = installerObject.AddComponent<LessonGraphRunnerInstaller>();

                Assert.That(installer.IsTelemetryPersistenceReady, Is.False);
                Assert.That(installer.TelemetryPersistenceError, Is.Not.Null.And.Not.Empty);
                Assert.That(LessonGraphRunnerInstaller.ShouldSkipLegacyFirebasePersistence(), Is.True,
                    "An enabled V2 installer keeps TimeManager off the legacy Firestore writer even when setup fails closed.");

                installer.enabled = false;
                Assert.That(LessonGraphRunnerInstaller.ShouldSkipLegacyFirebasePersistence(), Is.False,
                    "A disabled installer does not claim persistence ownership for a legacy scene.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(installerObject);
            }
        }

        [Test]
        public void ActiveUnreadyInstaller_BlocksManualRunnerStart()
        {
            var runnerObject = new GameObject("unready-v2-runner-test");
            try
            {
                LessonGraphRunner runner = runnerObject.AddComponent<LessonGraphRunner>();
                LogAssert.Expect(LogType.Error,
                    "[LessonGraphV2] V2 installer composition failed: LessonGraphRunnerInstaller needs a LessonGraph asset.");
                runnerObject.AddComponent<LessonGraphRunnerInstaller>();
                LogAssert.Expect(LogType.Error,
                    "[LessonGraphV2] Refusing to start because the active installer could not authorize the lesson. V2 installer composition failed: LessonGraphRunnerInstaller needs a LessonGraph asset.");

                Task<LessonResult> start = runner.StartLessonAsync();

                Assert.That(start.IsCompleted, Is.True);
                Assert.That(start.GetAwaiter().GetResult().FailureReason, Is.EqualTo(LessonFailureReason.InvalidGraph));
                Assert.That(runner.CurrentState, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void InstallerOnDifferentGameObject_BlocksItsConfiguredRunnerWhenUnready()
        {
            var runnerObject = new GameObject("external-v2-runner-test");
            var installerObject = new GameObject("external-v2-installer-test");
            try
            {
                LessonGraphRunner runner = runnerObject.AddComponent<LessonGraphRunner>();
                LogAssert.Expect(LogType.Error,
                    "[LessonGraphV2] V2 installer composition failed: LessonGraphRunnerInstaller needs a LessonGraph asset.");
                LessonGraphRunnerInstaller installer = installerObject.AddComponent<LessonGraphRunnerInstaller>();
                SetPrivateField(installer, "_runner", runner);

                Assert.Throws<InvalidOperationException>(() => installer.Configure());
                LogAssert.Expect(LogType.Error,
                    "[LessonGraphV2] Refusing to start because the active installer could not authorize the lesson. V2 installer composition failed: LessonGraphRunnerInstaller needs a LessonGraph asset.");

                Task<LessonResult> start = runner.StartLessonAsync();

                Assert.That(start.IsCompleted, Is.True);
                Assert.That(start.GetAwaiter().GetResult().FailureReason, Is.EqualTo(LessonFailureReason.InvalidGraph));
                Assert.That(runner.CurrentState, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(installerObject);
                UnityEngine.Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void DisabledExternalUnreadyInstaller_BlocksManualRunnerStartUntilDestroy()
        {
            var runnerObject = new GameObject("disabled-external-v2-runner-test");
            var installerObject = new GameObject("disabled-external-v2-installer-test");
            try
            {
                LessonGraphRunner runner = runnerObject.AddComponent<LessonGraphRunner>();
                LogAssert.Expect(LogType.Error,
                    "[LessonGraphV2] V2 installer composition failed: LessonGraphRunnerInstaller needs a LessonGraph asset.");
                LessonGraphRunnerInstaller installer = installerObject.AddComponent<LessonGraphRunnerInstaller>();
                SetPrivateField(installer, "_runner", runner);
                Assert.Throws<InvalidOperationException>(() => installer.Configure());

                installer.enabled = false;
                LogAssert.Expect(LogType.Error,
                    "[LessonGraphV2] Refusing to start because the active installer could not authorize the lesson. " +
                    "V2 telemetry installer is disabled while its runner is still attached.");

                Task<LessonResult> start = runner.StartLessonAsync();

                Assert.That(start.IsCompleted, Is.True);
                Assert.That(start.GetAwaiter().GetResult().FailureReason, Is.EqualTo(LessonFailureReason.InvalidGraph));
                Assert.That(runner.CurrentState, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(installerObject);
                UnityEngine.Object.DestroyImmediate(runnerObject);
            }
        }

        [Test]
        public void FailedReconfigure_DoesNotTransferOwnerFromPreviouslyConfiguredRunner()
        {
            var originalRunnerObject = new GameObject("original-v2-runner-test");
            var replacementRunnerObject = new GameObject("replacement-v2-runner-test");
            var installerObject = new GameObject("reconfigured-v2-installer-test");
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            try
            {
                LessonGraphRunner originalRunner = originalRunnerObject.AddComponent<LessonGraphRunner>();
                LessonGraphRunner replacementRunner = replacementRunnerObject.AddComponent<LessonGraphRunner>();
                LogAssert.Expect(LogType.Error,
                    "[LessonGraphV2] V2 installer composition failed: LessonGraphRunnerInstaller needs a LessonGraph asset.");
                LessonGraphRunnerInstaller installer = installerObject.AddComponent<LessonGraphRunnerInstaller>();
                LessonGraphBindings bindings = installerObject.AddComponent<LessonGraphBindings>();
                SetPrivateField(installer, "_runner", originalRunner);
                SetPrivateField(installer, "_lessonGraph", graph);
                SetPrivateField(installer, "_bindings", bindings);
                installer.Configure();
                SetPrivateField(installer, "_telemetryConfigured", true);

                SetPrivateField(installer, "_runner", replacementRunner);
                Assert.Throws<InvalidOperationException>(() => installer.Configure());

                Assert.That(GetPrivateField<LessonGraphRunner>(originalRunner, "_telemetryInstaller"), Is.SameAs(installer));
                Assert.That(GetPrivateField<LessonGraphRunner>(replacementRunner, "_telemetryInstaller"), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(installerObject);
                UnityEngine.Object.DestroyImmediate(originalRunnerObject);
                UnityEngine.Object.DestroyImmediate(replacementRunnerObject);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void RunnerWithoutInstaller_ContinuesThroughNormalGraphValidation()
        {
            var runnerObject = new GameObject("standalone-runner-test");
            try
            {
                LessonGraphRunner runner = runnerObject.AddComponent<LessonGraphRunner>();
                Task<LessonResult> start = runner.StartLessonAsync();

                Assert.That(start.IsCompleted, Is.True);
                Assert.That(start.GetAwaiter().GetResult().FailureReason, Is.EqualTo(LessonFailureReason.InvalidGraph));
                Assert.That(runner.CurrentState, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runnerObject);
            }
        }

        private static LessonStateV2 State(string status, int revision) => new LessonStateV2
        {
            contract_version = 2,
            session_id = "session-1",
            run_id = "run-1",
            graph_id = "graph-1",
            lesson_id = "lesson-1",
            launch_token = "launch-1",
            lesson_voice_revision = 2,
            child_phrase_revision = 3,
            node_id = "node-1",
            node_type = "Quest",
            node_index = 0,
            activation_id = "activation-1",
            status = status,
            updated_at_utc = T0.ToString("O"),
            state_revision = revision,
            active_node_ids = new[] { "node-1" },
            bindings = new LessonBindingV2[0]
        };

        private static int GetEventHandlerCount(LessonGraphRunner runner, string eventName)
        {
            var field = typeof(LessonGraphRunner).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            var handler = field?.GetValue(runner) as Delegate;
            return handler?.GetInvocationList().Length ?? 0;
        }

        private static void Raise<T>(LessonGraphRunner runner, string eventName, T value)
        {
            var field = typeof(LessonGraphRunner).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            var handler = field?.GetValue(runner) as Delegate;
            handler?.DynamicInvoke(value);
        }

        private static void SetPrivateField(object target, string fieldName, object value) =>
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static T GetPrivateField<T>(object target, string fieldName) =>
            (T)target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static IEnumerator WaitFor(Task task, int maxFrames = 120)
        {
            int frame = 0;
            while (!task.IsCompleted && frame++ < maxFrames) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Telemetry operation did not complete within the frame bound.");
            task.GetAwaiter().GetResult();
        }

        private sealed class FixedUtcClock : IUtcClockV2
        {
            public DateTimeOffset UtcNow => T0;
        }

        private sealed class FakeSink : ILessonTelemetrySinkV2
        {
            public int NodeEnteredAuditCount;
            public int AuditEventCount;
            public int SessionCancelledAuditCount;
            public int SessionCompletedAuditCount;
            public int NodeLogCount;
            public int QuestLogCount;
            public LessonStateV2 LatestState;
            public double NodeEnteredElapsedSeconds = -1d;
            public double TerminalElapsedSeconds = -1d;

            public Task WriteStateAsync(LessonStateV2 state, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                LatestState = state;
                return Task.CompletedTask;
            }

            public Task UpsertBatchAsync(TelemetryWriteBatchV2 batch, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                foreach (var audit in batch.audit_events_by_id.Values)
                {
                    AuditEventCount++;
                    if (audit.event_type == LessonTelemetryEventTypeV2.NodeEntered) NodeEnteredAuditCount++;
                    if (audit.event_type == LessonTelemetryEventTypeV2.NodeEntered) NodeEnteredElapsedSeconds = audit.elapsed_seconds;
                    if (audit.event_type == LessonTelemetryEventTypeV2.LessonCancelled) SessionCancelledAuditCount++;
                    if (audit.event_type == LessonTelemetryEventTypeV2.LessonCompleted) SessionCompletedAuditCount++;
                    if (audit.event_type == LessonTelemetryEventTypeV2.LessonCompleted ||
                        audit.event_type == LessonTelemetryEventTypeV2.LessonCancelled ||
                        audit.event_type == LessonTelemetryEventTypeV2.LessonFailed)
                        TerminalElapsedSeconds = audit.elapsed_seconds;
                }
                NodeLogCount += batch.node_logs_by_id.Count;
                QuestLogCount += batch.quest_logs_by_id.Count;
                return Task.CompletedTask;
            }
        }
    }
}
