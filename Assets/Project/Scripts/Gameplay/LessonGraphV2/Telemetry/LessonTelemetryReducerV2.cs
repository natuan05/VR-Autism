using System;
using System.Collections.Generic;
using System.Globalization;
using VRAutism.Cloud.Models;
using VRAutism.Gameplay.LessonGraphV2.Remote;

namespace VRAutism.Gameplay.LessonGraphV2.Telemetry
{
    /// <summary>
    /// Pure in-memory projection of immutable runner observations. The writer owns retries and persistence.
    /// </summary>
    public sealed class LessonTelemetryReducerV2
    {
        private readonly HashSet<string> _observedEventIds = new HashSet<string>();
        private readonly Dictionary<string, OpenNode> _openNodes = new Dictionary<string, OpenNode>();
        private readonly Dictionary<string, QuestAggregate> _quests = new Dictionary<string, QuestAggregate>();
        private readonly HashSet<string> _finalizedQuestIds = new HashSet<string>();

        public TelemetryWriteBatchV2 Observe(LessonLifecycleEventV2 item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var batch = new TelemetryWriteBatchV2();
            string auditId = GetAuditId(item);
            if (!_observedEventIds.Add(auditId)) return batch;

            batch.audit_events_by_id[auditId] = CreateAudit(item, auditId);
            if (item.IsStateTransition && item.StateSnapshot != null)
                batch.state_projection = LessonLifecycleEventV2.CloneState(item.StateSnapshot);

            if (item.EventType == LessonTelemetryEventTypeV2.NodeEntered && item.Node != null)
            {
                ObserveNodeEntered(item);
            }
            else if ((item.EventType == LessonTelemetryEventTypeV2.NodeCompleted ||
                      item.EventType == LessonTelemetryEventTypeV2.NodeCancelled) && item.Node != null)
            {
                NodeLogData log = CloseNode(item.Context, item.Node, item.Status, item.CompletionChannel,
                    item.Reason, item.OccurredAtUtc, item.ElapsedSeconds);
                if (log != null)
                {
                    batch.node_logs_by_id[log.event_id] = log;
                    AddClosedAttempt(item.Context, item.Node, log);
                    if (item.EventType == LessonTelemetryEventTypeV2.NodeCompleted)
                        FinalizeQuest(item.Context, item.Node, log, item.Status, item.ElapsedSeconds, batch);
                }
            }
            else if (IsLessonTerminal(item.EventType))
            {
                CloseRemainingNodes(item, batch);
                FinalizeRemainingQuests(item, batch);
            }
            else if (IsAcceptedHint(item))
            {
                AddAcceptedHint(item);
            }

            return batch;
        }

        private void ObserveNodeEntered(LessonLifecycleEventV2 item)
        {
            string activationKey = ActivationKey(item.Context, item.Node);
            if (_openNodes.ContainsKey(activationKey)) return;
            _openNodes.Add(activationKey, new OpenNode(item.Context, item.Node, item.OccurredAtUtc, item.ElapsedSeconds));

            if (!IsQuestNode(item.Node)) return;
            string questKey = QuestKey(item.Context, item.Node);
            if (!_quests.ContainsKey(questKey))
                _quests.Add(questKey, new QuestAggregate(item.Context, item.Node));
        }

        private NodeLogData CloseNode(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node,
            string status, string completionChannel, string reason, DateTimeOffset exitedAtUtc, double elapsedSeconds)
        {
            string activationKey = ActivationKey(context, node);
            if (!_openNodes.TryGetValue(activationKey, out OpenNode open) ||
                !string.Equals(open.Node.NodeId, node.NodeId, StringComparison.Ordinal)) return null;
            _openNodes.Remove(activationKey);
            double duration = Math.Max(0d, elapsedSeconds - open.ElapsedSeconds);
            return new NodeLogData
            {
                event_id = LessonTelemetryIdentityV2.NodeLog(
                    open.Context.SessionId, open.Context.RunId, open.Node.ActivationId),
                session_id = open.Context.SessionId,
                run_id = open.Context.RunId,
                graph_id = open.Context.GraphId,
                lesson_id = open.Context.LessonId,
                launch_token = open.Context.LaunchToken,
                lesson_voice_revision = open.Context.LessonVoiceRevision,
                child_phrase_revision = open.Context.ChildPhraseRevision,
                node_id = open.Node.NodeId,
                node_type = open.Node.NodeType,
                node_name = open.Node.NodeName,
                node_index = open.Node.NodeIndex,
                activation_id = open.Node.ActivationId,
                entered_at_utc = FormatUtc(open.OccurredAtUtc),
                exited_at_utc = FormatUtc(exitedAtUtc),
                duration_seconds = duration,
                elapsed_seconds = elapsedSeconds,
                status = NormalizeStatus(status, reason),
                completion_channel = completionChannel ?? string.Empty
            };
        }

