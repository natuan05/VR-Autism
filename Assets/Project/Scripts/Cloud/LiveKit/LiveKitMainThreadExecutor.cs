using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VRAutism.Cloud.LiveKit
{
    internal sealed class LiveKitMainThreadExecutor
    {
        private readonly object _gate = new object();
        private readonly Queue<WorkItem> _queue = new Queue<WorkItem>();
        private readonly int _ownerThreadId;
        private readonly SynchronizationContext _ownerSynchronizationContext;
        private long _generation;
        private bool _closed;

        public LiveKitMainThreadExecutor()
        {
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            _ownerSynchronizationContext = SynchronizationContext.Current;
        }

        internal bool IsOwnerThread => Thread.CurrentThread.ManagedThreadId == _ownerThreadId;

        internal bool Post(long generation, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            lock (_gate)
            {
                if (_closed) return false;
                _queue.Enqueue(new WorkItem(generation, action, true));
                return true;
            }
        }

        internal Task PostAsync(long generation, CancellationToken cancellationToken, Func<Task> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            var completion = new TaskCompletionSource<bool>();
            if (IsOwnerThread)
            {
                lock (_gate)
                {
                    if (_closed)
                    {
                        completion.TrySetCanceled();
                        return completion.Task;
                    }
                }

                RunAsyncOperation(generation, operation, completion);
                return completion.Task;
            }

            lock (_gate)
            {
                if (_closed)
                {
                    completion.TrySetCanceled();
                    return completion.Task;
                }

                _queue.Enqueue(new WorkItem(
                    generation,
                    () => RunAsyncOperation(generation, operation, completion),
                    false));
            }

            return completion.Task;
        }

        internal void AdvanceGeneration(long generation)
        {
            EnsureOwnerThread(nameof(AdvanceGeneration));

            lock (_gate)
            {
                if (generation > _generation)
                {
                    _generation = generation;
                }
            }
        }

        internal void Drain()
        {
            EnsureOwnerThread(nameof(Drain));

            while (true)
            {
                WorkItem workItem;
                lock (_gate)
                {
                    if (_queue.Count == 0) return;
                    workItem = _queue.Dequeue();
                    if (workItem.GenerationFiltered && workItem.Generation != _generation)
                        continue;
                }

                var previousContext = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(
                    new ExecutorSynchronizationContext(this, workItem.Generation));
                try
                {
                    workItem.Action();
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previousContext);
                }
            }
        }

        internal void Close()
        {
            lock (_gate)
            {
                _closed = true;
                if (_queue.Count == 0)
                    return;

                var continuations = new Queue<WorkItem>();
                while (_queue.Count > 0)
                {
                    var workItem = _queue.Dequeue();
                    if (!workItem.GenerationFiltered)
                        continuations.Enqueue(workItem);
                }

                while (continuations.Count > 0)
                    _queue.Enqueue(continuations.Dequeue());
            }
        }

        private void RunAsyncOperation(
            long generation,
            Func<Task> operation,
            TaskCompletionSource<bool> completion)
        {
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(
                new ExecutorSynchronizationContext(this, generation));
            try
            {
                Task operationTask;
                try
                {
                    operationTask = operation();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                    return;
                }

                if (operationTask == null)
                {
                    completion.TrySetException(
                        new InvalidOperationException("Executor operation returned null."));
                    return;
                }

                operationTask.ContinueWith(
                    completedTask =>
                    {
                        if (completedTask.IsCanceled)
                            completion.TrySetCanceled();
                        else if (completedTask.IsFaulted)
                            completion.TrySetException(completedTask.Exception.InnerExceptions);
                        else
                            completion.TrySetResult(true);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        private void PostContinuation(long generation, SendOrPostCallback callback, object state)
        {
            SynchronizationContext ownerContext;
            lock (_gate)
            {
                // Continuations belong to an already-started phase. They must survive
                // generation rollover and Close long enough to run cancellation/finally.
                if (!_closed)
                {
                    _queue.Enqueue(new WorkItem(
                        generation,
                        () => RunContinuation(generation, callback, state),
                        false));
                    return;
                }

                ownerContext = _ownerSynchronizationContext;
            }

            // OnDestroy closes the executor and Unity will no longer call Update on the
            // destroyed component. Re-enter the captured Unity context so an in-flight
            // SDK await can still run its stale-phase cleanup and finally block.
            if (ownerContext != null)
            {
                ownerContext.Post(
                    _ => RunContinuation(generation, callback, state),
                    null);
                return;
            }

            if (IsOwnerThread)
            {
                RunContinuation(generation, callback, state);
                return;
            }

            lock (_gate)
            {
                _queue.Enqueue(new WorkItem(
                    generation,
                    () => RunContinuation(generation, callback, state),
                    false));
            }
        }

        private void RunContinuation(long generation, SendOrPostCallback callback, object state)
        {
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(
                new ExecutorSynchronizationContext(this, generation));
            try
            {
                callback(state);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        private void EnsureOwnerThread(string operation)
        {
            if (!IsOwnerThread)
            {
                throw new InvalidOperationException($"LiveKitMainThreadExecutor.{operation} must run on its owner thread.");
            }
        }

        private readonly struct WorkItem
        {
            public long Generation { get; }
            public Action Action { get; }
            public bool GenerationFiltered { get; }

            public WorkItem(long generation, Action action, bool generationFiltered)
            {
                Generation = generation;
                Action = action;
                GenerationFiltered = generationFiltered;
            }
        }

        private sealed class ExecutorSynchronizationContext : SynchronizationContext
        {
            private readonly LiveKitMainThreadExecutor _executor;
            private readonly long _generation;

            internal ExecutorSynchronizationContext(LiveKitMainThreadExecutor executor)
                : this(executor, 0)
            {
            }

            internal ExecutorSynchronizationContext(LiveKitMainThreadExecutor executor, long generation)
            {
                _executor = executor;
                _generation = generation;
            }

            public override void Post(SendOrPostCallback callback, object state)
            {
                if (callback == null) throw new ArgumentNullException(nameof(callback));
                _executor.PostContinuation(_generation, callback, state);
            }

            public override void Send(SendOrPostCallback callback, object state)
            {
                if (callback == null) throw new ArgumentNullException(nameof(callback));
                if (_executor.IsOwnerThread)
                {
                    callback(state);
                    return;
                }

                throw new InvalidOperationException(
                    "LiveKitMainThreadExecutor synchronization Send must run on its owner thread.");
            }
        }
    }
}
