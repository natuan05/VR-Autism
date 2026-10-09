using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using VRAutism.Core;
using VRAutism.Core.Models;
using VRAutism.Core.Telemetry;
using VRAutism.Cloud.RTDB;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonGraphSensorLifecycleV2Tests
    {
        [Test]
        public void AggregatedSnapshot_SerializesAdditiveLessonGraphScopeWithStableKeys()
        {
            var snapshot = new AggregatedSnapshot
            {
                runtime = "lesson_graph_v2",
                session_id = "session-1",
                run_id = "run-1",
                node_id = "node-2",
                activation_id = "activation-2",
                binding_id = "soap-touch",
                active_binding_ids = new[] { "soap-touch", "soap-hold" },
                node_index = 2,
                status = "running"
            };

            string json = JsonUtility.ToJson(snapshot);

            StringAssert.Contains("\"runtime\":\"lesson_graph_v2\"", json);
            StringAssert.Contains("\"session_id\":\"session-1\"", json);
            StringAssert.Contains("\"run_id\":\"run-1\"", json);
            StringAssert.Contains("\"node_id\":\"node-2\"", json);
            StringAssert.Contains("\"activation_id\":\"activation-2\"", json);
            StringAssert.Contains("\"binding_id\":\"soap-touch\"", json);
            StringAssert.Contains("\"active_binding_ids\":[\"soap-touch\",\"soap-hold\"]", json);
            StringAssert.Contains("\"node_index\":2", json);
            StringAssert.Contains("\"status\":\"running\"", json);
        }

        [Test]
        public void SensorHarvester_ScopeChangesDiscardPartialSamplesAndPauseClearsTargets()
        {
            var harvesterObject = new GameObject("sensor-harvester-test");
            var legacyTargetObject = new GameObject("legacy-target");
            var firstTargetObject = new GameObject("first-target");
            var secondTargetObject = new GameObject("second-target");
            var harvester = harvesterObject.AddComponent<SensorHarvester>();

            try
            {
                harvester.SetLessonGraphRuntimeEnabled(true);
                harvester.SetLessonGraphScope(
                    "session-1", "run-1", "node-1", "activation-1", 0, "running",
                    new[] { "soap-touch", "soap-hold" },
                    new[] { firstTargetObject.transform, secondTargetObject.transform });

                harvester.SetCurrentTarget(legacyTargetObject.transform);
                InvokePrivate(harvester, "SampleToBuffer");
                Assert.That(GetPrivateField<int>(harvester, "_bufferCount"), Is.EqualTo(1));

                AggregatedSnapshot firstSnapshot = harvester.AggregateAndFlush(1f);
                Assert.That(firstSnapshot.runtime, Is.EqualTo("lesson_graph_v2"));
                Assert.That(firstSnapshot.binding_id, Is.EqualTo("soap-touch"));
                CollectionAssert.AreEqual(new[] { "soap-touch", "soap-hold" }, firstSnapshot.active_binding_ids);
                Assert.That(firstSnapshot.expected_target, Is.EqualTo("first-target, second-target"));

                InvokePrivate(harvester, "SampleToBuffer");
                harvester.SetLessonGraphScope(
                    "session-1", "run-1", "node-2", "activation-2", 1, "running",
                    new[] { "dry-hands" }, new[] { secondTargetObject.transform });
                Assert.That(GetPrivateField<int>(harvester, "_bufferCount"), Is.Zero,
                    "The old activation's partial window must be discarded at a scope boundary.");

                harvester.SetAcceptedVisualHintTime(4.25f);
                harvester.SetLessonGraphScope(
                    "session-1", "run-1", "node-2", "activation-2", 1, "paused",
                    new string[0], new Transform[0]);
                AggregatedSnapshot pausedSnapshot = harvester.AggregateAndFlush(2f);

                Assert.That(pausedSnapshot.status, Is.EqualTo("paused"));
                Assert.That(pausedSnapshot.binding_id, Is.Empty);
                Assert.That(pausedSnapshot.active_binding_ids, Is.Empty);
                Assert.That(pausedSnapshot.expected_target, Is.EqualTo("None"));
                Assert.That(pausedSnapshot.last_visual_hint_time, Is.EqualTo(4.25f));
            }
            finally
            {
                Object.DestroyImmediate(harvesterObject);
                Object.DestroyImmediate(legacyTargetObject);
                Object.DestroyImmediate(firstTargetObject);
                Object.DestroyImmediate(secondTargetObject);
            }
        }

        [Test]
        public void LessonGraphBindings_TryGetBoundSourceWorksWhenSourceIsUnavailable()
        {
            var sourceObject = new GameObject("quest-source-test");
            var bindingsObject = new GameObject("bindings-test");
            var source = sourceObject.AddComponent<TouchQuestSourceV2>();
            var bindings = bindingsObject.AddComponent<LessonGraphBindings>();

            try
            {
                SetPrivateField(source, "_bindingId", "soap-touch");
                SetPrivateField(bindings, "_entries", new List<QuestBindingEntry>
                {
                    new QuestBindingEntry("soap-touch", source)
                });
                InvokePrivate(bindings, "BuildRegistry");
                source.enabled = false;

                Assert.That(bindings.Resolve("soap-touch").IsSuccess, Is.False);
                Assert.That(bindings.TryGetBoundSource("soap-touch", out QuestSourceV2 resolved), Is.True);
                Assert.That(resolved, Is.SameAs(source));
            }
            finally
            {
                Object.DestroyImmediate(bindingsObject);
                Object.DestroyImmediate(sourceObject);
            }
        }

        [Test]
        public void RemoteCommandListener_DisablingDropsQueuedDispatches()
        {
            RemoteCommandListener previousListener = RemoteCommandListener.Instance;
            SetRemoteCommandListenerInstance(null);
            var listenerObject = new GameObject("remote-command-listener-test");
            var listener = listenerObject.AddComponent<RemoteCommandListener>();
            var queue = GetPrivateField<ConcurrentQueue<System.Action>>(listener, "_mainThreadQueue");
            queue.Enqueue(() => Assert.Fail("A command queued before disable must not dispatch."));

            try
            {
                listener.enabled = false;
                InvokePrivate(listener, "OnDisable");
                Assert.That(queue.IsEmpty, Is.True);
            }
            finally
            {
                SetRemoteCommandListenerInstance(previousListener);
                Object.DestroyImmediate(listenerObject);
            }
        }

        [Test]
        public void LiveSessionReporter_PostAwaitGuardRejectsSessionAfterContextClear()
        {
            SessionContext previousSessionContext = SessionContext.Instance;
            LiveSessionReporter previousReporter = LiveSessionReporter.Instance;
            SessionContext.Instance = null;
            SetReporterInstance(null);
            var contextObject = new GameObject("session-context-clear-test");
            var context = contextObject.AddComponent<SessionContext>();
            SessionContext.Instance = context;
            context.SessionId = "session-1";

            var reporterObject = new GameObject("live-session-reporter-clear-test");
            var reporter = reporterObject.AddComponent<LiveSessionReporter>();
            SetPrivateField(reporter, "_activeSessionId", "session-1");
            SetPrivateField(reporter, "_sessionOperationGeneration", 5);

            MethodInfo postAwaitGuard = typeof(LiveSessionReporter).GetMethod(
                "IsCurrentSessionOperation",
                BindingFlags.Instance | BindingFlags.NonPublic);

            try
            {
                Assert.That(postAwaitGuard, Is.Not.Null);
                Assert.That(postAwaitGuard.Invoke(reporter, new object[] { "session-1", 5 }), Is.EqualTo(true));

                context.Clear();

                Assert.That(postAwaitGuard.Invoke(reporter, new object[] { "session-1", 5 }), Is.EqualTo(false),
                    "An existing but cleared SessionContext must invalidate post-await handshake effects even when the reporter generation still matches.");
            }
            finally
            {
                SetReporterInstance(previousReporter);
                SessionContext.Instance = previousSessionContext;
                Object.DestroyImmediate(reporterObject);
                Object.DestroyImmediate(contextObject);
            }
        }

        [Test]
        public void LessonGraphLifecycle_EndsOnceAndShowsCongratulationsOnlyOnSuccess()
        {
            SessionContext previousSessionContext = SessionContext.Instance;
            LiveSessionReporter previousReporter = LiveSessionReporter.Instance;
            SessionContext.Instance = null;
            SetReporterInstance(null);
            var contextObject = new GameObject("session-context-test");
            var context = contextObject.AddComponent<SessionContext>();
            SessionContext.Instance = context;
            context.SessionId = "session-1";

            var reporterObject = new GameObject("live-session-reporter-test");
            var reporter = reporterObject.AddComponent<LiveSessionReporter>();
            SetReporterInstance(reporter);

            var congratulations = new GameObject("congratulations-test");
            var lifecycleObject = new GameObject("lesson-lifecycle-test");
            lifecycleObject.SetActive(false);
            var runner = lifecycleObject.AddComponent<LessonGraphRunner>();
            var streamer = lifecycleObject.AddComponent<TelemetryStreamer>();
            SetPrivateField(streamer, "_sessionId", "session-1");
            var lifecycle = lifecycleObject.AddComponent<VRAutism.Gameplay.LessonGraphV2.Integration.LessonGraphLifecycleV2>();
            SetPrivateField(lifecycle, "_runner", runner);
            SetPrivateField(lifecycle, "_telemetryStreamer", streamer);
            SetPrivateField(lifecycle, "_congratulationsRoot", congratulations);

            int successEventCount = 0;
            var successEvent = new UnityEngine.Events.UnityEvent();
            successEvent.AddListener(() => successEventCount++);
            SetPrivateField(lifecycle, "_onSuccessfulCompletion", successEvent);

            try
            {
                SetPrivateField(runner, "_currentState", TerminalState("run-1", "completed"));
                lifecycleObject.SetActive(true);

                InvokePrivate(lifecycle, "OnLessonCompleted", new LessonCompletedEvent(
                    LessonResult.Completed("run-1", NodeResult.Completed("finish", "activation-1", NodeStatus.Success, 1d), 1d)));
                InvokePrivate(lifecycle, "OnLessonCompleted", new LessonCompletedEvent(
                    LessonResult.Completed("run-1", NodeResult.Completed("finish", "activation-1", NodeStatus.Success, 1d), 1d)));

                Assert.That(congratulations.activeSelf, Is.True);
                Assert.That(successEventCount, Is.EqualTo(1));
                Assert.That(GetPrivateField<int>(reporter, "_sessionOperationGeneration"), Is.EqualTo(1));
                Assert.That(GetPrivateField<string>(streamer, "_sessionId"), Is.Empty);

                congratulations.SetActive(false);
                SetPrivateField(runner, "_currentState", TerminalState("run-2", "failed"));
                InvokePrivate(lifecycle, "OnLessonCompleted", new LessonCompletedEvent(
                    LessonResult.Failed("run-2", LessonFailureReason.Aborted, 1d)));

                Assert.That(congratulations.activeSelf, Is.False);
                Assert.That(successEventCount, Is.EqualTo(1));
                Assert.That(GetPrivateField<int>(reporter, "_sessionOperationGeneration"), Is.EqualTo(1),
                    "One selected session owns exactly one ended signal.");
            }
            finally
            {
                SetReporterInstance(previousReporter);
                SessionContext.Instance = previousSessionContext;
                Object.DestroyImmediate(lifecycleObject);
                Object.DestroyImmediate(congratulations);
                Object.DestroyImmediate(reporterObject);
                Object.DestroyImmediate(contextObject);
            }

        }

        private static LessonStateV2 TerminalState(string runId, string status) => new LessonStateV2
        {
            session_id = "session-1",
            run_id = runId,
            node_id = "finish",
            activation_id = "activation-1",
            node_index = 0,
            status = status
        };

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        private static void InvokePrivate(object target, string methodName, object argument) =>
            target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, new[] { argument });

        private static T GetPrivateField<T>(object target, string fieldName) =>
            (T)GetPrivateFieldInfo(target, fieldName).GetValue(target);

        private static void SetPrivateField(object target, string fieldName, object value) =>
            GetPrivateFieldInfo(target, fieldName).SetValue(target, value);

        private static FieldInfo GetPrivateFieldInfo(object target, string fieldName)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }

            Assert.Fail($"Could not find private field '{fieldName}' on {target.GetType().FullName} or its base types.");
            return null;
        }

        private static void SetReporterInstance(LiveSessionReporter reporter)
        {
            SetStaticInstance(typeof(LiveSessionReporter), reporter);
        }

        private static void SetRemoteCommandListenerInstance(RemoteCommandListener listener) =>
            SetStaticInstance(typeof(RemoteCommandListener), listener);

        private static void SetStaticInstance(Type type, object value) =>
            type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetSetMethod(true)
                .Invoke(null, new[] { value });
    }
}
