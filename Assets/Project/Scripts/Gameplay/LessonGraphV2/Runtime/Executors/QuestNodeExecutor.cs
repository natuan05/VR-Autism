using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Questing.Sources;
using VRAutism.Gameplay.LessonGraphV2.Remote;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime.Executors
{
    /// <summary>Activates scene-owned quest sources and settles a quest node exactly once.</summary>
    public sealed class QuestNodeExecutor : INodeExecutor
    {
        private readonly IQuestBindingResolver _resolver;
        private readonly INodeClock _clock;
        private readonly object _hintGate = new object();
        private readonly Dictionary<string, List<QuestSourceV2>> _pausedSources = new Dictionary<string, List<QuestSourceV2>>(StringComparer.Ordinal);
        private ActiveExecution _activeExecution;
        private string _pauseRequestedActivationId;

        public QuestNodeExecutor(IQuestBindingResolver resolver, INodeClock clock = null)
        { _resolver = resolver; _clock = clock; }

        public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            if (context == null || context.Node == null || !(context.Node.Config is QuestNodeConfig config) || _resolver == null)
            {
                UnityEngine.Debug.LogWarning($"[LessonGraphV2] QuestExecutor: invalid config for node={context?.Node?.Id}");
                return Result(context, NodeStatus.Failed, "invalid_quest");
            }

            var sources = new List<QuestSourceV2>();
            var bindingIds = config.CompletionBindingIds;
            UnityEngine.Debug.Log($"[LessonGraphV2] QuestExecutor: START node={context.Node.Id} bindings={(bindingIds != null ? bindingIds.Count : 0)}");
            
            if (bindingIds == null || bindingIds.Count == 0)
            {
                UnityEngine.Debug.LogWarning($"[LessonGraphV2] QuestExecutor: no bindings for node={context?.Node?.Id}");
                return Result(context, NodeStatus.Failed, QuestBindingFailureCodes.InvalidGraph);
            }

            var uniqueBindings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bindingId in bindingIds)
            {
                if (string.IsNullOrWhiteSpace(bindingId) || !uniqueBindings.Add(bindingId))
                {
                    UnityEngine.Debug.LogWarning($"[LessonGraphV2] QuestExecutor: invalid binding '{bindingId}' for node={context.Node.Id}");
                    return Result(context, NodeStatus.Failed, QuestBindingFailureCodes.InvalidGraph);
                }

                QuestBindingResolution resolution;
                try { resolution = _resolver.Resolve(bindingId); }
                catch { return Result(context, NodeStatus.Failed, QuestBindingFailureCodes.BindingUnavailable); }
                if (resolution == null || !resolution.IsSuccess || resolution.Source == null)
                {
                    UnityEngine.Debug.LogWarning($"[LessonGraphV2] QuestExecutor: resolve failed binding='{bindingId}' code={resolution?.Issue?.Code}");
                    return Result(context, NodeStatus.Failed, resolution?.Issue?.Code ?? QuestBindingFailureCodes.BindingUnavailable);
                }
                if (sources.Contains(resolution.Source))
                    return Result(context, NodeStatus.Failed, QuestBindingFailureCodes.InvalidGraph);
                sources.Add(resolution.Source);
            }

            var completion = new TaskCompletionSource<NodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var settled = 0;
            var active = new List<QuestSourceV2>();
            var hintScope = new ActiveExecution(context, sources);
            lock (_hintGate) _activeExecution = hintScope;
            Action<QuestSourceResult> handler = result =>
            {
                if (result == null || result.ActivationId != context.ActivationId) return;
                var status = result.Status == QuestSourceTerminalStatus.Completed ? NodeStatus.Success :
                    result.Status == QuestSourceTerminalStatus.Cancelled ? NodeStatus.Skipped : NodeStatus.Failed;
                if (Interlocked.CompareExchange(ref settled, 1, 0) == 0)
                {
                    UnityEngine.Debug.Log($"[LessonGraphV2] QuestExecutor: FIRST-WIN binding='{result.BindingId}' status={result.Status} channel={result.CompletionChannel}");
                    completion.TrySetResult(Result(context, status, result.CompletionChannel));
                }
            };

            foreach (var source in sources) source.Terminated += handler;
            try
            {
                foreach (var source in sources)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref settled) != 0) break;
                    if (!source.TryActivate(new QuestSourceActivation(context.ActivationId, DateTimeOffset.UtcNow, Elapsed(context))))
                    {
                        UnityEngine.Debug.LogWarning($"[LessonGraphV2] QuestExecutor: activation rejected source='{source.BindingId}'");
                        if (Interlocked.CompareExchange(ref settled, 1, 0) == 0)
                            completion.TrySetResult(Result(context, NodeStatus.Failed, QuestSourceFailureCodes.ActivationFailed));
                        break;
                    }
                    UnityEngine.Debug.Log($"[LessonGraphV2] QuestExecutor: activated source binding='{source.BindingId}' activation={context.ActivationId}");
                    active.Add(source);
                }

                var clock = context.Clock ?? _clock;
                var timeoutTask = config.TimeoutSeconds == -1f || clock == null
                    ? Never() : clock.Delay(config.TimeoutSeconds, context.CancellationToken);
                using (var abort = new CancellationSignal(context.CancellationToken))
                using (var skip = new CancellationSignal(context.SkipToken))
                using (var timeout = new CancellationSignal(context.TimeoutToken))
                {
                    var winner = await Task.WhenAny(completion.Task, abort.Task, skip.Task, timeout.Task, timeoutTask);
                    context.CancellationToken.ThrowIfCancellationRequested();
                    if (winner == skip.Task)
                    {
                        UnityEngine.Debug.Log($"[LessonGraphV2] QuestExecutor: skip won race node={context.Node.Id}");
                        Claim(completion, ref settled, Result(context, NodeStatus.Skipped, "skip"));
                    }
                    else if (winner == timeout.Task || winner == timeoutTask)
                    {
                        UnityEngine.Debug.Log($"[LessonGraphV2] QuestExecutor: timeout won race node={context.Node.Id}");
                        Claim(completion, ref settled, Result(context, NodeStatus.Timeout, "timeout"));
                    }
                    return await completion.Task;
                }
            }
            finally
            {
                Interlocked.Exchange(ref settled, 1);
                bool rearmAfterPause;
                bool ownsActiveExecution;
                lock (_hintGate)
                {
                    ownsActiveExecution = ReferenceEquals(_activeExecution, hintScope);
                    rearmAfterPause = string.Equals(_pauseRequestedActivationId, context.ActivationId, StringComparison.Ordinal);
                    if (rearmAfterPause) _pauseRequestedActivationId = null;
                }
                foreach (var source in sources) source.Terminated -= handler;
                var rearmableSources = new List<QuestSourceV2>();
                foreach (var source in active)
                {
                    UnityEngine.Debug.Log($"[LessonGraphV2] QuestExecutor: cancelling loser source='{source.BindingId}'");
                    var reason = rearmAfterPause ? QuestSourceV2.PauseCancellationReason : "first_win";
                    if (source.TryCancel(new QuestSourceCancellation(context.ActivationId, reason)) && rearmAfterPause)
                        rearmableSources.Add(source);
                }
                if (rearmableSources.Count > 0)
                {
                    lock (_hintGate) _pausedSources[context.ActivationId] = rearmableSources;
                }
                if (ownsActiveExecution)
                {
                    lock (_hintGate)
                    {
                        if (ReferenceEquals(_activeExecution, hintScope)) _activeExecution = null;
                    }
                }
            }
        }

        public void NotifyPauseRequested(string activationId)
        {
            lock (_hintGate)
            {
                if (_activeExecution != null &&
                    string.Equals(_activeExecution.Context.ActivationId, activationId, StringComparison.Ordinal))
                    _pauseRequestedActivationId = activationId;
            }
        }

        public bool HasTerminalSourceDecision(string activationId)
        {
            lock (_hintGate)
            {
                var scope = _activeExecution;
                if (scope == null || !string.Equals(scope.Context.ActivationId, activationId, StringComparison.Ordinal))
                    return false;

                foreach (var source in scope.Sources)
                {
                    if (source.State == QuestSourceState.Completing ||
                        source.State == QuestSourceState.Completed ||
                        source.State == QuestSourceState.Failed ||
                        source.State == QuestSourceState.Cancelled)
                        return true;
                }

                return false;
            }
        }

        public void CancelPauseRequest(string activationId)
        {
            lock (_hintGate)
            {
                if (string.Equals(_pauseRequestedActivationId, activationId, StringComparison.Ordinal))
                    _pauseRequestedActivationId = null;
                _pausedSources.Remove(activationId ?? string.Empty);
            }
        }

        public void RearmAfterPause(string activationId)
        {
            List<QuestSourceV2> sources;
            lock (_hintGate)
            {
                if (!_pausedSources.TryGetValue(activationId ?? string.Empty, out sources)) return;
                _pausedSources.Remove(activationId);
            }

            foreach (var source in sources)
                source.TryRearmAfterPause(activationId);
        }

        public async Task<LessonCommandResultV2> TryApplyHintAsync(LessonCommandV2 command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.command_id) ||
                string.IsNullOrWhiteSpace(command.activation_id) || string.IsNullOrWhiteSpace(command.binding_id))
                return HintDecision(command, false, LessonCommandReasonV2.Malformed);

            ActiveExecution scope;
            QuestSourceV2 source;
            Task<bool> verbalSend = null;
            lock (_hintGate)
            {
                scope = _activeExecution;
                if (scope == null || !string.Equals(scope.Context.ActivationId, command.activation_id, StringComparison.Ordinal))
                    return HintDecision(command, false, LessonCommandReasonV2.StaleActivation);
                if (scope.Context.CancellationToken.IsCancellationRequested)
                    return HintDecision(command, false, LessonCommandReasonV2.Cancelled);
                if (scope.Context.SkipToken.IsCancellationRequested || scope.Context.TimeoutToken.IsCancellationRequested)
                    return HintDecision(command, false, LessonCommandReasonV2.InvalidState);

                source = scope.Sources.Find(candidate => string.Equals(candidate.BindingId, command.binding_id, StringComparison.Ordinal));
                if (source == null) return HintDecision(command, false, LessonCommandReasonV2.WrongBinding);
                if (!IsEligible(source, command.activation_id))
                    return HintDecision(command, false, IneligibleReason(source, command.activation_id));

                if (command.command == LessonCommandKindV2.VisualHint)
                {
                    if (!source.CanShowVisualHint) return HintDecision(command, false, LessonCommandReasonV2.UnsupportedCapability);
                    return source.TryShowVisualHint(command.activation_id)
                        ? HintDecision(command, true, LessonCommandReasonV2.None)
                        : HintDecision(command, false, IsEligible(source, command.activation_id)
                            ? LessonCommandReasonV2.UnsupportedCapability
                            : IneligibleReason(source, command.activation_id));
                }

                if (command.command != LessonCommandKindV2.VerbalHint)
                    return HintDecision(command, false, LessonCommandReasonV2.Malformed);
                if (!(source is IQuestVerbalHintV2 verbalSource) ||
                    (source is VoiceQuestSourceV2 voiceSource && !voiceSource.CanSendVerbalHint))
                    return HintDecision(command, false, LessonCommandReasonV2.UnsupportedCapability);

                try
                {
                    verbalSend = verbalSource.SendVerbalHintAsync(
                        command.activation_id,
                        command.command_id,
                        scope.Context.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return HintDecision(command, false, LessonCommandReasonV2.Cancelled);
                }
                catch
                {
                    return HintDecision(command, false, LessonCommandReasonV2.TransportUnavailable);
                }
            }

            bool sent;
            try { sent = await verbalSend; }
            catch (OperationCanceledException) { return HintDecision(command, false, LessonCommandReasonV2.Cancelled); }
            catch { return HintDecision(command, false, LessonCommandReasonV2.TransportUnavailable); }
            if (!sent) return HintDecision(command, false, LessonCommandReasonV2.TransportUnavailable);

            return HintDecision(command, true, LessonCommandReasonV2.None);
        }

        public LessonBindingV2[] GetActiveBindings(string activationId)
        {
            lock (_hintGate)
            {
                var scope = _activeExecution;
                if (scope == null || !string.Equals(scope.Context.ActivationId, activationId, StringComparison.Ordinal) ||
                    scope.Context.CancellationToken.IsCancellationRequested || scope.Context.SkipToken.IsCancellationRequested ||
                    scope.Context.TimeoutToken.IsCancellationRequested)
                    return Array.Empty<LessonBindingV2>();

                var bindings = new List<LessonBindingV2>();
                foreach (var source in scope.Sources)
                {
                    if (!IsEligible(source, activationId)) continue;
                    var voiceSource = source as VoiceQuestSourceV2;
                    bindings.Add(new LessonBindingV2
                    {
                        binding_id = source.BindingId,
                        npc_binding_id = voiceSource?.NpcBindingId ?? string.Empty,
                        can_verbal_hint = source is IQuestVerbalHintV2 && (voiceSource == null || voiceSource.CanSendVerbalHint),
                        can_visual_hint = source.CanShowVisualHint
                    });
                }
                return bindings.ToArray();
            }
        }

        private static bool IsEligible(QuestSourceV2 source, string activationId) =>
            source != null && source.State == QuestSourceState.Active &&
            string.Equals(source.CurrentActivationId, activationId, StringComparison.Ordinal);

        private static string IneligibleReason(QuestSourceV2 source, string activationId) =>
            source == null || !string.Equals(source.CurrentActivationId, activationId, StringComparison.Ordinal)
                ? LessonCommandReasonV2.StaleActivation
                : LessonCommandReasonV2.NotActive;

        private static LessonCommandResultV2 HintDecision(LessonCommandV2 command, bool accepted, string reason) =>
            new LessonCommandResultV2
            {
                contract_version = LessonRemoteContractV2.ContractVersion,
                @event = LessonRemoteContractV2.CommandResultEvent,
                command_id = command?.command_id ?? string.Empty,
                session_id = command?.session_id ?? string.Empty,
                run_id = command?.run_id ?? string.Empty,
                node_id = command?.node_id ?? string.Empty,
                activation_id = command?.activation_id ?? string.Empty,
                command = command?.command ?? string.Empty,
                binding_id = command?.binding_id ?? string.Empty,
                accepted = accepted,
                reason = reason
            };

        private sealed class ActiveExecution
        {
            public NodeExecutionContext Context { get; }
            public List<QuestSourceV2> Sources { get; }
            public ActiveExecution(NodeExecutionContext context, List<QuestSourceV2> sources)
            {
                Context = context;
                Sources = sources;
            }
        }

        private static void Claim(TaskCompletionSource<NodeResult> completion, ref int settled, NodeResult result)
        { if (Interlocked.CompareExchange(ref settled, 1, 0) == 0) completion.TrySetResult(result); }
        private static NodeResult Result(NodeExecutionContext context, NodeStatus status, string channel) => NodeResult.Completed(context?.Node?.Id, context?.ActivationId, status, Elapsed(context), channel);
        private static double Elapsed(NodeExecutionContext context) => context?.Clock?.ElapsedSeconds ?? context?.ElapsedSeconds ?? 0d;
        private static Task Never() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        private sealed class CancellationSignal : IDisposable
        {
            private readonly CancellationTokenRegistration _registration;
            private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task Task => _completion.Task;
            public CancellationSignal(CancellationToken token) { if (token.CanBeCanceled) _registration = token.Register(() => _completion.TrySetResult(true)); }
            public void Dispose() => _registration.Dispose();
        }
    }
}