        private void AddClosedAttempt(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node, NodeLogData log)
        {
            if (!IsQuestNode(node)) return;
            string key = QuestKey(context, node);
            if (!_quests.TryGetValue(key, out QuestAggregate aggregate))
            {
                aggregate = new QuestAggregate(context, node);
                _quests.Add(key, aggregate);
            }
            if (aggregate.ClosedActivations.Add(node.ActivationId))
                aggregate.TotalDurationSeconds += log.duration_seconds;
            aggregate.TerminalLog = log;
            aggregate.TerminalElapsedSeconds = log.elapsed_seconds;
        }

        private void AddAcceptedHint(LessonLifecycleEventV2 item)
        {
            if (!IsQuestNode(item.Node) || string.IsNullOrWhiteSpace(item.CommandId)) return;
            string key = QuestKey(item.Context, item.Node);
            if (!_quests.TryGetValue(key, out QuestAggregate aggregate))
            {
                aggregate = new QuestAggregate(item.Context, item.Node);
                _quests.Add(key, aggregate);
            }
            if (!aggregate.AcceptedHintIds.Add(item.CommandId)) return;
            if (item.Command == "VERBAL_HINT")
                aggregate.AcceptedVerbalHints++;
            else if (item.Command == "VISUAL_HINT")
            {
                aggregate.AcceptedVisualHints++;
                aggregate.LastVisualHintElapsedSeconds = item.ElapsedSeconds;
            }
        }

        private void CloseRemainingNodes(LessonLifecycleEventV2 terminal, TelemetryWriteBatchV2 batch)
        {
            var open = new List<OpenNode>(_openNodes.Values);
            for (int i = 0; i < open.Count; i++)
            {
                OpenNode node = open[i];
                if (!SameSessionAndRun(node.Context, terminal.Context)) continue;
                var log = CloseNode(node.Context, node.Node, terminal.Status, string.Empty, terminal.Reason,
                    terminal.OccurredAtUtc, terminal.ElapsedSeconds);
                if (log == null) continue;
                batch.node_logs_by_id[log.event_id] = log;
                AddClosedAttempt(node.Context, node.Node, log);
            }
        }

        private void FinalizeRemainingQuests(LessonLifecycleEventV2 terminal, TelemetryWriteBatchV2 batch)
        {
            var entries = new List<KeyValuePair<string, QuestAggregate>>(_quests);
            for (int i = 0; i < entries.Count; i++)
            {
                QuestAggregate aggregate = entries[i].Value;
                if (!SameSessionAndRun(aggregate.Context, terminal.Context) || aggregate.TerminalLog == null) continue;
                FinalizeQuest(aggregate.Context, aggregate.Node, aggregate.TerminalLog,
                    terminal.Status, terminal.ElapsedSeconds, batch);
            }
        }

        private void FinalizeQuest(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node, NodeLogData terminalLog,
            string finalStatus, double terminalElapsedSeconds, TelemetryWriteBatchV2 batch)
        {
            if (!IsQuestNode(node)) return;
            string questId = LessonTelemetryIdentityV2.QuestLog(context.SessionId, context.RunId, node.NodeId);
            if (!_finalizedQuestIds.Add(questId)) return;
            string key = QuestKey(context, node);
            if (!_quests.TryGetValue(key, out QuestAggregate aggregate)) return;
            NodeLogData projectionLog = terminalLog;
            if (!string.IsNullOrWhiteSpace(finalStatus) && finalStatus != terminalLog.status)
                projectionLog = CopyWithStatus(terminalLog, NormalizeStatus(finalStatus, string.Empty));
            var hints = new QuestHintSummaryV2
            {
                AcceptedVerbalHints = aggregate.AcceptedVerbalHints,
                AcceptedVisualHints = aggregate.AcceptedVisualHints,
                TotalDurationSeconds = aggregate.TotalDurationSeconds,
                LastVisualHintElapsedSeconds = aggregate.LastVisualHintElapsedSeconds,
                TerminalElapsedSeconds = terminalElapsedSeconds
            };
            batch.quest_logs_by_id[questId] = QuestLogCompatibilityMapperV2.Map(projectionLog, hints);
        }

        private static NodeLogData CopyWithStatus(NodeLogData source, string status) =>
            new NodeLogData
            {
                event_id = source.event_id,
                session_id = source.session_id,
                run_id = source.run_id,
                graph_id = source.graph_id,
                lesson_id = source.lesson_id,
                launch_token = source.launch_token,
                lesson_voice_revision = source.lesson_voice_revision,
                child_phrase_revision = source.child_phrase_revision,
                node_id = source.node_id,
                node_type = source.node_type,
                node_name = source.node_name,
                node_index = source.node_index,
                activation_id = source.activation_id,
                entered_at_utc = source.entered_at_utc,
                exited_at_utc = source.exited_at_utc,
                duration_seconds = source.duration_seconds,
                elapsed_seconds = source.elapsed_seconds,
                status = status,
                completion_channel = source.completion_channel
            };

