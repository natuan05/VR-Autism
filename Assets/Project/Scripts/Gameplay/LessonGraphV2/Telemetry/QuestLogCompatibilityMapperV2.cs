using System;
using VRAutism.Cloud.Models;

namespace VRAutism.Gameplay.LessonGraphV2.Telemetry
{
    /// <summary>
    /// Maps one logical V2 quest to the legacy inline quest-log shape.
    /// V2 telemetry is the sole owner of this compatibility projection.
    /// </summary>
    public static class QuestLogCompatibilityMapperV2
    {
        public static QuestLogData Map(NodeLogData terminal, QuestHintSummaryV2 hints)
        {
            if (terminal == null) throw new ArgumentNullException(nameof(terminal));
            if (hints == null) throw new ArgumentNullException(nameof(hints));
            string status = (terminal.status ?? string.Empty).Trim().ToLowerInvariant();
            bool success = status == "success";
            int acceptedHints = Math.Max(0, hints.AcceptedVerbalHints) + Math.Max(0, hints.AcceptedVisualHints);
            string completionStatus = status == "skipped"
                ? "skipped"
                : success ? (acceptedHints > 0 ? "assisted" : "success") : "failed";
            double responseTimeFromHint = hints.AcceptedVisualHints > 0 &&
                                          hints.LastVisualHintElapsedSeconds >= 0d &&
                                          hints.TerminalElapsedSeconds >= hints.LastVisualHintElapsedSeconds
                ? hints.TerminalElapsedSeconds - hints.LastVisualHintElapsedSeconds
                : -1d;
            return new QuestLogData
            {
                index = terminal.node_index,
                quest_name = string.IsNullOrWhiteSpace(terminal.node_name) ? terminal.node_id : terminal.node_name,
                response_time = Math.Max(0d, hints.TotalDurationSeconds),
                completion_status = completionStatus,
                hints_verbal = Math.Max(0, hints.AcceptedVerbalHints),
                hints_visual = Math.Max(0, hints.AcceptedVisualHints),
                hints_physical = 0,
                response_time_from_hint = responseTimeFromHint
            };
        }
    }
}
