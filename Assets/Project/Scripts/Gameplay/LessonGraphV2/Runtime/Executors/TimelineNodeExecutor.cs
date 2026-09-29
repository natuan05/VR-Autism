using System;
using System.Threading;
using System.Threading.Tasks;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime.Executors
{
    public interface ITimelinePlaybackController
    {
        ITimelinePlaybackSession StartPlayback(TimelineNodeConfig config);
    }

    public interface ITimelinePlaybackSession : IDisposable
    {
        Task SignalTask { get; }
        bool IsCancelled { get; }
    }

    /// <summary>Executes a Timeline node and maps its signal or cancellation outcome into one result.</summary>
    public sealed class TimelineNodeExecutor : INodeExecutor
    {
        private readonly ITimelinePlaybackController _playback;
        private readonly INodeClock _clock;

        public TimelineNodeExecutor(ITimelinePlaybackController playback, INodeClock clock = null)
        {
            _playback = playback;
            _clock = clock;
        }

        public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            if (context == null || context.Node == null || !(context.Node.Config is TimelineNodeConfig config) ||
                _playback == null || string.IsNullOrWhiteSpace(context.ActivationId) ||
                config.TimelineAsset == null || string.IsNullOrWhiteSpace(config.ExpectedSignalName) ||
                config.TimeoutSeconds <= 0f || float.IsNaN(config.TimeoutSeconds) || float.IsInfinity(config.TimeoutSeconds))
                return Result(context, NodeStatus.Failed, "invalid_timeline");

            ITimelinePlaybackSession session = null;
            using (var abort = new CancellationSignal(context.CancellationToken))
            using (var skip = new CancellationSignal(context.SkipToken))
            using (var externalTimeout = new CancellationSignal(context.TimeoutToken))
            using (var localTimeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken))
            {
                try
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    if (context.SkipToken.IsCancellationRequested)
                        return Result(context, NodeStatus.Skipped, "skip");
                    if (context.TimeoutToken.IsCancellationRequested)
                        return TimeoutResult(context, config);
                    session = _playback.StartPlayback(config);
                    if (session == null || session.SignalTask == null)
                        return Result(context, NodeStatus.Failed, "playback_unavailable");

                    var clock = context.Clock ?? _clock;
                    if (clock == null)
                        return Result(context, NodeStatus.Failed, "clock_unavailable");

                    var timeoutTask = clock.Delay(config.TimeoutSeconds, localTimeoutCancellation.Token);
                    var winner = await Task.WhenAny(session.SignalTask, timeoutTask, abort.Task, skip.Task, externalTimeout.Task);
                    context.CancellationToken.ThrowIfCancellationRequested();
                    if (session.IsCancelled) throw new OperationCanceledException();
                    if (context.SkipToken.IsCancellationRequested || winner == skip.Task)
                        return Result(context, NodeStatus.Skipped, "skip");
                    if (context.TimeoutToken.IsCancellationRequested || winner == externalTimeout.Task ||
                        winner == timeoutTask || timeoutTask.IsCompleted)
                        return TimeoutResult(context, config);

                    if (!session.SignalTask.IsCompleted || session.SignalTask.IsCanceled || session.SignalTask.IsFaulted)
                    {
                        if (session.SignalTask.IsCanceled) throw new OperationCanceledException();
                        return Result(context, NodeStatus.Failed, "playback_ended");
                    }

                    // Cancellation tokens are checked again immediately before claiming success.
                    context.CancellationToken.ThrowIfCancellationRequested();
                    if (session.IsCancelled) throw new OperationCanceledException();
                    if (context.SkipToken.IsCancellationRequested) return Result(context, NodeStatus.Skipped, "skip");
                    if (context.TimeoutToken.IsCancellationRequested) return TimeoutResult(context, config);
                    return Result(context, NodeStatus.Success, "signal");
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (session?.IsCancelled == true || session?.SignalTask.IsCanceled == true)
                {
                    throw;
                }
                catch (Exception)
                {
                    return Result(context, NodeStatus.Failed, "playback_exception");
                }
                finally
                {
                    localTimeoutCancellation.Cancel();
                    session?.Dispose();
                }
            }
        }

        private static NodeResult TimeoutResult(NodeExecutionContext context, TimelineNodeConfig config) =>
            Result(context, config.TimeoutOutcome == TimelineTimeoutOutcome.Failed ? NodeStatus.Failed : NodeStatus.Timeout, "timeout");

        private static NodeResult Result(NodeExecutionContext context, NodeStatus status, string channel) =>
            NodeResult.Completed(context?.Node?.Id, context?.ActivationId, status,
                context?.Clock?.ElapsedSeconds ?? context?.ElapsedSeconds ?? 0d, channel);

        private sealed class CancellationSignal : IDisposable
        {
            private readonly CancellationTokenRegistration _registration;
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