        private static LessonAuditEventDataV2 CreateAudit(LessonLifecycleEventV2 item, string eventId)
        {
            LessonTelemetryNodeV2 node = item.Node;
            return new LessonAuditEventDataV2
            {
                event_id = eventId,
                event_type = item.EventType,
                session_id = item.Context.SessionId,
                run_id = item.Context.RunId,
                graph_id = item.Context.GraphId,
                lesson_id = item.Context.LessonId,
                node_id = node == null ? string.Empty : node.NodeId,
                node_type = node == null ? string.Empty : node.NodeType,
                node_index = node == null ? -1 : node.NodeIndex,
                activation_id = node == null ? string.Empty : node.ActivationId,
                occurred_at_utc = FormatUtc(item.OccurredAtUtc),
                elapsed_seconds = item.ElapsedSeconds,
                status = item.Status,
                command_id = item.CommandId,
                command = item.Command,
                binding_id = item.BindingId,
                reason = item.Reason,
                launch_token = item.Context.LaunchToken,
                lesson_voice_revision = item.Context.LessonVoiceRevision,
                child_phrase_revision = item.Context.ChildPhraseRevision
            };
        }

        private static string GetAuditId(LessonLifecycleEventV2 item)
        {
            if (!string.IsNullOrWhiteSpace(item.CommandId))
            {
                string disposition = string.Equals(item.Reason, "DUPLICATE", StringComparison.OrdinalIgnoreCase)
                    ? "duplicate"
                    : item.EventType == LessonTelemetryEventTypeV2.CommandAccepted ? "accepted" : "rejected";
                return LessonTelemetryIdentityV2.Command(
                    item.Context.SessionId, item.Context.RunId, item.CommandId, disposition);
            }
            return LessonTelemetryIdentityV2.Lifecycle(item.Context.SessionId, item.Context.RunId,
                item.Node == null ? string.Empty : item.Node.ActivationId, item.EventType);
        }

        private static bool IsAcceptedHint(LessonLifecycleEventV2 item) =>
            item.EventType == LessonTelemetryEventTypeV2.CommandAccepted &&
            (item.Command == "VERBAL_HINT" || item.Command == "VISUAL_HINT");

        private static bool IsLessonTerminal(string eventType) =>
            eventType == LessonTelemetryEventTypeV2.LessonCompleted ||
            eventType == LessonTelemetryEventTypeV2.LessonFailed ||
            eventType == LessonTelemetryEventTypeV2.LessonCancelled;

        private static string NormalizeStatus(string status, string reason)
        {
            string normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "cancelled")
            {
                if (string.Equals(reason, "timeout", StringComparison.OrdinalIgnoreCase)) return "timeout";
                if (string.Equals(reason, "skip", StringComparison.OrdinalIgnoreCase)) return "skipped";
            }

            if (normalized == "success" || normalized == "skipped" || normalized == "timeout" ||
                normalized == "failed" || normalized == "cancelled")
                return normalized;
            if (string.Equals(reason, "timeout", StringComparison.OrdinalIgnoreCase)) return "timeout";
            if (string.Equals(reason, "skip", StringComparison.OrdinalIgnoreCase)) return "skipped";
            return "failed";
        }

        private static string FormatUtc(DateTimeOffset value) =>
            value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        private static string QuestKey(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node) =>
            LessonTelemetryIdentityV2.CompositeKey(context.SessionId, context.RunId, node.NodeId);

        private static string ActivationKey(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node) =>
            LessonTelemetryIdentityV2.CompositeKey(context.SessionId, context.RunId, node.ActivationId);

        private static bool SameSessionAndRun(LessonTelemetryContextV2 left, LessonTelemetryContextV2 right) =>
            string.Equals(left.SessionId, right.SessionId, StringComparison.Ordinal) &&
            string.Equals(left.RunId, right.RunId, StringComparison.Ordinal);

        private static bool IsQuestNode(LessonTelemetryNodeV2 node) =>
            node != null && string.Equals(node.NodeType, "Quest", StringComparison.OrdinalIgnoreCase);

        private sealed class OpenNode
        {
            public readonly LessonTelemetryContextV2 Context;
            public readonly LessonTelemetryNodeV2 Node;
            public readonly DateTimeOffset OccurredAtUtc;
            public readonly double ElapsedSeconds;

            public OpenNode(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node,
                DateTimeOffset occurredAtUtc, double elapsedSeconds)
            {
                Context = context;
                Node = node;
                OccurredAtUtc = occurredAtUtc;
                ElapsedSeconds = elapsedSeconds;
            }
        }

        private sealed class QuestAggregate
        {
            public readonly LessonTelemetryContextV2 Context;
            public readonly LessonTelemetryNodeV2 Node;
            public readonly HashSet<string> ClosedActivations = new HashSet<string>();
            public readonly HashSet<string> AcceptedHintIds = new HashSet<string>();
            public int AcceptedVerbalHints;
            public int AcceptedVisualHints;
            public double TotalDurationSeconds;
            public double LastVisualHintElapsedSeconds = -1d;
            public double TerminalElapsedSeconds;
            public NodeLogData TerminalLog;

            public QuestAggregate(LessonTelemetryContextV2 context, LessonTelemetryNodeV2 node)
            {
                Context = context;
                Node = node;
            }
        }
    }
}
