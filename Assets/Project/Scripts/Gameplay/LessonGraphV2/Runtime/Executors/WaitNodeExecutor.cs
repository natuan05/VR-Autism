using System;
using System.Threading;
using System.Threading.Tasks;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime.Executors
{
    public sealed class WaitNodeExecutor : INodeExecutor
    {
        private readonly INodeClock _clock;
        public WaitNodeExecutor(INodeClock clock) { _clock = clock ?? throw new ArgumentNullException(nameof(clock)); }

        public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            if (context == null || context.Node == null || !(context.Node.Config is WaitNodeConfig config))
            {
                UnityEngine.Debug.LogWarning($"[LessonGraphV2] WaitExecutor: invalid config node={context?.Node?.Id}");
                return NodeResult.Completed(context?.Node?.Id, context?.ActivationId, NodeStatus.Failed, context?.ElapsedSeconds ?? 0d, "invalid_wait");
            }

            UnityEngine.Debug.Log($"[LessonGraphV2] WaitExecutor: START node={context.Node.Id} duration={config.Duration}s");

            using (var skip = new CancellationSignal(context.SkipToken))
            using (var timeout = new CancellationSignal(context.TimeoutToken))
            {
                Task delay;
                try { delay = _clock.Delay(config.Duration, context.CancellationToken); }
                catch (Exception)
                {
                    UnityEngine.Debug.LogError($"[LessonGraphV2] WaitExecutor: timer exception node={context.Node.Id}");
                    return NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Failed, Elapsed(context), "timer_exception");
                }

                var winner = await Task.WhenAny(delay, skip.Task, timeout.Task);
                context.CancellationToken.ThrowIfCancellationRequested();
                if (winner == skip.Task)
                {
                    UnityEngine.Debug.Log($"[LessonGraphV2] WaitExecutor: SKIPPED node={context.Node.Id}");
                    return NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Skipped, Elapsed(context), "skip");
                }
                if (winner == timeout.Task)
                {
                    UnityEngine.Debug.Log($"[LessonGraphV2] WaitExecutor: TIMEOUT node={context.Node.Id}");
                    return NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Timeout, Elapsed(context), "timeout");
                }

                try { await delay; }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    UnityEngine.Debug.LogError($"[LessonGraphV2] WaitExecutor: timer exception node={context.Node.Id}");
                    return NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Failed, Elapsed(context), "timer_exception");
                }
                UnityEngine.Debug.Log($"[LessonGraphV2] WaitExecutor: DONE node={context.Node.Id} status=Success");
                return NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Success, Elapsed(context), "duration");
            }
        }

        private static double Elapsed(NodeExecutionContext context) => context.Clock?.ElapsedSeconds ?? context.ElapsedSeconds;
        private sealed class CancellationSignal : IDisposable
        {
            private readonly CancellationTokenRegistration _registration;
            // Complete inline so Unity's main-thread synchronization context observes
            // cancellation immediately while the executor is awaiting Task.WhenAny.
            private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();
            public Task Task => _completion.Task;

            public CancellationSignal(CancellationToken token)
            {
                if (token.CanBeCanceled) _registration = token.Register(() => _completion.TrySetResult(true));
            }

            public void Dispose() => _registration.Dispose();
        }
    }
}
