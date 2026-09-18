using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Dialogue;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime.Executors
{
    public sealed class DialogueNodeExecutor : INodeExecutor
    {
        private readonly IDialogueTransportV2 _transport;
        private readonly INodeClock _clock;

        public DialogueNodeExecutor(IDialogueTransportV2 transport, INodeClock clock = null)
        {
            _transport = transport;
            _clock = clock;
        }

        public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            if (context == null || context.Node == null || !(context.Node.Config is DialogueNodeConfig config) || _transport == null)
            {
                Debug.LogWarning($"[LessonGraphV2] DialogueExecutor: invalid config or missing transport node={context?.Node?.Id}");
                return Result(context, NodeStatus.Failed, "invalid_dialogue");
            }

            if (string.IsNullOrWhiteSpace(context?.ActivationId))
            {
                Debug.LogWarning($"[LessonGraphV2] DialogueExecutor: null or whitespace activationId for node={context?.Node?.Id}");
                return Result(context, NodeStatus.Failed, "invalid_activation");
            }

            if (string.IsNullOrWhiteSpace(config.SequenceId) ||
                string.IsNullOrWhiteSpace(config.Text) ||
                string.IsNullOrWhiteSpace(config.NpcBindingId))
            {
                Debug.LogWarning($"[LessonGraphV2] DialogueExecutor: invalid dialogue config parameters for node={context.Node.Id}");
                return Result(context, NodeStatus.Failed, "invalid_dialogue");
            }

            Debug.Log($"[LessonGraphV2] DialogueExecutor: START node={context.Node.Id} seq={config.SequenceId} npc={config.NpcBindingId} blocking={config.Blocking}");

            if (!config.Blocking)
            {
                try
                {
                    await _transport.SpeakAsync(
                        new DialogueRequestV2(context.ActivationId, config.SequenceId, config.Text, config.NpcBindingId),
                        context.CancellationToken);
                    return Result(context, NodeStatus.Success, "non_blocking");
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LessonGraphV2] DialogueExecutor: non-blocking speak exception node={context.Node.Id}: {ex}");
                    return Result(context, NodeStatus.Failed, "speak_exception");
                }
            }

            var completion = new TaskCompletionSource<NodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var settled = 0;

            Action<DialogueSignalV2> handler = signal =>
            {
                if (signal == null ||
                    !string.Equals(signal.ActivationId, context.ActivationId, StringComparison.Ordinal) ||
                    !string.Equals(signal.SequenceId, config.SequenceId, StringComparison.Ordinal))
                {
                    return;
                }

                var status = signal.Type == DialogueSignalType.Done ? NodeStatus.Success :
                    signal.Type == DialogueSignalType.Cancelled ? NodeStatus.Skipped : NodeStatus.Failed;

                if (Interlocked.CompareExchange(ref settled, 1, 0) == 0)
                {
                    Debug.Log($"[LessonGraphV2] DialogueExecutor: SIGNAL RECEIVED node={context.Node.Id} status={status} reason={signal.Reason}");
                    completion.TrySetResult(Result(context, status, string.IsNullOrEmpty(signal.Reason) ? signal.Type.ToString().ToLowerInvariant() : signal.Reason));
                }
            };

            _transport.SignalReceived += handler;
            try
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                await _transport.SpeakAsync(
                    new DialogueRequestV2(context.ActivationId, config.SequenceId, config.Text, config.NpcBindingId),
                    context.CancellationToken);

                var clock = context.Clock ?? _clock;
                var timeoutTask = config.TimeoutSeconds <= 0f || clock == null
                    ? Never()
                    : clock.Delay(config.TimeoutSeconds, context.CancellationToken);

                using (var abort = new CancellationSignal(context.CancellationToken))
                using (var skip = new CancellationSignal(context.SkipToken))
                using (var timeout = new CancellationSignal(context.TimeoutToken))
                {
                    var winner = await Task.WhenAny(completion.Task, abort.Task, skip.Task, timeout.Task, timeoutTask);
                    context.CancellationToken.ThrowIfCancellationRequested();

                    if (winner == skip.Task)
                    {
                        Debug.Log($"[LessonGraphV2] DialogueExecutor: skip won race node={context.Node.Id}");
                        Claim(completion, ref settled, Result(context, NodeStatus.Skipped, "skip"));
                        try { await _transport.CancelAsync(context.ActivationId, config.SequenceId, "skip", CancellationToken.None); } catch { }
                    }
                    else if (winner == timeout.Task || winner == timeoutTask)
                    {
                        Debug.Log($"[LessonGraphV2] DialogueExecutor: timeout won race node={context.Node.Id}");
                        Claim(completion, ref settled, Result(context, NodeStatus.Timeout, "timeout"));
                        try { await _transport.CancelAsync(context.ActivationId, config.SequenceId, "timeout", CancellationToken.None); } catch { }
                    }

                    return await completion.Task;
                }
            }
            finally
            {
                Interlocked.Exchange(ref settled, 1);
                _transport.SignalReceived -= handler;
                if (!completion.Task.IsCompleted)
                {
                    try { await _transport.CancelAsync(context.ActivationId, config.SequenceId, "teardown", CancellationToken.None); } catch { }
                }
            }
        }

        private static void Claim(TaskCompletionSource<NodeResult> completion, ref int settled, NodeResult result)
        {
            if (Interlocked.CompareExchange(ref settled, 1, 0) == 0)
                completion.TrySetResult(result);
        }

        private static NodeResult Result(NodeExecutionContext context, NodeStatus status, string channel) =>
            NodeResult.Completed(context?.Node?.Id, context?.ActivationId, status, Elapsed(context), channel);

        private static double Elapsed(NodeExecutionContext context) =>
            context?.Clock?.ElapsedSeconds ?? context?.ElapsedSeconds ?? 0d;

        private static Task Never() =>
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        private sealed class CancellationSignal : IDisposable
        {
            private readonly CancellationTokenRegistration _registration;
            private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task Task => _completion.Task;

            public CancellationSignal(CancellationToken token)
            {
                if (token.CanBeCanceled)
                    _registration = token.Register(() => _completion.TrySetResult(true));
            }

            public void Dispose() => _registration.Dispose();
        }
    }
}
