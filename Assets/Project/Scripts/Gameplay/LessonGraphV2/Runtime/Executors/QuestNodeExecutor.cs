using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Questing;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime.Executors
{
    /// <summary>Activates scene-owned quest sources and settles a quest node exactly once.</summary>
    public sealed class QuestNodeExecutor : INodeExecutor
    {
        private readonly IQuestBindingResolver _resolver;
        private readonly INodeClock _clock;

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
                foreach (var source in sources) source.Terminated -= handler;
                foreach (var source in active)
                {
                    UnityEngine.Debug.Log($"[LessonGraphV2] QuestExecutor: cancelling loser source='{source.BindingId}'");
                    source.TryCancel(new QuestSourceCancellation(context.ActivationId, "first_win"));
                }
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
