using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Telemetry;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class FirebaseLessonTelemetrySinkV2Tests
    {
        [Test]
        public void LateAuditAfterTerminal_DoesNotResetSessionOrReplaceLegacyArrays()
        {
            var current = new Dictionary<string, object>
            {
                ["completion_status"] = "completed",
                ["duration"] = 12d,
                ["finish_time"] = "2026-09-24T12:00:12.0000000Z",
                ["score"] = 4,
                ["unrelated"] = "preserve-me",
                ["node_logs"] = new List<object> { "legacy-node" },
                ["quest_logs"] = new List<object> { "legacy-quest" }
            };
            var batch = new TelemetryWriteBatchV2();
            batch.audit_events_by_id["stable-node-entry"] = new LessonAuditEventDataV2
            {
                event_id = "stable-node-entry",
                event_type = LessonTelemetryEventTypeV2.NodeEntered,
                session_id = "session-1",
                run_id = "run-1",
                occurred_at_utc = "2026-09-24T12:00:13.0000000Z",
                elapsed_seconds = 13d,
                status = "running"
            };

            Dictionary<string, object> patch = BuildPatch(current, batch);

            Assert.That(patch.ContainsKey("completion_status"), Is.False);
            Assert.That(patch.ContainsKey("duration"), Is.False);
            Assert.That(patch.ContainsKey("finish_time"), Is.False);
            Assert.That(patch.ContainsKey("node_logs"), Is.False);
            Assert.That(patch.ContainsKey("quest_logs"), Is.False);
            Assert.That(current["completion_status"], Is.EqualTo("completed"));
            Assert.That(current["duration"], Is.EqualTo(12d));
            Assert.That(current["unrelated"], Is.EqualTo("preserve-me"));
        }

        [Test]
        public void FirstTerminalObservation_SeedsMetadataAndTerminalProjection()
        {
            var batch = new TelemetryWriteBatchV2();
            batch.audit_events_by_id["stable-lesson-terminal"] = new LessonAuditEventDataV2
            {
                event_id = "stable-lesson-terminal",
                event_type = LessonTelemetryEventTypeV2.LessonCancelled,
                session_id = "session-1",
                run_id = "run-1",
                occurred_at_utc = "2026-09-24T12:00:12.0000000Z",
                elapsed_seconds = 12d,
                status = "cancelled"
            };

            Dictionary<string, object> patch = BuildPatch(new Dictionary<string, object>(), batch);

            Assert.That(patch["completion_status"], Is.EqualTo("cancelled"));
            Assert.That(patch["duration"], Is.EqualTo(12d));
            Assert.That(patch["finish_time"], Is.EqualTo("2026-09-24T12:00:12.0000000Z"));
            Assert.That(patch["score"], Is.EqualTo(0));
            Assert.That(patch.ContainsKey("quest_logs"), Is.False, "No V2 quest projection should overwrite an existing field before the first V2 quest log.");
        }

        [Test]
        public void CompletedTerminal_MapsLegacySessionStatusToSuccessWithoutChangingV2Audit()
        {
            var completed = new LessonAuditEventDataV2
            {
                event_id = "stable-lesson-completed",
                event_type = LessonTelemetryEventTypeV2.LessonCompleted,
                session_id = "session-1",
                run_id = "run-1",
                occurred_at_utc = "2026-09-24T12:00:12.0000000Z",
                elapsed_seconds = 12d,
                status = "completed"
            };
            var batch = new TelemetryWriteBatchV2();
            batch.audit_events_by_id[completed.event_id] = completed;

            Dictionary<string, object> patch = BuildPatch(new Dictionary<string, object>(), batch);

            Assert.That(patch["completion_status"], Is.EqualTo("success"));
            Assert.That(completed.status, Is.EqualTo("completed"), "V2 audit vocabulary remains unchanged.");
        }

        [Test]
        public void PausingState_WithNoActiveNodesOrBindings_ProjectsEmptyRtdbArrays()
        {
            var state = new LessonStateV2
            {
                contract_version = 2,
                session_id = "session-1",
                run_id = "run-1",
                status = "pausing",
                active_node_ids = new string[0],
                bindings = new LessonBindingV2[0]
            };

            IDictionary<string, object> projection = BuildRtdbProjection(state);

            Assert.That(projection["status"], Is.EqualTo("pausing"));
            Assert.That(projection["active_node_ids"], Is.TypeOf<List<object>>());
            Assert.That(((List<object>)projection["active_node_ids"]).Count, Is.Zero);
            Assert.That(projection["bindings"], Is.TypeOf<List<object>>());
            Assert.That(((List<object>)projection["bindings"]).Count, Is.Zero);
        }

        [Test]
        public void RtdbRevisionPolicy_AcceptsOnlyNewerStateFromTheSessionOwner()
        {
            LessonStateV2 incoming = new LessonStateV2 { run_id = "run-1", state_revision = 6 };
            string conflict;

            Assert.That(ShouldWriteRtdbState(new Dictionary<string, object>
            {
                ["run_id"] = "run-1",
                ["state_revision"] = 6L
            }, incoming, out conflict), Is.False, "A revision tie must not rewrite the RTDB projection.");
            Assert.That(conflict, Is.Null);

            Assert.That(ShouldWriteRtdbState(new Dictionary<string, object>
            {
                ["run_id"] = "run-1",
                ["state_revision"] = 7L
            }, incoming, out conflict), Is.False, "An older revision must not roll back the RTDB projection.");
            Assert.That(conflict, Is.Null);

            Assert.That(ShouldWriteRtdbState(new Dictionary<string, object>
            {
                ["run_id"] = "run-1",
                ["state_revision"] = 5L
            }, incoming, out conflict), Is.True);
            Assert.That(conflict, Is.Null);

            Assert.That(ShouldWriteRtdbState(new Dictionary<string, object>
            {
                ["run_id"] = "run-other",
                ["state_revision"] = 1L
            }, incoming, out conflict), Is.False);
            Assert.That(conflict, Is.EqualTo("run-other"));
        }

        [Test]
        public void RtdbStatePathTargetsOnlyTheSessionLessonGraphChild()
        {
            string[] pathSegments = BuildRtdbLessonGraphPathSegments("session-1");

            Assert.That(pathSegments, Is.EqualTo(new[] { "live_sessions", "session-1", "lesson_graph" }),
                "The RTDB transaction must target exactly live_sessions/{sessionId}/lesson_graph, with no sibling writes.");
            Assert.That(string.Join("/", pathSegments), Is.EqualTo("live_sessions/session-1/lesson_graph"));
        }

        private static Dictionary<string, object> BuildPatch(IDictionary<string, object> existing, TelemetryWriteBatchV2 batch)
        {
            MethodInfo method = typeof(FirebaseLessonTelemetrySinkV2).GetMethod("BuildSessionPatch",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "The session merge projection helper should remain available for its contract test.");
            return (Dictionary<string, object>)method.Invoke(null, new object[]
            {
                existing,
                new Dictionary<string, object>
                {
                    ["session_id"] = "session-1",
                    ["completion_status"] = "running",
                    ["duration"] = 0d,
                    ["score"] = 0
                },
                batch
            });
        }

        private static IDictionary<string, object> BuildRtdbProjection(LessonStateV2 state)
        {
            MethodInfo method = typeof(FirebaseLessonTelemetrySinkV2).GetMethod("ToRtdbState",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "The RTDB projection helper should remain available for its contract test.");
            return (IDictionary<string, object>)method.Invoke(null, new object[] { state });
        }

        private static string[] BuildRtdbLessonGraphPathSegments(string sessionId)
        {
            MethodInfo method = typeof(FirebaseLessonTelemetrySinkV2).GetMethod("BuildRtdbLessonGraphPathSegments",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "The RTDB path used by the production state transaction should remain available for its contract test.");
            return (string[])method.Invoke(null, new object[] { sessionId });
        }

        private static bool ShouldWriteRtdbState(IDictionary<string, object> current, LessonStateV2 incoming,
            out string conflictingRunId)
        {
            MethodInfo method = typeof(FirebaseLessonTelemetrySinkV2).GetMethod("ShouldWriteRtdbState",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "The production RTDB transaction policy helper should remain available for its contract test.");
            object[] arguments = { current, incoming, null };
            bool shouldWrite = (bool)method.Invoke(null, arguments);
            conflictingRunId = arguments[2] as string;
            return shouldWrite;
        }
    }
}
