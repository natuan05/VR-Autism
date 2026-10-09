using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRAutism.Core;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Questing.Voice;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Executors;
using VRAutism.Gameplay.LessonGraphV2.Validation;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>Local observation of a live Quest child owned by a structured parent node.</summary>
    public sealed class ActiveQuestScopeV2
    {
        private readonly string[] _bindingIds;
        public string ParentSessionId { get; }
        public string ParentRunId { get; }
        public string ParentNodeId { get; }
        public string ParentActivationId { get; }
        public string ChildNodeId { get; }
        public string ChildActivationId { get; }
        public string[] BindingIds => (string[])_bindingIds.Clone();
        internal long LeaseId { get; }

        internal ActiveQuestScopeV2(string parentSessionId, string parentRunId, string parentNodeId,
            string parentActivationId, string childNodeId, string childActivationId, string[] bindingIds, long leaseId)
        {
            ParentSessionId = parentSessionId ?? string.Empty;
            ParentRunId = parentRunId ?? string.Empty;
            ParentNodeId = parentNodeId ?? string.Empty;
            ParentActivationId = parentActivationId ?? string.Empty;
            ChildNodeId = childNodeId ?? string.Empty;
            ChildActivationId = childActivationId ?? string.Empty;
            _bindingIds = bindingIds == null ? Array.Empty<string>() : (string[])bindingIds.Clone();
            LeaseId = leaseId;
        }
    }

    public sealed class LessonGraphRunner : MonoBehaviour
    {
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _commandSerial = new SemaphoreSlim(1, 1);
        private readonly HashSet<string> _commandIds = new HashSet<string>(StringComparer.Ordinal);
        private LessonGraph _graph;
        private INodeExecutorRegistry _registry;
        private ILessonStartPreflight _preflight;
        private INodeClock _clock;
        private bool _usesDefaultClock;
        private ICheckpointTelemetry _checkpointTelemetry;
        private ILessonVariableSource _variableSource;
        private readonly Dictionary<string, int> _runNodeVisitCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _runEdgeVisitCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private CancellationTokenSource _lessonCancellation;
        private CancellationTokenSource _activationCancellation;
        private CancellationTokenSource _skipCancellation;
        private CancellationTokenSource _timeoutCancellation;
        private Task<LessonResult> _activeTask;
        private LessonSessionContextV2 _sessionContext;
        private SynchronizationContext _unitySynchronizationContext;
        private LessonStateV2 _currentState;
        private INodeExecutor _activeExecutor;
        private Task<NodeResult> _activeExecutionTask;
        private QuestNodeExecutor _pauseQuestExecutor;
        private LessonNodeData _activeNode;
        private TaskCompletionSource<LessonStateV2> _pauseCompleted;
        private TaskCompletionSource<string> _resumeRequested;
        private TaskCompletionSource<LessonStateV2> _resumeStarted;
        private string _activeRunId;
        private string _activeActivationId;
        private LessonGraphRunnerInstaller _telemetryInstaller;
        private int _stateRevision;
        private bool _executorReady;
        private bool _pauseRequestedFlag;
        private bool _resumeInProgress;
        private bool _nodeCancellationEmitted;
        private bool _nodeResultCommitted;
        private bool _lessonCompletedEmitted;
        private ActiveQuestScopeV2 _activeQuestScope;
        private long _questScopeSequence;
        private string _pauseQuestActivationId;

        public event Action<NodeEnteredEvent> NodeEntered;
        public event Action<NodeCompletedEvent> NodeCompleted;
        public event Action<LessonCompletedEvent> LessonCompleted;
        public event Action<LessonStateV2> StateChanged;
        public event Action<LessonCommandResultV2> CommandEvaluated;
        public event Action<NodeCancelledEventV2> NodeCancelled;

        public LessonStateV2 CurrentState
        {
            get { lock (_gate) return CloneState(_currentState); }
        }

        /// <summary>Gets a live structured Quest child for local presentation and command routing.</summary>
        public ActiveQuestScopeV2 ActiveQuestScope
        {
            get
            {
                lock (_gate)
                {
                    return IsActiveQuestScopeLocked(_activeQuestScope) ? CloneQuestScope(_activeQuestScope) : null;
                }
            }
        }

        public IReadOnlyDictionary<string, int> RunNodeVisitCounts
        {
            get { lock (_gate) return new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(_runNodeVisitCounts, StringComparer.Ordinal)); }
        }

        public IReadOnlyDictionary<string, int> RunEdgeVisitCounts
        {
            get { lock (_gate) return new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(_runEdgeVisitCounts, StringComparer.Ordinal)); }
        }

        public void Configure(LessonGraph graph, INodeExecutorRegistry registry, ILessonStartPreflight preflight = null,
            INodeClock clock = null, ICheckpointTelemetry checkpointTelemetry = null, ILessonVariableSource variableSource = null)
        {
            _graph = graph;
            _registry = registry;
            _preflight = preflight;
            _clock = clock;
            _usesDefaultClock = clock == null;
            _checkpointTelemetry = checkpointTelemetry;
            _variableSource = variableSource;
        }

        public void ConfigureSession(LessonSessionContextV2 context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            lock (_gate)
            {
                if (_activeTask != null && !_activeTask.IsCompleted)
                    throw new InvalidOperationException("Session context cannot change while a lesson is running.");
                _sessionContext = context;
                _unitySynchronizationContext = SynchronizationContext.Current;
            }
        }

        public Task<LessonResult> StartLesson() => StartLessonAsync();

        public Task<LessonResult> StartLessonAsync()
        {
            lock (_gate)
            {
                if (_activeTask != null && !_activeTask.IsCompleted)
                {
                    Debug.LogWarning($"[LessonGraphV2] StartLesson ignored — lesson already running", this);
                    return _activeTask;
                }
                LessonGraphRunnerInstaller installer = _telemetryInstaller != null
                    ? _telemetryInstaller
                    : GetComponent<LessonGraphRunnerInstaller>();
                string authorizationError;
                bool installerOwnsThisRun = installer != null;
                if (installerOwnsThisRun && !installer.CanAuthorizeLessonStart(out authorizationError))
                {
                    Debug.LogError("[LessonGraphV2] Refusing to start because the active installer could not authorize the lesson. " +
                        authorizationError, this);
                    return Task.FromResult(LessonResult.Failed(Guid.NewGuid().ToString("N"), LessonFailureReason.InvalidGraph));
                }
                if (!CanStart()) return Task.FromResult(LessonResult.Failed(Guid.NewGuid().ToString("N"), LessonFailureReason.InvalidGraph));
                if (installerOwnsThisRun) installer.MarkLessonStartRequested();

                if (_usesDefaultClock) _clock = new MonotonicClock();
                _runNodeVisitCounts.Clear();
                _runEdgeVisitCounts.Clear();
                _lessonCancellation = new CancellationTokenSource();
                _activeRunId = Guid.NewGuid().ToString("N");
                _activeTask = RunAsync(_activeRunId, _lessonCancellation.Token);
                return _activeTask;
            }
        }

        internal void RegisterTelemetryInstaller(LessonGraphRunnerInstaller installer)
        {
            if (installer == null) throw new ArgumentNullException(nameof(installer));
            lock (_gate)
            {
                if (_telemetryInstaller != null && !ReferenceEquals(_telemetryInstaller, installer))
                    throw new InvalidOperationException("A lesson runner can have only one active V2 telemetry installer owner.");
                _telemetryInstaller = installer;
            }
        }

        internal void UnregisterTelemetryInstaller(LessonGraphRunnerInstaller installer)
        {
            if (installer == null) return;
            lock (_gate)
            {
                if (ReferenceEquals(_telemetryInstaller, installer))
                    _telemetryInstaller = null;
            }
        }

        public void RequestSkip() => SignalNodeCompletionRequest(skip: true);
        public void RequestTimeout() => SignalNodeCompletionRequest(skip: false);
        public void AbortLesson()
        {
            LessonStateV2 changedState = null;
            NodeCancelledEventV2 cancelledEvent = null;
            CancellationTokenSource lessonCancellation;
            CancellationTokenSource activationCancellation;
            QuestNodeExecutor pauseExecutor;
            string pauseActivationId;
            lock (_gate)
            {
                lessonCancellation = _lessonCancellation;
                activationCancellation = _activationCancellation;
                if (string.IsNullOrEmpty(_activeRunId) || IsTerminal(_currentState?.status)) return;

                pauseExecutor = _pauseQuestExecutor;
                pauseActivationId = _currentState?.activation_id ?? _activeActivationId ?? string.Empty;
                _pauseQuestExecutor = null;
                _pauseRequestedFlag = false;
                cancelledEvent = CreateNodeCancelledLocked("CANCELLED");
                changedState = SetStateLocked("cancelled", _activeRunId, _currentState?.node_id ?? _activeNode?.Id,
                    _currentState?.node_type ?? _activeNode?.NodeType.ToString(), _currentState?.node_index ?? NodeIndex(_activeNode?.Id),
                    _currentState?.activation_id ?? _activeActivationId);
                _resumeInProgress = false;
                _pauseCompleted?.TrySetResult(CloneState(changedState));
                _resumeStarted?.TrySetResult(CloneState(changedState));
            }

            if (cancelledEvent != null) Emit(NodeCancelled, cancelledEvent);
            Emit(StateChanged, CloneState(changedState));
            pauseExecutor?.CancelPauseRequest(pauseActivationId);
            CancelActive(activationCancellation);
            CancelActive(lessonCancellation);
        }

        public Task<LessonCommandResultV2> ApplyCommandAsync(LessonCommandV2 command) =>
            ApplyCommandAsync(command, null);

        public Task<LessonCommandResultV2> ApplyCommandAsync(
            LessonCommandV2 command,
            Func<LessonCommandV2, bool> publishScript)
        {
            var context = _unitySynchronizationContext;
            if (context != null && !ReferenceEquals(SynchronizationContext.Current, context))
            {
                var completion = new TaskCompletionSource<LessonCommandResultV2>(TaskCreationOptions.RunContinuationsAsynchronously);
                context.Post(async _ =>
                {
                    try { completion.TrySetResult(await ApplyCommandSerializedAsync(command, publishScript)); }
                    catch (Exception exception) { completion.TrySetException(exception); }
                }, null);
                return completion.Task;
            }
            return ApplyCommandSerializedAsync(command, publishScript);
        }

        private async Task<LessonCommandResultV2> ApplyCommandSerializedAsync(
            LessonCommandV2 command,
            Func<LessonCommandV2, bool> publishScript)
        {
            Task<LessonCommandResultV2> decisionTask;
            await _commandSerial.WaitAsync();
            try { decisionTask = ApplyCommandOnRunnerAsync(command, publishScript); }
            finally { _commandSerial.Release(); }
            return await decisionTask;
        }

        private async Task<LessonCommandResultV2> ApplyCommandOnRunnerAsync(
            LessonCommandV2 command,
            Func<LessonCommandV2, bool> publishScript)
        {
            var malformedReason = ValidateCommandEnvelope(command);
            if (malformedReason != null) return PublishDecision(CreateCommandResult(command, false, malformedReason, CurrentState));

            CancellationTokenSource activationToCancel = null;
            TaskCompletionSource<LessonStateV2> pauseCompletion = null;
            TaskCompletionSource<string> resumeSignal = null;
            TaskCompletionSource<LessonStateV2> resumeStarted = null;
            Task<LessonCommandResultV2> hintTask = null;
            bool applyVolume = false;
            bool acceptScript = false;
            QuestNodeExecutor hintExecutor = null;
            QuestNodeExecutor pauseExecutor = null;
            LessonStateV2 changedState = null;
            string rejection = null;
            string resumedActivationId = null;
            string pauseTargetActivationId = null;
            long hintScopeLease = 0L;

            lock (_gate)
            {
                if (_sessionContext == null || command.session_id != _sessionContext.SessionId)
                {
                    rejection = LessonCommandReasonV2.WrongSession;
                }
                else if (string.IsNullOrEmpty(_activeRunId) || command.run_id != _activeRunId)
                {
                    rejection = LessonCommandReasonV2.WrongRun;
                }
                else if (_commandIds.Contains(command.command_id))
                {
                    rejection = LessonCommandReasonV2.Duplicate;
                }
                else
                {
                    _commandIds.Add(command.command_id);
                    if (_currentState == null || command.node_id != _currentState.node_id)
                        rejection = LessonCommandReasonV2.WrongNode;
                    else if (command.activation_id != _currentState.activation_id)
                        rejection = LessonCommandReasonV2.StaleActivation;
                    else if (command.command == LessonCommandKindV2.Skip)
                    {
                        if (_currentState.status != "running" || _skipCancellation == null)
                            rejection = LessonCommandReasonV2.InvalidState;
                    }
                    else if (command.command == LessonCommandKindV2.Pause)
                    {
                        if (_currentState.status != "running" || _activationCancellation == null || _nodeResultCommitted ||
                            (_activeExecutionTask != null && _activeExecutionTask.IsCompleted))
                            rejection = LessonCommandReasonV2.InvalidState;
                        else if (_activeExecutor is QuestNodeExecutor questExecutor &&
                                 questExecutor.HasTerminalSourceDecision(command.activation_id))
                            rejection = LessonCommandReasonV2.InvalidState;
                        else if (IsActiveQuestScopeLocked(_activeQuestScope) && _registry != null &&
                            _registry.TryGet(NodeType.Quest, out var activeChildExecutor) &&
                            activeChildExecutor is QuestNodeExecutor activeChildQuestExecutor &&
                            activeChildQuestExecutor.HasTerminalSourceDecision(_activeQuestScope.ChildActivationId))
                            rejection = LessonCommandReasonV2.InvalidState;
                        else
                        {
                            var childScopeForPause = IsActiveQuestScopeLocked(_activeQuestScope)
                                ? _activeQuestScope
                                : null;
                            _pauseRequestedFlag = true;
                            pauseExecutor = _activeExecutor as QuestNodeExecutor;
                            pauseTargetActivationId = command.activation_id;
                            if (pauseExecutor == null && childScopeForPause != null &&
                                _registry != null && _registry.TryGet(NodeType.Quest, out var childPauseExecutor))
                            {
                                pauseExecutor = childPauseExecutor as QuestNodeExecutor;
                                pauseTargetActivationId = childScopeForPause.ChildActivationId;
                            }
                            _pauseQuestExecutor = pauseExecutor;
                            _pauseQuestActivationId = pauseTargetActivationId;
                            _pauseCompleted = new TaskCompletionSource<LessonStateV2>(TaskCreationOptions.RunContinuationsAsynchronously);
                            _resumeRequested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                            _resumeStarted = new TaskCompletionSource<LessonStateV2>(TaskCreationOptions.RunContinuationsAsynchronously);
                            pauseCompletion = _pauseCompleted;
                            activationToCancel = _activationCancellation;
                            changedState = SetStateLocked("pausing", _activeRunId, _activeNode?.Id,
                                _activeNode?.NodeType.ToString(), NodeIndex(_activeNode?.Id), _activeActivationId);
                        }
                    }
                    else if (command.command == LessonCommandKindV2.Resume)
                    {
                        if (_currentState.status != "paused" || _resumeInProgress || _resumeRequested == null || _resumeStarted == null)
                            rejection = LessonCommandReasonV2.InvalidState;
                        else
                        {
                            _resumeInProgress = true;
                            resumedActivationId = Guid.NewGuid().ToString("N");
                            resumeSignal = _resumeRequested;
                            resumeStarted = _resumeStarted;
                        }
                    }
                    else if (command.command == LessonCommandKindV2.VerbalHint || command.command == LessonCommandKindV2.VisualHint)
                    {
                        if (_currentState.status != "running" || _nodeResultCommitted)
                            rejection = LessonCommandReasonV2.InvalidState;
                        else if (_executorReady && _activeExecutor is QuestNodeExecutor questExecutor)
                        {
                            hintExecutor = questExecutor;
                            try { hintTask = questExecutor.TryApplyHintAsync(command); }
                            catch { rejection = LessonCommandReasonV2.TransportUnavailable; }
                        }
                        else if (IsActiveQuestScopeLocked(_activeQuestScope) &&
                            _registry != null && _registry.TryGet(NodeType.Quest, out var childExecutor) &&
                            childExecutor is QuestNodeExecutor childQuestExecutor)
                        {
                            hintExecutor = childQuestExecutor;
                            hintScopeLease = _activeQuestScope.LeaseId;
                            var childCommand = CloneCommand(command);
                            childCommand.activation_id = _activeQuestScope.ChildActivationId;
                            try { hintTask = childQuestExecutor.TryApplyHintAsync(childCommand); }
                            catch { rejection = LessonCommandReasonV2.TransportUnavailable; }
                        }
                        else if (!_executorReady)
                            rejection = LessonCommandReasonV2.NotActive;
                        else
                            rejection = LessonCommandReasonV2.UnsupportedCapability;
                    }
                    else if (command.command == LessonCommandKindV2.SetVolume || command.command == LessonCommandKindV2.SpeakScript)
                    {
                        if (_currentState.status != "running" || _nodeResultCommitted)
                            rejection = LessonCommandReasonV2.InvalidState;
                        else if (!_executorReady)
                            rejection = LessonCommandReasonV2.NotActive;
                        else if (command.command == LessonCommandKindV2.SetVolume)
                        {
                            if (SessionContext.Instance == null)
                                rejection = LessonCommandReasonV2.NotActive;
                            else
                                applyVolume = true;
                        }
                        else if (!TryResolveVoiceBinding(_currentState, command.npc_binding_id, out _))
                            rejection = LessonCommandReasonV2.WrongBinding;
                        else if (publishScript == null)
                            rejection = LessonCommandReasonV2.TransportUnavailable;
                        else
                        {
                            try { acceptScript = publishScript(command); }
                            catch { acceptScript = false; }
                            if (!acceptScript) rejection = LessonCommandReasonV2.TransportUnavailable;
                        }
                    }
                    else if (_currentState.status != "running")
                    {
                        rejection = LessonCommandReasonV2.InvalidState;
                    }
                    else if (!_executorReady)
                    {
                        rejection = LessonCommandReasonV2.NotActive;
                    }
                    else
                    {
                        rejection = LessonCommandReasonV2.UnsupportedCapability;
                    }
                }
            }

            if (rejection != null)
                return PublishDecision(CreateCommandResult(command, false, rejection, CurrentState));

            if (applyVolume)
            {
                SessionContext.Instance.MaxVolume = command.volume;
                return PublishDecision(CreateCommandResult(command, true, LessonCommandReasonV2.None, CurrentState));
            }

            if (acceptScript)
                return PublishDecision(CreateCommandResult(command, true, LessonCommandReasonV2.None, CurrentState));

            if (hintTask != null)
                return await CompleteHintCommandAsync(command, hintExecutor, hintTask, hintScopeLease);

            if (changedState != null)
            {
                pauseExecutor?.NotifyPauseRequested(pauseTargetActivationId ?? command.activation_id);
                CancelActive(activationToCancel);
                Emit(StateChanged, CloneState(changedState));
                var pausedState = await pauseCompletion.Task;
                return PublishDecision(CreateCommandResult(command, true, LessonCommandReasonV2.None, pausedState));
            }

            if (resumeSignal != null)
            {
                resumeSignal.TrySetResult(resumedActivationId);
                var activeState = await resumeStarted.Task;
                return PublishDecision(CreateCommandResult(command, true, LessonCommandReasonV2.None, activeState));
            }

            RequestSkip();
            return PublishDecision(CreateCommandResult(command, true, LessonCommandReasonV2.None, CurrentState));
        }

        private string ValidateCommandEnvelope(LessonCommandV2 command)
        {
            if (command == null || command.contract_version != LessonRemoteContractV2.ContractVersion ||
                command.@event != LessonRemoteContractV2.CommandEvent ||
                string.IsNullOrWhiteSpace(command.command_id) || string.IsNullOrWhiteSpace(command.session_id) ||
                string.IsNullOrWhiteSpace(command.run_id) || string.IsNullOrWhiteSpace(command.node_id) ||
                string.IsNullOrWhiteSpace(command.activation_id) || string.IsNullOrWhiteSpace(command.command))
                return LessonCommandReasonV2.Malformed;

            var isHint = command.command == LessonCommandKindV2.VerbalHint || command.command == LessonCommandKindV2.VisualHint;
            var isVolume = command.command == LessonCommandKindV2.SetVolume;
            var isScript = command.command == LessonCommandKindV2.SpeakScript;
            if (command.command != LessonCommandKindV2.Skip && command.command != LessonCommandKindV2.Pause &&
                command.command != LessonCommandKindV2.Resume && !isHint && !isVolume && !isScript)
                return LessonCommandReasonV2.Malformed;
            if (isHint ? string.IsNullOrWhiteSpace(command.binding_id) : !string.IsNullOrWhiteSpace(command.binding_id))
                return LessonCommandReasonV2.Malformed;
            if (isVolume && (float.IsNaN(command.volume) || float.IsInfinity(command.volume) || command.volume < 0f || command.volume > 1f))
                return LessonCommandReasonV2.Malformed;
            if (isScript && (string.IsNullOrWhiteSpace(command.npc_binding_id) || string.IsNullOrWhiteSpace(command.text) ||
                command.text.Length > VoiceQuestTransportV2Constants.MaxScriptLength))
                return LessonCommandReasonV2.Malformed;
            return null;
        }

        public bool RecordAcceptedVoiceReminder(string activationId, string npcBindingId, string commandId)
        {
            LessonCommandResultV2 decision;
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(activationId) || string.IsNullOrWhiteSpace(npcBindingId) ||
                    string.IsNullOrWhiteSpace(commandId) || _sessionContext == null || _currentState == null ||
                    _currentState.status != "running" || !_executorReady || _nodeResultCommitted || _pauseRequestedFlag ||
                    string.IsNullOrWhiteSpace(_activeRunId) ||
                    !string.Equals(_currentState.session_id, _sessionContext.SessionId, StringComparison.Ordinal) ||
                    !string.Equals(_currentState.run_id, _activeRunId, StringComparison.Ordinal) ||
                    !string.Equals(_currentState.activation_id, activationId, StringComparison.Ordinal) ||
                    !string.Equals(_activeActivationId, activationId, StringComparison.Ordinal) ||
                    _commandIds.Contains(commandId) ||
                    !TryResolveVoiceBinding(_currentState, npcBindingId, out var bindingId))
                    return false;

                _commandIds.Add(commandId);
                decision = new LessonCommandResultV2
                {
                    contract_version = LessonRemoteContractV2.ContractVersion,
                    @event = LessonRemoteContractV2.CommandResultEvent,
                    command_id = commandId,
                    session_id = _currentState.session_id,
                    run_id = _currentState.run_id,
                    node_id = _currentState.node_id,
                    activation_id = _currentState.activation_id,
                    command = LessonCommandKindV2.VerbalHint,
                    binding_id = bindingId,
                    accepted = true,
                    reason = LessonCommandReasonV2.None,
                    state = CloneState(_currentState)
                };
            }

            PublishDecision(decision);
            return true;
        }

        private static bool TryResolveVoiceBinding(LessonStateV2 state, string npcBindingId, out string bindingId)
        {
            bindingId = string.Empty;
            if (state?.bindings == null || string.IsNullOrWhiteSpace(npcBindingId)) return false;
            LessonBindingV2 matched = null;
            for (var index = 0; index < state.bindings.Length; index++)
            {
                var candidate = state.bindings[index];
                if (candidate == null || !candidate.can_verbal_hint ||
                    !string.Equals(candidate.npc_binding_id, npcBindingId, StringComparison.Ordinal))
                    continue;
                if (matched != null) return false;
                matched = candidate;
            }
            if (matched == null || string.IsNullOrWhiteSpace(matched.binding_id)) return false;
            bindingId = matched.binding_id;
            return true;
        }

        private LessonCommandResultV2 PublishDecision(LessonCommandResultV2 decision)
        {
            Emit(CommandEvaluated, decision);
            return decision;
        }

        private bool CanStart()
        {
            try
            {
                if (_graph == null)
                {
                    Debug.LogWarning($"[LessonGraphV2] CanStart failed: graph is null (did Installer configure runner?)", this);
                    return false;
                }
                if (_registry == null)
                {
                    Debug.LogWarning($"[LessonGraphV2] CanStart failed: registry is null", this);
                    return false;
                }
                var validation = LessonGraphValidator.ValidateForExecution(_graph, _variableSource != null);
                if (!validation.IsValid)
                {
                    var errorDetails = string.Join("; ", validation.Errors);
                    Debug.LogWarning($"[LessonGraphV2] CanStart failed: graph validation failed: {errorDetails}", this);
                    return false;
                }
                if (_preflight != null && !_preflight.IsReady(_graph, out var reason))
                {
                    Debug.LogWarning($"[LessonGraphV2] CanStart failed: preflight not ready. Reason: {reason}", this);
                    return false;
                }
                if (!_graph.Nodes.All(node => node != null && (IsStructuredNode(node.NodeType) ||
                    (_registry.TryGet(node.NodeType, out var executor) && executor != null))))
                {
                    var missing = _graph.Nodes.Where(node => node == null || (!IsStructuredNode(node.NodeType) &&
                        (!_registry.TryGet(node.NodeType, out var exec) || exec == null)))
                                              .Select(node => node?.Id ?? "null_node");
                    Debug.LogWarning($"[LessonGraphV2] CanStart failed: missing executor for nodes: {string.Join(", ", missing)}", this);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LessonGraphV2] CanStart exception: {ex}", this);
                return false;
            }
        }

        private static bool IsStructuredNode(NodeType type) =>
            type == NodeType.Parallel || type == NodeType.Gate || type == NodeType.Loop;

        private async Task<LessonResult> RunAsync(string runId, CancellationToken cancellationToken)
        {
            try
            {
                var nodes = _graph.Nodes.ToDictionary(node => node.Id);
                var branchEvidence = new Dictionary<string, StructuredBranchEvidence>(StringComparer.Ordinal);
                var node = nodes[_graph.EntryNodeId];
                Debug.Log($"[LessonGraphV2] LESSON START graph={_graph.name} entry={node.Id} runId={runId}", this);
                string activationId = null;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    activationId = activationId ?? Guid.NewGuid().ToString("N");
                    RecordNodeVisit(node.Id);
                    BeginActivation(runId, activationId, node);
                    if (cancellationToken.IsCancellationRequested) return FinishAborted(runId);
                    Emit(NodeEntered, new NodeEnteredEvent(runId, activationId, node.Id, _clock.ElapsedSeconds));
                    if (cancellationToken.IsCancellationRequested) return FinishAborted(runId);
                    if (IsPauseRequested(runId, activationId))
                    {
                        CompletePauseAfterCleanup(runId, activationId);
                        activationId = await WaitForResumeOrCancellationAsync(runId, cancellationToken);
                        if (activationId == null || cancellationToken.IsCancellationRequested) return FinishAborted(runId);
                        continue;
                    }
                    INodeExecutor executor = null;
                    if (!IsStructuredNode(node.NodeType)) _registry.TryGet(node.NodeType, out executor);
                    NodeResult result;
                    var activationToken = GetActivationToken(runId, activationId);
                    try
                    {
                        SetExecutorStarting(runId, activationId, executor);
                        var context = new NodeExecutionContext(runId, activationId, _graph.name, node, _clock.ElapsedSeconds,
                            activationToken, _skipCancellation.Token, _timeoutCancellation.Token, _checkpointTelemetry, _clock);
                        var executionTask = IsStructuredNode(node.NodeType)
                            ? ExecuteStructuredNodeAsync(context, branchEvidence)
                            : executor.ExecuteAsync(context);
                        var executorState = SetExecutorReady(runId, activationId, executor, executionTask);
                        if (executorState != null) Emit(StateChanged, executorState);
                        if (executionTask == null)
                        {
                            result = null;
                        }
                        else
                        {
                            using (var abort = new CancellationSignal(cancellationToken))
                            {
                                if (await Task.WhenAny(executionTask, abort.Task) != executionTask)
                                {
                                    ObserveFault(executionTask);
                                    return FinishAborted(runId);
                                }
                            }
                            result = await executionTask;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return FinishAborted(runId);
                    }
                    catch (OperationCanceledException) when (activationToken.IsCancellationRequested && IsPauseRequested(runId, activationId))
                    {
                        result = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[LessonGraphV2] Executor exception node={node.Id}: {ex}", this);
                        result = NodeResult.Completed(node.Id, activationId, NodeStatus.Failed, _clock.ElapsedSeconds, "exception");
                    }

                    SetExecutorReady(runId, activationId, null, null);

                    if (cancellationToken.IsCancellationRequested)
                        return FinishAborted(runId);

                    if (IsPauseRequested(runId, activationId))
                    {
                        CompletePauseAfterCleanup(runId, activationId);
                        activationId = await WaitForResumeOrCancellationAsync(runId, cancellationToken);
                        if (activationId == null || cancellationToken.IsCancellationRequested)
                            return FinishAborted(runId);
                        continue;
                    }

                    if (!IsCurrentActivation(runId, activationId)) return FinishAborted(runId);
                    if (result == null || result.ActivationId != activationId || result.NodeId != node.Id)
                    {
                        Debug.LogWarning($"[LessonGraphV2] Invalid/null result for node={node.Id}, treating as failed", this);
                        result = NodeResult.Completed(node.Id, activationId, NodeStatus.Failed, _clock.ElapsedSeconds, "invalid_result");
                    }

                    if (!CommitNodeResult(runId, activationId)) return FinishAborted(runId);
                    Emit(NodeCompleted, new NodeCompletedEvent(result));
                    if (cancellationToken.IsCancellationRequested || !IsCurrentActivation(runId, activationId))
                        return FinishAborted(runId);
                    LessonEdgeData next;
                    if (node.Config is ParallelNodeConfig parallelConfig && result.Status == NodeStatus.Success)
                    {
                        next = null;
                        RecordEdgeVisit(node.Id, parallelConfig.GateNodeId);
                        node = nodes[parallelConfig.GateNodeId];
                        activationId = null;
                        continue;
                    }
                    if (node.Config is LoopNodeConfig loopConfig && result.Status == NodeStatus.Success)
                    {
                        next = null;
                        RecordEdgeVisit(node.Id, loopConfig.ExitNodeId);
                        node = nodes[loopConfig.ExitNodeId];
                        activationId = null;
                        continue;
                    }
                    next = SelectEdge(node.Id, result.Status);
                    if (node.Config is ParallelNodeConfig failedParallel && result.Status != NodeStatus.Success &&
                        next != null && next.ToNodeId == failedParallel.GateNodeId)
                        next = null;
                    Debug.Log($"[LessonGraphV2] EDGE node={node.Id} status={result.Status} → next={next?.ToNodeId ?? "TERMINAL"}", this);
                    if (next == null)
                    {
                        var completed = LessonResult.Completed(runId, result, _clock.ElapsedSeconds);
                        var terminalState = result.Status == NodeStatus.Failed || result.Status == NodeStatus.Timeout ? "failed" : "completed";
                        return FinishLesson(runId, completed, terminalState);
                    }
                    RecordEdgeVisit(next.FromNodeId, next.ToNodeId);
                    node = nodes[next.ToNodeId];
                    activationId = null;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return FinishAborted(runId);
            }
            finally
            {
                EndRun(runId);
            }
        }

        private LessonEdgeData SelectEdge(string nodeId, NodeStatus status)
        {
            var outgoing = _graph.Edges.Where(edge => edge != null && edge.FromNodeId == nodeId).Select((edge, index) => new { edge, index });
            var matchedStatus = outgoing.Where(candidate => candidate.edge.Condition is StatusCondition condition && condition.RequiredStatus == NodeStatusCondition.ToCondition(status))
                .OrderBy(candidate => candidate.edge.Priority).ThenBy(candidate => candidate.index).FirstOrDefault();
            if (matchedStatus != null) return matchedStatus.edge;
            return outgoing.Where(candidate => !(candidate.edge.Condition is StatusCondition) &&
                    LessonConditionEvaluator.Evaluate(candidate.edge.Condition, status, _variableSource))
                .OrderBy(candidate => candidate.edge.Priority).ThenBy(candidate => candidate.index)
                .Select(candidate => candidate.edge).FirstOrDefault();
        }

        private async Task<NodeResult> ExecuteStructuredNodeAsync(NodeExecutionContext context,
            Dictionary<string, StructuredBranchEvidence> branchEvidence)
        {
            var flow = new StructuredFlowExecution(_graph, _registry, _clock, _variableSource, _checkpointTelemetry,
                RecordNodeVisit, RecordEdgeVisit,
                (child, childActivationId) => BeginStructuredQuestScope(context, child, childActivationId),
                MarkStructuredQuestScopeReady,
                EndStructuredQuestScope);
            if (context.Node.Config is ParallelNodeConfig)
            {
                var result = await flow.ExecuteParallelAsync(context);
                if (result.BranchEvidence != null) branchEvidence[context.Node.Id] = result.BranchEvidence;
                return result.ParentResult;
            }
            if (context.Node.Config is GateNodeConfig gate)
            {
                branchEvidence.TryGetValue(gate.ParallelNodeId, out var evidence);
                branchEvidence.Remove(gate.ParallelNodeId);
                return flow.ExecuteGate(context, evidence);
            }
            if (context.Node.Config is LoopNodeConfig)
                return (await flow.ExecuteLoopAsync(context)).ParentResult;
            return NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Failed,
                _clock.ElapsedSeconds, "unsupported_structured_node");
        }

        private void RecordNodeVisit(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId)) return;
            lock (_gate)
            {
                _runNodeVisitCounts.TryGetValue(nodeId, out var count);
                _runNodeVisitCounts[nodeId] = count + 1;
            }
        }

        private long BeginStructuredQuestScope(NodeExecutionContext parent, LessonNodeData child, string childActivationId)
        {
            if (parent == null || !(child?.Config is QuestNodeConfig questConfig) || string.IsNullOrWhiteSpace(childActivationId))
                return 0L;
            lock (_gate)
            {
                if (_currentState == null || _currentState.status != "running" || _pauseRequestedFlag || _nodeResultCommitted ||
                    _activeRunId != parent.RunId || _activeActivationId != parent.ActivationId ||
                    _currentState.node_id != parent.Node.Id || _currentState.activation_id != parent.ActivationId ||
                    parent.CancellationToken.IsCancellationRequested || parent.SkipToken.IsCancellationRequested ||
                    parent.TimeoutToken.IsCancellationRequested)
                    return 0L;

                var leaseId = ++_questScopeSequence;
                _activeQuestScope = new ActiveQuestScopeV2(_currentState.session_id, parent.RunId, parent.Node.Id,
                    parent.ActivationId, child.Id, childActivationId,
                    questConfig.CompletionBindingIds?.ToArray(), leaseId);
                return leaseId;
            }
        }

        private void EndStructuredQuestScope(long leaseId)
        {
            if (leaseId == 0L) return;
            LessonStateV2 changedState = null;
            lock (_gate)
            {
                if (_activeQuestScope?.LeaseId == leaseId)
                {
                    var scope = _activeQuestScope;
                    _activeQuestScope = null;
                    if (_currentState != null && _currentState.status == "running" &&
                        _currentState.run_id == scope.ParentRunId && _currentState.node_id == scope.ParentNodeId &&
                        _currentState.activation_id == scope.ParentActivationId)
                    {
                        _currentState.bindings = Array.Empty<LessonBindingV2>();
                        _currentState.state_revision = ++_stateRevision;
                        _currentState.updated_at_utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                        changedState = CloneState(_currentState);
                    }
                }
            }
            if (changedState != null) Emit(StateChanged, changedState);
        }

        private void MarkStructuredQuestScopeReady(LessonNodeData child, string childActivationId, long leaseId)
        {
            if (leaseId == 0L || _registry == null || !_registry.TryGet(NodeType.Quest, out var executor) ||
                !(executor is QuestNodeExecutor questExecutor))
                return;

            var bindings = questExecutor.GetActiveBindings(childActivationId);
            if (bindings == null || bindings.Length == 0) return;
            LessonStateV2 changedState = null;
            lock (_gate)
            {
                var scope = _activeQuestScope;
                if (scope == null || scope.LeaseId != leaseId || !IsActiveQuestScopeLocked(scope) ||
                    scope.ChildNodeId != child?.Id || scope.ChildActivationId != childActivationId)
                    return;
                _currentState.bindings = bindings;
                _currentState.state_revision = ++_stateRevision;
                _currentState.updated_at_utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                changedState = CloneState(_currentState);
            }
            Emit(StateChanged, changedState);
        }

        private bool IsActiveQuestScopeLocked(ActiveQuestScopeV2 scope) =>
            scope != null && _currentState != null && _currentState.status == "running" &&
            !_pauseRequestedFlag && !_nodeResultCommitted && _activationCancellation != null &&
            !_activationCancellation.IsCancellationRequested && _activeRunId == scope.ParentRunId &&
            _skipCancellation != null && !_skipCancellation.IsCancellationRequested &&
            _timeoutCancellation != null && !_timeoutCancellation.IsCancellationRequested &&
            _activeActivationId == scope.ParentActivationId && _currentState.run_id == scope.ParentRunId &&
            _currentState.node_id == scope.ParentNodeId && _currentState.activation_id == scope.ParentActivationId &&
            _currentState.session_id == scope.ParentSessionId;

        private static ActiveQuestScopeV2 CloneQuestScope(ActiveQuestScopeV2 scope) => scope == null ? null :
            new ActiveQuestScopeV2(scope.ParentSessionId, scope.ParentRunId, scope.ParentNodeId,
                scope.ParentActivationId, scope.ChildNodeId, scope.ChildActivationId, scope.BindingIds, scope.LeaseId);

        private static LessonCommandV2 CloneCommand(LessonCommandV2 command) => new LessonCommandV2
        {
            contract_version = command.contract_version,
            @event = command.@event,
            command_id = command.command_id,
            session_id = command.session_id,
            run_id = command.run_id,
            node_id = command.node_id,
            activation_id = command.activation_id,
            command = command.command,
            binding_id = command.binding_id,
            volume = command.volume,
            npc_binding_id = command.npc_binding_id,
            text = command.text
        };

        private void RecordEdgeVisit(string fromNodeId, string toNodeId)
        {
            if (string.IsNullOrWhiteSpace(fromNodeId) || string.IsNullOrWhiteSpace(toNodeId)) return;
            var key = fromNodeId + "->" + toNodeId;
            lock (_gate)
            {
                _runEdgeVisitCounts.TryGetValue(key, out var count);
                _runEdgeVisitCounts[key] = count + 1;
            }
        }

        private void BeginActivation(string runId, string activationId, LessonNodeData node)
        {
            LessonStateV2 state;
            TaskCompletionSource<LessonStateV2> resumeStarted;
            lock (_gate)
            {
                var isNewRun = _currentState == null || _currentState.run_id != runId;
                _skipCancellation?.Dispose();
                _timeoutCancellation?.Dispose();
                _activationCancellation?.Dispose();
                if (isNewRun)
                {
                    _commandIds.Clear();
                    _lessonCompletedEmitted = false;
                }
                _activationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lessonCancellation.Token);
                _skipCancellation = new CancellationTokenSource();
                _timeoutCancellation = new CancellationTokenSource();
                _activeRunId = runId;
                _activeActivationId = activationId;
                _activeNode = node;
                _activeExecutor = null;
                _activeExecutionTask = null;
                _executorReady = false;
                _pauseRequestedFlag = false;
                _resumeInProgress = false;
                _nodeCancellationEmitted = false;
                _nodeResultCommitted = false;
                state = SetStateLocked("running", runId, node.Id, node.NodeType.ToString(), NodeIndex(node.Id), activationId);
                resumeStarted = _resumeStarted;
                _pauseCompleted = null;
                _resumeRequested = null;
                _resumeStarted = null;
            }
            Emit(StateChanged, CloneState(state));
            resumeStarted?.TrySetResult(CloneState(state));
        }

        private bool IsCurrentActivation(string runId, string activationId)
        {
            lock (_gate) return _activeRunId == runId && _activeActivationId == activationId;
        }

        private void EndRun(string runId)
        {
            lock (_gate)
            {
                if (_activeRunId != runId) return;
                _lessonCancellation?.Dispose(); _lessonCancellation = null;
                _activationCancellation?.Dispose(); _activationCancellation = null;
                _skipCancellation?.Dispose(); _skipCancellation = null;
                _timeoutCancellation?.Dispose(); _timeoutCancellation = null;
                _activeExecutor = null;
                _activeExecutionTask = null;
                _activeNode = null;
                _executorReady = false;
                _pauseRequestedFlag = false;
                _resumeInProgress = false;
                _pauseCompleted = null;
                _resumeRequested = null;
                _resumeStarted = null;
                _activeRunId = null;
                _activeActivationId = null;
                _activeQuestScope = null;
                _pauseQuestActivationId = null;
            }
        }

        private void SignalNodeCompletionRequest(bool skip)
        {
            CancellationTokenSource signal;
            lock (_gate)
            {
                if (_currentState == null || _currentState.status != "running") return;
                signal = skip ? _skipCancellation : _timeoutCancellation;
            }
            CancelActive(signal);
        }

        private CancellationToken GetActivationToken(string runId, string activationId)
        {
            lock (_gate)
                return _activeRunId == runId && _activeActivationId == activationId && _activationCancellation != null
                    ? _activationCancellation.Token
                    : CancellationToken.None;
        }

        private LessonStateV2 SetExecutorReady(string runId, string activationId, INodeExecutor executor, Task<NodeResult> executionTask)
        {
            lock (_gate)
            {
                if (_activeRunId != runId || _activeActivationId != activationId) return null;
                if (executionTask != null) _activeExecutionTask = executionTask;
                var taskStarted = executionTask != null && !executionTask.IsCompleted;
                _activeExecutor = taskStarted ? executor : null;
                _executorReady = executor != null && taskStarted;
                if (!_executorReady || !(executor is QuestNodeExecutor questExecutor) ||
                    _currentState == null || _currentState.status != "running")
                    return null;

                _currentState.bindings = questExecutor.GetActiveBindings(activationId);
                _currentState.state_revision = ++_stateRevision;
                _currentState.updated_at_utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                return CloneState(_currentState);
            }
        }

        private void SetExecutorStarting(string runId, string activationId, INodeExecutor executor)
        {
            lock (_gate)
            {
                if (_activeRunId != runId || _activeActivationId != activationId) return;
                _activeExecutor = executor;
                _activeExecutionTask = null;
                _executorReady = false;
            }
        }

        private bool CommitNodeResult(string runId, string activationId)
        {
            lock (_gate)
            {
                if (_activeRunId != runId || _activeActivationId != activationId ||
                    _currentState?.status != "running" || _lessonCancellation?.IsCancellationRequested == true)
                    return false;
                _nodeResultCommitted = true;
                _executorReady = false;
                _activeExecutor = null;
                _activeExecutionTask = null;
                return true;
            }
        }

        private async Task<LessonCommandResultV2> CompleteHintCommandAsync(
            LessonCommandV2 command,
            QuestNodeExecutor executor,
            Task<LessonCommandResultV2> hintTask,
            long childScopeLease)
        {
            LessonCommandResultV2 hintResult;
            try { hintResult = await hintTask; }
            catch (OperationCanceledException)
            {
                hintResult = CreateCommandResult(command, false, LessonCommandReasonV2.Cancelled, null);
            }
            catch
            {
                hintResult = CreateCommandResult(command, false, LessonCommandReasonV2.TransportUnavailable, null);
            }

            bool accepted = hintResult != null && hintResult.accepted;
            string reason = hintResult?.reason ?? LessonCommandReasonV2.UnsupportedCapability;
            LessonStateV2 state;
            lock (_gate)
            {
                if (childScopeLease != 0L)
                {
                    if (!IsActiveQuestScopeLocked(_activeQuestScope) || _activeQuestScope.LeaseId != childScopeLease ||
                        _activeRunId != command.run_id || _activeActivationId != command.activation_id ||
                        _currentState?.node_id != command.node_id)
                    {
                        accepted = false;
                        reason = LessonCommandReasonV2.StaleActivation;
                    }
                }
                else if (!accepted && (_activeRunId != command.run_id || _activeActivationId != command.activation_id ||
                    _currentState?.node_id != command.node_id || _currentState?.status != "running" ||
                    _nodeResultCommitted || !_executorReady || !ReferenceEquals(_activeExecutor, executor)))
                {
                    reason = LessonCommandReasonV2.StaleActivation;
                }
                state = CloneState(_currentState);
            }
            return PublishDecision(CreateCommandResult(command, accepted, accepted ? LessonCommandReasonV2.None : reason, state));
        }

        private bool IsPauseRequested(string runId, string activationId)
        {
            lock (_gate)
                return _activeRunId == runId && _activeActivationId == activationId && _pauseRequestedFlag;
        }

        private void CompletePauseAfterCleanup(string runId, string activationId)
        {
            LessonStateV2 state;
            NodeCancelledEventV2 cancelledEvent;
            TaskCompletionSource<LessonStateV2> pauseCompletion;
            QuestNodeExecutor pauseExecutor;
            string pauseQuestActivationId;
            lock (_gate)
            {
                if (_activeRunId != runId || _activeActivationId != activationId || !_pauseRequestedFlag) return;
                cancelledEvent = CreateNodeCancelledLocked(LessonCommandKindV2.Pause);
                _activationCancellation?.Dispose(); _activationCancellation = null;
                _skipCancellation?.Dispose(); _skipCancellation = null;
                _timeoutCancellation?.Dispose(); _timeoutCancellation = null;
                _activeExecutor = null;
                _activeExecutionTask = null;
                _executorReady = false;
                state = SetStateLocked("paused", runId, _activeNode?.Id, _activeNode?.NodeType.ToString(),
                    NodeIndex(_activeNode?.Id), activationId);
                pauseCompletion = _pauseCompleted;
                pauseExecutor = _pauseQuestExecutor;
                _pauseQuestExecutor = null;
                pauseQuestActivationId = _pauseQuestActivationId ?? activationId;
                _pauseQuestActivationId = null;
            }
            if (cancelledEvent != null) Emit(NodeCancelled, cancelledEvent);
            Emit(StateChanged, CloneState(state));
            pauseExecutor?.RearmAfterPause(pauseQuestActivationId);
            pauseCompletion?.TrySetResult(CloneState(state));
        }

        private async Task<string> WaitForResumeOrCancellationAsync(string runId, CancellationToken cancellationToken)
        {
            TaskCompletionSource<string> resume;
            lock (_gate)
            {
                if (_activeRunId != runId) return null;
                resume = _resumeRequested;
            }
            if (resume == null) return null;
            using (var abort = new CancellationSignal(cancellationToken))
            {
                await Task.WhenAny(resume.Task, abort.Task);
            }
            if (cancellationToken.IsCancellationRequested || !resume.Task.IsCompleted) return null;
            return await resume.Task;
        }

        private LessonResult FinishAborted(string runId)
        {
            SetTerminalState(runId, "cancelled");
            var result = LessonResult.Failed(runId, LessonFailureReason.Aborted, _clock.ElapsedSeconds);
            EmitLessonCompletedOnce(runId, result);
            return result;
        }

        private LessonResult FinishLesson(string runId, LessonResult result, string status)
        {
            SetTerminalState(runId, status);
            EmitLessonCompletedOnce(runId, result);
            return result;
        }

        private void SetTerminalState(string runId, string status)
        {
            LessonStateV2 state = null;
            lock (_gate)
            {
                if (_activeRunId != runId || IsTerminal(_currentState?.status)) return;
                state = SetStateLocked(status, runId, _currentState?.node_id, _currentState?.node_type,
                    _currentState?.node_index ?? -1, _currentState?.activation_id);
                _pauseCompleted?.TrySetResult(CloneState(state));
                _resumeStarted?.TrySetResult(CloneState(state));
            }
            Emit(StateChanged, CloneState(state));
        }

        private void EmitLessonCompletedOnce(string runId, LessonResult result)
        {
            lock (_gate)
            {
                if (_activeRunId != runId || _lessonCompletedEmitted) return;
                _lessonCompletedEmitted = true;
            }
            Emit(LessonCompleted, new LessonCompletedEvent(result));
        }

        private NodeCancelledEventV2 CreateNodeCancelledLocked(string reason)
        {
            if (_nodeCancellationEmitted || _nodeResultCommitted || _activeNode == null || string.IsNullOrEmpty(_activeActivationId)) return null;
            _nodeCancellationEmitted = true;
            return new NodeCancelledEventV2(_activeRunId, _activeNode.Id, _activeActivationId, reason,
                _clock.ElapsedSeconds);
        }

        private LessonStateV2 SetStateLocked(string status, string runId, string nodeId, string nodeType, int nodeIndex, string activationId)
        {
            var context = _sessionContext;
            _currentState = new LessonStateV2
            {
                contract_version = LessonRemoteContractV2.ContractVersion,
                session_id = context?.SessionId ?? string.Empty,
                run_id = runId ?? string.Empty,
                graph_id = _graph?.name ?? string.Empty,
                lesson_id = context?.LessonId ?? _graph?.name ?? string.Empty,
                launch_token = context?.LaunchToken ?? string.Empty,
                lesson_voice_revision = context?.LessonVoiceRevision ?? 0,
                child_phrase_revision = context?.ChildPhraseRevision ?? 0,
                node_id = nodeId ?? string.Empty,
                node_type = nodeType ?? string.Empty,
                node_index = nodeIndex,
                activation_id = activationId ?? string.Empty,
                status = status,
                checkpoint_id = string.Empty,
                updated_at_utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                state_revision = ++_stateRevision,
                active_node_ids = status == "running" || status == "pausing" || status == "paused"
                    ? (string.IsNullOrEmpty(nodeId) ? Array.Empty<string>() : new[] { nodeId })
                    : Array.Empty<string>(),
                parallel_group_id = string.Empty,
                bindings = status == "running" || status == "pausing"
                    ? (_activeExecutor as QuestNodeExecutor)?.GetActiveBindings(activationId) ?? Array.Empty<LessonBindingV2>()
                    : Array.Empty<LessonBindingV2>()
            };
            return CloneState(_currentState);
        }

        private int NodeIndex(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || _graph == null) return -1;
            for (var index = 0; index < _graph.Nodes.Count; index++)
                if (_graph.Nodes[index]?.Id == nodeId) return index;
            return -1;
        }

        private static LessonStateV2 CloneState(LessonStateV2 state)
        {
            if (state == null) return null;
            return new LessonStateV2
            {
                contract_version = state.contract_version,
                session_id = state.session_id,
                run_id = state.run_id,
                graph_id = state.graph_id,
                lesson_id = state.lesson_id,
                launch_token = state.launch_token,
                lesson_voice_revision = state.lesson_voice_revision,
                child_phrase_revision = state.child_phrase_revision,
                node_id = state.node_id,
                node_type = state.node_type,
                node_index = state.node_index,
                activation_id = state.activation_id,
                status = state.status,
                checkpoint_id = state.checkpoint_id,
                updated_at_utc = state.updated_at_utc,
                state_revision = state.state_revision,
                active_node_ids = state.active_node_ids == null ? Array.Empty<string>() : (string[])state.active_node_ids.Clone(),
                parallel_group_id = state.parallel_group_id,
                bindings = state.bindings == null ? Array.Empty<LessonBindingV2>() : state.bindings.Select(binding => binding == null ? null : new LessonBindingV2
                {
                    binding_id = binding.binding_id,
                    npc_binding_id = binding.npc_binding_id,
                    can_verbal_hint = binding.can_verbal_hint,
                    can_visual_hint = binding.can_visual_hint
                }).ToArray()
            };
        }

        private static LessonCommandResultV2 CreateCommandResult(LessonCommandV2 command, bool accepted, string reason, LessonStateV2 state)
        {
            return new LessonCommandResultV2
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
                reason = reason,
                state = CloneState(state)
            };
        }

        private static bool IsTerminal(string status) => status == "completed" || status == "failed" || status == "cancelled";

        private static void CancelActive(CancellationTokenSource source)
        {
            try { source?.Cancel(); } catch (ObjectDisposedException) { }
        }

        private static void ObserveFault(Task task)
        {
            task.ContinueWith(completed => {
                if (completed.Exception != null)
                    Debug.LogError($"[LessonGraphV2] Executor task faulted: {completed.Exception}");
            },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private sealed class CancellationSignal : IDisposable
        {
            private readonly CancellationTokenRegistration _registration;
            private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task Task => _completion.Task;

            public CancellationSignal(CancellationToken token)
            {
                if (token.CanBeCanceled) _registration = token.Register(() => _completion.TrySetResult(true));
            }

            public void Dispose() => _registration.Dispose();
        }
        private static void Emit<T>(Action<T> eventHandler, T payload)
        {
            if (eventHandler == null) return;
            foreach (var subscriber in eventHandler.GetInvocationList())
            {
                if (subscriber is Action<T> handler)
                {
                    try { handler(payload); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
            }
        }

        private void OnDisable() => AbortLesson();
        private void OnDestroy() => AbortLesson();
    }
}
