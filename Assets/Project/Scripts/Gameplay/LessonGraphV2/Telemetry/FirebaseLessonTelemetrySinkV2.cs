using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Database;
using Firebase.Firestore;
using UnityEngine;
using VRAutism.Cloud;
using VRAutism.Cloud.Models;
using VRAutism.Cloud.RTDB;
using VRAutism.Core;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Telemetry
{
    /// <summary>
    /// Firebase persistence for one V2 session. RTDB state is transactionally restricted to the
    /// dedicated lesson_graph child; Firestore uses stable event documents and a merged V2 projection.
    /// </summary>
    public sealed class FirebaseLessonTelemetrySinkV2 : ILessonTelemetrySinkV2
    {
        private readonly string _sessionId;
        private readonly LessonSessionContextV2 _lessonContext;
        private readonly string _graphId;
        private readonly Dictionary<string, object> _sessionMetadata;
        private readonly FirebaseFirestore _firestore;
        private string _ownerRunId;

        public FirebaseLessonTelemetrySinkV2(LessonSessionContextV2 lessonContext,
            SessionContext sessionContext, string graphId, IUtcClockV2 utcClock = null)
        {
            _lessonContext = lessonContext ?? throw new ArgumentNullException(nameof(lessonContext));
            _sessionId = lessonContext.SessionId;
            _graphId = graphId ?? string.Empty;
            _firestore = FirebaseFirestore.DefaultInstance;
            _sessionMetadata = CaptureSessionMetadata(sessionContext, lessonContext, _graphId,
                (utcClock ?? new SystemUtcClockV2()).UtcNow);
        }

        public async Task WriteStateAsync(LessonStateV2 state, CancellationToken token)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            ValidateSession(state.session_id);
            CaptureRunOwner(state.run_id);
            token.ThrowIfCancellationRequested();

            DatabaseReference root = RTDBConnection.Instance?.RootRef;
            if (root == null) throw new InvalidOperationException("RTDBConnection.RootRef is unavailable.");
            string[] pathSegments = BuildRtdbLessonGraphPathSegments(_sessionId);
            DatabaseReference lessonGraphRef = root;
            for (int i = 0; i < pathSegments.Length; i++)
                lessonGraphRef = lessonGraphRef.Child(pathSegments[i]);
            string conflictingRunId = null;

            await lessonGraphRef.RunTransaction(data =>
            {
                IDictionary<string, object> current = AsDictionary(data.Value);
                if (!ShouldWriteRtdbState(current, state, out string transactionConflictRunId))
                {
                    if (!string.IsNullOrEmpty(transactionConflictRunId))
                        conflictingRunId = transactionConflictRunId;
                    return TransactionResult.Abort();
                }

                data.Value = ToRtdbState(state);
                return TransactionResult.Success(data);
            });

            if (!string.IsNullOrEmpty(conflictingRunId))
                throw new InvalidOperationException($"RTDB session '{_sessionId}' is already owned by run '{conflictingRunId}'.");
            token.ThrowIfCancellationRequested();
        }

        public async Task UpsertBatchAsync(TelemetryWriteBatchV2 batch, CancellationToken token)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            ValidateBatch(batch);
            token.ThrowIfCancellationRequested();

            DocumentReference sessionDocument = _firestore.Collection(FirebasePaths.Sessions).Document(_sessionId);
            var eventWrites = new List<Task>();
            foreach (LessonAuditEventDataV2 audit in batch.audit_events_by_id.Values)
            {
                token.ThrowIfCancellationRequested();
                DocumentReference eventDocument = sessionDocument.Collection("lesson_events").Document(audit.event_id);
                eventWrites.Add(eventDocument.SetAsync(audit, SetOptions.MergeAll));
            }
            if (eventWrites.Count > 0) await Task.WhenAll(eventWrites);
            token.ThrowIfCancellationRequested();

            await _firestore.RunTransactionAsync(async transaction =>
            {
                // All transaction reads precede writes. The merge patch only includes V2-owned fields,
                // so unrelated session metadata survives each retry and concurrent legacy field update.
                DocumentSnapshot snapshot = await transaction.GetSnapshotAsync(sessionDocument);
                IDictionary<string, object> existing = snapshot.Exists
                    ? AsDictionary(snapshot.ToDictionary())
                    : new Dictionary<string, object>(StringComparer.Ordinal);
                Dictionary<string, object> patch = BuildSessionPatch(existing, _sessionMetadata, batch);
                transaction.Set(sessionDocument, patch, SetOptions.MergeAll);
            });

            token.ThrowIfCancellationRequested();
        }

        private void ValidateBatch(TelemetryWriteBatchV2 batch)
        {
            if (batch.state_projection != null)
            {
                ValidateSession(batch.state_projection.session_id);
                CaptureRunOwner(batch.state_projection.run_id);
            }
            foreach (LessonAuditEventDataV2 audit in batch.audit_events_by_id.Values)
            {
                ValidateSession(audit.session_id);
                CaptureRunOwner(audit.run_id);
                if (string.IsNullOrWhiteSpace(audit.event_id)) throw new InvalidOperationException("Audit event is missing its stable ID.");
            }
            foreach (NodeLogData log in batch.node_logs_by_id.Values)
            {
                ValidateSession(log.session_id);
                CaptureRunOwner(log.run_id);
            }
        }

        private void ValidateSession(string sessionId)
        {
            if (!string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
                throw new InvalidOperationException("A telemetry sink cannot write a different external session.");
        }

        private void CaptureRunOwner(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new InvalidOperationException("V2 telemetry is missing its run ID.");
            if (_ownerRunId == null) _ownerRunId = runId;
            else if (!string.Equals(_ownerRunId, runId, StringComparison.Ordinal))
                throw new InvalidOperationException("A V2 telemetry sink cannot own multiple lesson runs for one session.");
        }

        private static Dictionary<string, object> BuildSessionPatch(IDictionary<string, object> existing,
            IDictionary<string, object> sessionMetadata, TelemetryWriteBatchV2 batch)
        {
            var patch = new Dictionary<string, object>(sessionMetadata ?? new Dictionary<string, object>(), StringComparer.Ordinal);
            // These fields are mutable session projection values. Seed them once, then let only a
            // first terminal observation replace the running defaults; retrying old audit data must
            // never reopen a completed/cancelled session.
            patch.Remove("completion_status");
            patch.Remove("duration");
            patch.Remove("score");
            if (!HasValue(existing, "completion_status")) patch["completion_status"] = "running";
            if (!HasValue(existing, "duration")) patch["duration"] = 0d;
            if (!HasValue(existing, "score")) patch["score"] = 0;

            Dictionary<string, object> nodeMap = MergeMap(existing, "v2_node_logs_by_id", batch.node_logs_by_id,
                ToNodeLogMap);
            if (batch.node_logs_by_id.Count > 0 || HasValue(existing, "v2_node_logs_by_id"))
            {
                patch["v2_node_logs_by_id"] = nodeMap;
                patch["node_logs"] = OrderedNodeLogs(nodeMap);
            }

            Dictionary<string, object> questMap = MergeMap(existing, "v2_quest_logs_by_id", batch.quest_logs_by_id,
                ToQuestLogMap);
            if (batch.quest_logs_by_id.Count > 0 || HasValue(existing, "v2_quest_logs_by_id"))
            {
                patch["v2_quest_logs_by_id"] = questMap;
                patch["quest_logs"] = OrderedQuestLogs(questMap);
            }

            string runId = batch.state_projection?.run_id ??
                batch.audit_events_by_id.Values.Select(item => item.run_id)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(runId)) patch["v2_run_id"] = runId;

            LessonAuditEventDataV2 terminal = batch.audit_events_by_id.Values
                .Where(IsLessonTerminal)
                .OrderBy(item => item.elapsed_seconds)
                .LastOrDefault();
            if (terminal != null && !IsTerminalStatus(ReadString(existing, "completion_status")))
            {
                patch["finish_time"] = terminal.occurred_at_utc;
                patch["duration"] = terminal.elapsed_seconds;
                patch["completion_status"] = ToLegacySessionCompletionStatus(terminal.status);
            }

            return patch;
        }

        private static string ToLegacySessionCompletionStatus(string v2Status) =>
            string.Equals(v2Status, "completed", StringComparison.OrdinalIgnoreCase) ? "success" : v2Status;

        private static bool ShouldWriteRtdbState(IDictionary<string, object> current, LessonStateV2 incoming,
            out string conflictingRunId)
        {
            conflictingRunId = null;
            string storedRunId = ReadString(current, "run_id");
            if (!string.IsNullOrEmpty(storedRunId) && !string.Equals(storedRunId, incoming.run_id, StringComparison.Ordinal))
            {
                conflictingRunId = storedRunId;
                return false;
            }

            long storedRevision = ReadLong(current, "state_revision", -1);
            return storedRevision < incoming.state_revision;
        }

        private static string[] BuildRtdbLessonGraphPathSegments(string sessionId) =>
            new[] { "live_sessions", sessionId, "lesson_graph" };

        private static Dictionary<string, object> CaptureSessionMetadata(SessionContext source,
            LessonSessionContextV2 lesson, string graphId, DateTimeOffset startedAtUtc)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["session_id"] = lesson.SessionId,
                ["child_profile_id"] = source?.ChildId ?? string.Empty,
                ["hosted_by"] = source?.HostId ?? string.Empty,
                ["lesson_id"] = lesson.LessonId,
                ["lesson_name"] = source?.LessonName ?? string.Empty,
                ["level_name"] = source?.LevelName ?? string.Empty,
                ["level_index"] = source == null ? 0 : source.LevelIndex,
                ["device_id"] = SystemInfo.deviceUniqueIdentifier ?? string.Empty,
                ["type"] = source?.LessonType ?? string.Empty,
                ["start_time"] = FormatUtc(startedAtUtc),
                ["completion_status"] = "running",
                ["duration"] = 0d,
                ["score"] = 0,
                ["v2_graph_id"] = graphId,
                ["v2_launch_token"] = lesson.LaunchToken,
                ["v2_lesson_voice_revision"] = lesson.LessonVoiceRevision,
                ["v2_child_phrase_revision"] = lesson.ChildPhraseRevision
            };
        }

        private static IDictionary<string, object> ToRtdbState(LessonStateV2 state)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["contract_version"] = (long)state.contract_version,
                ["session_id"] = state.session_id ?? string.Empty,
                ["run_id"] = state.run_id ?? string.Empty,
                ["graph_id"] = state.graph_id ?? string.Empty,
                ["lesson_id"] = state.lesson_id ?? string.Empty,
                ["launch_token"] = state.launch_token ?? string.Empty,
                ["lesson_voice_revision"] = (long)state.lesson_voice_revision,
                ["child_phrase_revision"] = (long)state.child_phrase_revision,
                ["node_id"] = state.node_id ?? string.Empty,
                ["node_type"] = state.node_type ?? string.Empty,
                ["node_index"] = (long)state.node_index,
                ["activation_id"] = state.activation_id ?? string.Empty,
                ["status"] = state.status ?? string.Empty,
                ["checkpoint_id"] = state.checkpoint_id ?? string.Empty,
                ["updated_at_utc"] = state.updated_at_utc ?? string.Empty,
                ["state_revision"] = (long)state.state_revision,
                ["active_node_ids"] = state.active_node_ids == null
                    ? new List<object>()
                    : state.active_node_ids.Cast<object>().ToList(),
                ["parallel_group_id"] = state.parallel_group_id ?? string.Empty
            };
            var bindings = new List<object>();
            if (state.bindings != null)
            {
                foreach (LessonBindingV2 binding in state.bindings)
                {
                    if (binding == null) continue;
                    bindings.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["binding_id"] = binding.binding_id ?? string.Empty,
                        ["npc_binding_id"] = binding.npc_binding_id ?? string.Empty,
                        ["can_verbal_hint"] = binding.can_verbal_hint,
                        ["can_visual_hint"] = binding.can_visual_hint
                    });
                }
            }
            map["bindings"] = bindings;
            return map;
        }

        private static Dictionary<string, object> MergeMap<T>(IDictionary<string, object> existing, string key,
            IDictionary<string, T> incoming, Func<T, Dictionary<string, object>> serialize)
        {
            var merged = new Dictionary<string, object>(StringComparer.Ordinal);
            if (existing != null && existing.TryGetValue(key, out object value))
            {
                IDictionary<string, object> current = AsDictionary(value);
                if (current != null)
                    foreach (KeyValuePair<string, object> item in current) merged[item.Key] = item.Value;
            }
            foreach (KeyValuePair<string, T> item in incoming)
                merged[item.Key] = serialize(item.Value);
            return merged;
        }

        private static List<object> OrderedNodeLogs(IDictionary<string, object> nodeMap) => nodeMap
            .Select(item => new { item.Key, Data = AsDictionary(item.Value) })
            .OrderBy(item => ReadString(item.Data, "entered_at_utc"), StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => (object)item.Data)
            .ToList();

        private static List<object> OrderedQuestLogs(IDictionary<string, object> questMap) => questMap
            .Select(item => new { item.Key, Data = AsDictionary(item.Value) })
            .OrderBy(item => ReadLong(item.Data, "index", int.MaxValue))
            .ThenBy(item => ReadString(item.Data, "quest_name"), StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => (object)item.Data)
            .ToList();

        private static Dictionary<string, object> ToNodeLogMap(NodeLogData log) => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["event_id"] = log.event_id ?? string.Empty,
            ["session_id"] = log.session_id ?? string.Empty,
            ["run_id"] = log.run_id ?? string.Empty,
            ["graph_id"] = log.graph_id ?? string.Empty,
            ["lesson_id"] = log.lesson_id ?? string.Empty,
            ["launch_token"] = log.launch_token ?? string.Empty,
            ["lesson_voice_revision"] = log.lesson_voice_revision,
            ["child_phrase_revision"] = log.child_phrase_revision,
            ["node_id"] = log.node_id ?? string.Empty,
            ["node_type"] = log.node_type ?? string.Empty,
            ["node_name"] = log.node_name ?? string.Empty,
            ["node_index"] = log.node_index,
            ["activation_id"] = log.activation_id ?? string.Empty,
            ["entered_at_utc"] = log.entered_at_utc ?? string.Empty,
            ["exited_at_utc"] = log.exited_at_utc ?? string.Empty,
            ["duration_seconds"] = log.duration_seconds,
            ["elapsed_seconds"] = log.elapsed_seconds,
            ["status"] = log.status ?? string.Empty,
            ["completion_channel"] = log.completion_channel ?? string.Empty
        };

        private static Dictionary<string, object> ToQuestLogMap(QuestLogData log) => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["index"] = log.index,
            ["quest_name"] = log.quest_name ?? string.Empty,
            ["response_time"] = log.response_time,
            ["completion_status"] = log.completion_status ?? string.Empty,
            ["hints_verbal"] = log.hints_verbal,
            ["hints_visual"] = log.hints_visual,
            ["hints_physical"] = log.hints_physical,
            ["response_time_from_hint"] = log.response_time_from_hint
        };

        private static bool IsLessonTerminal(LessonAuditEventDataV2 item) =>
            item.event_type == LessonTelemetryEventTypeV2.LessonCompleted ||
            item.event_type == LessonTelemetryEventTypeV2.LessonFailed ||
            item.event_type == LessonTelemetryEventTypeV2.LessonCancelled;

        private static bool IsTerminalStatus(string status) =>
            string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);

        private static bool HasValue(IDictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out object value) && value != null;

        private static IDictionary<string, object> AsDictionary(object value) => value as IDictionary<string, object>;

        private static string ReadString(IDictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out object value) ? value as string ?? value?.ToString() ?? string.Empty : string.Empty;

        private static long ReadLong(IDictionary<string, object> map, string key, long fallback) =>
            map != null && map.TryGetValue(key, out object value) && value != null
                ? Convert.ToInt64(value)
                : fallback;

        private static string FormatUtc(DateTimeOffset value) => value.ToUniversalTime().ToString("O");
    }
}
