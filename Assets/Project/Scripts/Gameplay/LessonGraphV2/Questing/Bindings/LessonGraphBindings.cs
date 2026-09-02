using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Questing
{
    [Serializable]
    public sealed class QuestBindingEntry
    {
        [SerializeField] private string _bindingId = string.Empty;
        [SerializeField] private QuestSourceV2 _source;

        public string BindingId => _bindingId ?? string.Empty;
        public QuestSourceV2 Source => _source;

        public QuestBindingEntry(string bindingId, QuestSourceV2 source)
        {
            _bindingId = bindingId;
            _source = source;
        }
    }

    [DisallowMultipleComponent]
    public sealed class LessonGraphBindings : MonoBehaviour, IQuestBindingResolver, ILessonStartPreflight
    {
        [SerializeField] private List<QuestBindingEntry> _entries = new List<QuestBindingEntry>();

        private readonly Dictionary<string, QuestSourceV2> _sources =
            new Dictionary<string, QuestSourceV2>(StringComparer.Ordinal);
        private QuestBindingValidationIssue[] _validationIssues = Array.Empty<QuestBindingValidationIssue>();
        private QuestBindingValidationIssue[] _lastPreflightIssues = Array.Empty<QuestBindingValidationIssue>();

        public IReadOnlyList<QuestBindingValidationIssue> ValidationIssues =>
            Array.AsReadOnly(_validationIssues);
        public IReadOnlyList<QuestBindingValidationIssue> LastPreflightIssues =>
            Array.AsReadOnly(_lastPreflightIssues);

        private void Awake()
        {
            BuildRegistry();
        }

        public QuestBindingResolution Resolve(string bindingId)
        {
            var normalizedId = bindingId ?? string.Empty;
            if (!_sources.TryGetValue(normalizedId, out var source))
            {
                return QuestBindingResolution.Failure(new QuestBindingValidationIssue(
                    QuestBindingFailureCodes.MissingBinding,
                    normalizedId,
                    $"No scene quest source is registered for binding '{normalizedId}'."));
            }

            if (source == null || !source.IsAvailable)
            {
                return QuestBindingResolution.Failure(new QuestBindingValidationIssue(
                    QuestBindingFailureCodes.BindingUnavailable,
                    normalizedId,
                    $"Quest source for binding '{normalizedId}' is disabled, destroyed, active, or terminal."));
            }

            return QuestBindingResolution.Success(source);
        }

        public bool IsReady(LessonGraph graph, out string reason)
        {
            var issues = new List<QuestBindingValidationIssue>(_validationIssues);
            if (graph == null)
            {
                issues.Add(new QuestBindingValidationIssue(
                    QuestBindingFailureCodes.InvalidGraph,
                    string.Empty,
                    "Lesson graph is null."));
            }
            else
            {
                foreach (var node in graph.Nodes ?? Array.Empty<LessonNodeData>())
                {
                    if (!(node?.Config is QuestNodeConfig questConfig)) continue;
                    foreach (var bindingId in questConfig.CompletionBindingIds ?? Array.Empty<string>())
                    {
                        var resolution = Resolve(bindingId);
                        if (!resolution.IsSuccess) issues.Add(resolution.Issue);
                    }
                }
            }

            _lastPreflightIssues = Deduplicate(issues).ToArray();
            if (_lastPreflightIssues.Length == 0)
            {
                reason = null;
                return true;
            }

            reason = string.Join("; ", _lastPreflightIssues.Select(issue => issue.ToString()));
            return false;
        }

        private void BuildRegistry()
        {
            _sources.Clear();
            var issues = new List<QuestBindingValidationIssue>();
            var entries = _entries ?? new List<QuestBindingEntry>();
            var counts = entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.BindingId))
                .GroupBy(entry => entry.BindingId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var duplicateIssuesAdded = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    issues.Add(new QuestBindingValidationIssue(
                        QuestBindingFailureCodes.NullEntry,
                        string.Empty,
                        "Scene quest binding entry is null."));
                    continue;
                }

                var bindingId = entry.BindingId;
                if (string.IsNullOrWhiteSpace(bindingId))
                {
                    issues.Add(new QuestBindingValidationIssue(
                        QuestBindingFailureCodes.BlankBindingId,
                        bindingId,
                        "Scene quest binding ID must not be blank."));
                    continue;
                }

                if (counts[bindingId] > 1)
                {
                    if (duplicateIssuesAdded.Add(bindingId))
                    {
                        issues.Add(new QuestBindingValidationIssue(
                            QuestBindingFailureCodes.DuplicateBindingId,
                            bindingId,
                            $"Scene quest binding ID '{bindingId}' is duplicated."));
                    }
                    continue;
                }

                var source = entry.Source;
                if (source == null)
                {
                    issues.Add(new QuestBindingValidationIssue(
                        QuestBindingFailureCodes.NullSource,
                        bindingId,
                        $"Scene quest binding '{bindingId}' has no source."));
                    continue;
                }

                if (!string.Equals(source.BindingId, bindingId, StringComparison.Ordinal))
                {
                    issues.Add(new QuestBindingValidationIssue(
                        QuestBindingFailureCodes.BindingIdMismatch,
                        bindingId,
                        $"Scene entry '{bindingId}' references source '{source.BindingId}'."));
                    continue;
                }

                _sources.Add(bindingId, source);
                if (!source.IsAvailable)
                {
                    issues.Add(new QuestBindingValidationIssue(
                        QuestBindingFailureCodes.BindingUnavailable,
                        bindingId,
                        $"Quest source for binding '{bindingId}' is unavailable during registry construction."));
                }
            }

            _validationIssues = issues.ToArray();
            _lastPreflightIssues = Array.Empty<QuestBindingValidationIssue>();
        }

        private static IEnumerable<QuestBindingValidationIssue> Deduplicate(
            IEnumerable<QuestBindingValidationIssue> issues)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var issue in issues)
            {
                var key = $"{issue.Code}\n{issue.BindingId}";
                if (seen.Add(key)) yield return issue;
            }
        }
    }
}