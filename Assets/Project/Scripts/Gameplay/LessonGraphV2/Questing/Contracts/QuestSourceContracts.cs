using System;

namespace VRAutism.Gameplay.LessonGraphV2.Questing
{
    public enum QuestSourceState
    {
        Inactive,
        Activating,
        Active,
        Completing,
        Completed,
        Cancelled,
        Failed,
    }

    public enum QuestSourceTerminalStatus
    {
        Completed,
        Cancelled,
        Failed,
    }

    public static class QuestSourceFailureCodes
    {
        public const string BindingUnavailable = "binding_unavailable";
        public const string ActivationFailed = "activation_failed";
    }

    public static class QuestBindingFailureCodes
    {
        public const string NullEntry = "null_entry";
        public const string NullSource = "null_source";
        public const string BlankBindingId = "blank_binding_id";
        public const string DuplicateBindingId = "duplicate_binding_id";
        public const string BindingIdMismatch = "binding_id_mismatch";
        public const string MissingBinding = "missing_binding";
        public const string BindingUnavailable = "binding_unavailable";
        public const string InvalidGraph = "invalid_graph";
    }

    public sealed class QuestSourceActivation
    {
        public string ActivationId { get; }
        public DateTimeOffset ActivatedAtUtc { get; }
        public double ActivatedAtMonotonicSeconds { get; }

        public QuestSourceActivation(
            string activationId,
            DateTimeOffset activatedAtUtc,
            double activatedAtMonotonicSeconds)
        {
            if (string.IsNullOrWhiteSpace(activationId))
                throw new ArgumentException("Activation ID must not be blank.", nameof(activationId));
            if (double.IsNaN(activatedAtMonotonicSeconds) || double.IsInfinity(activatedAtMonotonicSeconds))
                throw new ArgumentOutOfRangeException(nameof(activatedAtMonotonicSeconds));

            ActivationId = activationId;
            ActivatedAtUtc = activatedAtUtc;
            ActivatedAtMonotonicSeconds = activatedAtMonotonicSeconds;
        }
    }

    public sealed class QuestSourceCancellation
    {
        public string ActivationId { get; }
        public string Reason { get; }

        public QuestSourceCancellation(string activationId, string reason)
        {
            if (string.IsNullOrWhiteSpace(activationId))
                throw new ArgumentException("Activation ID must not be blank.", nameof(activationId));

            ActivationId = activationId;
            Reason = string.IsNullOrWhiteSpace(reason) ? "cancelled" : reason;
        }
    }

    public sealed class QuestSourceResult
    {
        public string ActivationId { get; }
        public string BindingId { get; }
        public string CompletionChannel { get; }
        public QuestSourceTerminalStatus Status { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public double CompletedAtMonotonicSeconds { get; }
        public string FailureCode { get; }
        public string CancellationReason { get; }

        public QuestSourceResult(
            string activationId,
            string bindingId,
            string completionChannel,
            QuestSourceTerminalStatus status,
            DateTimeOffset completedAtUtc,
            double completedAtMonotonicSeconds,
            string failureCode = null,
            string cancellationReason = null)
        {
            ActivationId = activationId ?? string.Empty;
            BindingId = bindingId ?? string.Empty;
            CompletionChannel = completionChannel ?? string.Empty;
            Status = status;
            CompletedAtUtc = completedAtUtc;
            CompletedAtMonotonicSeconds = completedAtMonotonicSeconds;
            FailureCode = failureCode ?? string.Empty;
            CancellationReason = cancellationReason ?? string.Empty;
        }
    }

    public sealed class QuestBindingValidationIssue
    {
        public string Code { get; }
        public string BindingId { get; }
        public string Message { get; }

        public QuestBindingValidationIssue(string code, string bindingId, string message)
        {
            Code = code ?? string.Empty;
            BindingId = bindingId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public override string ToString() => $"{Code}:{BindingId}";
    }

    public sealed class QuestBindingResolution
    {
        public bool IsSuccess { get; }
        public QuestSourceV2 Source { get; }
        public QuestBindingValidationIssue Issue { get; }

        private QuestBindingResolution(
            bool isSuccess,
            QuestSourceV2 source,
            QuestBindingValidationIssue issue)
        {
            IsSuccess = isSuccess;
            Source = source;
            Issue = issue;
        }

        public static QuestBindingResolution Success(QuestSourceV2 source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new QuestBindingResolution(true, source, null);
        }

        public static QuestBindingResolution Failure(QuestBindingValidationIssue issue)
        {
            if (issue == null) throw new ArgumentNullException(nameof(issue));
            return new QuestBindingResolution(false, null, issue);
        }
    }

    public interface IQuestSource
    {
        string BindingId { get; }
        QuestSourceState State { get; }
        string CurrentActivationId { get; }
        event Action<QuestSourceState> StateChanged;
        event Action<QuestSourceResult> Terminated;
        bool TryActivate(QuestSourceActivation activation);
        bool TryCancel(QuestSourceCancellation cancellation);
    }

    public interface IQuestBindingResolver
    {
        QuestBindingResolution Resolve(string bindingId);
    }
}