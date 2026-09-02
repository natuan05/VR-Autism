using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime.Executors
{
    public sealed class CheckpointNodeExecutor : INodeExecutor
    {
        private readonly ICheckpointTelemetry _telemetry;
        private readonly HashSet<string> _emittedActivations = new HashSet<string>();
        private readonly object _gate = new object();

        public CheckpointNodeExecutor(ICheckpointTelemetry telemetry = null) { _telemetry = telemetry; }

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            if (context == null || context.Node == null || !(context.Node.Config is CheckpointNodeConfig config))
                return Task.FromResult(NodeResult.Completed(context?.Node?.Id, context?.ActivationId, NodeStatus.Failed, context?.ElapsedSeconds ?? 0d, "invalid_checkpoint"));

            try
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var key = context.RunId + ":" + context.ActivationId;
                var shouldEmit = false;
                lock (_gate) shouldEmit = config.EmitTelemetry && _emittedActivations.Add(key);
                if (shouldEmit)
                    (context.CheckpointTelemetry ?? _telemetry)?.Record(new CheckpointMarker(context.RunId, context.ActivationId, context.GraphId, context.Node.Id, config.CheckpointId, Elapsed(context)));
                context.CancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Success, Elapsed(context), "checkpoint"));
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<NodeResult>(context.CancellationToken);
            }
            catch (Exception)
            {
                return Task.FromResult(NodeResult.Completed(context.Node.Id, context.ActivationId, NodeStatus.Failed, Elapsed(context), "telemetry_exception"));
            }
        }
        private static double Elapsed(NodeExecutionContext context) => context.Clock?.ElapsedSeconds ?? context.ElapsedSeconds;
    }
}